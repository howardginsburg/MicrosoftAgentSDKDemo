using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Provides intelligent tool routing by analyzing user queries and selecting only relevant tool categories.
/// This helps stay within Azure OpenAI's 128 tool limit while providing access to many tools.
/// </summary>
public interface IToolRoutingService
{
    /// <summary>
    /// Analyzes the user's query and returns only the tools that are relevant.
    /// </summary>
    Task<IList<AITool>> SelectRelevantToolsAsync(string userQuery, IDictionary<string, IList<AITool>> toolsByCategory);
}

public class ToolRoutingService : IToolRoutingService
{
    private readonly ILogger<ToolRoutingService> _logger;
    private readonly IChatClient _chatClient;
    private const int MaxTools = 128;

    public ToolRoutingService(
        ILogger<ToolRoutingService> logger,
        AzureOpenAIClient azureOpenAIClient,
        string deploymentName)
    {
        _logger = logger;
        _chatClient = azureOpenAIClient.GetChatClient(deploymentName).AsIChatClient();
    }

    public async Task<IList<AITool>> SelectRelevantToolsAsync(string userQuery, IDictionary<string, IList<AITool>> toolsByCategory)
    {
        var totalTools = toolsByCategory.Values.Sum(t => t.Count);
        
        // If total tools are under the limit, return all
        if (totalTools <= MaxTools)
        {
            _logger.LogDebug("Total tools ({TotalTools}) within limit, using all", totalTools);
            return toolsByCategory.Values.SelectMany(t => t).ToList();
        }

        _logger.LogDebug("Total tools ({TotalTools}) exceeds limit ({MaxTools}), using reasoning to select categories", 
            totalTools, MaxTools);

        // Build category descriptions for the model
        var categoryDescriptions = toolsByCategory.Select(kvp => 
        {
            var sampleTools = string.Join(", ", kvp.Value.Take(5).Select(t => t.Name));
            var more = kvp.Value.Count > 5 ? $" and {kvp.Value.Count - 5} more" : "";
            return $"- {kvp.Key} ({kvp.Value.Count} tools): {sampleTools}{more}";
        });

        var routingPrompt = $@"You are a tool routing assistant. Based on the user's query, determine which tool categories are needed.

Available tool categories:
{string.Join("\n", categoryDescriptions)}

User query: ""{userQuery}""

Respond with ONLY a comma-separated list of category names that are needed to answer this query.
Be selective - only include categories that are directly relevant.
If no specific tools are needed (general knowledge question), respond with ""NONE"".

Example responses:
- For ""what Azure services are available?"": Azure MCP, Microsoft Learn
- For ""list my pull requests"": Azure DevOps
- For ""generate an image of a cat"": Image Generation
- For ""what is the capital of France?"": NONE";

        try
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "You are a concise tool routing assistant. Only output category names, nothing else."),
                new ChatMessage(ChatRole.User, routingPrompt)
            };

            var response = await _chatClient.GetResponseAsync(messages);
            var selectedCategories = response.Messages.LastOrDefault()?.Text ?? "NONE";
            
            _logger.LogInformation("Tool routing decision: {SelectedCategories}", selectedCategories);

            if (selectedCategories.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                // No tools needed - return empty or minimal set
                _logger.LogDebug("No tools needed for this query");
                return new List<AITool>();
            }

            // Parse selected categories
            var categoryNames = selectedCategories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Collect tools from selected categories
            var selectedTools = new List<AITool>();
            foreach (var kvp in toolsByCategory)
            {
                // Fuzzy match category names (handle slight variations)
                if (categoryNames.Any(cn => 
                    kvp.Key.Contains(cn, StringComparison.OrdinalIgnoreCase) || 
                    cn.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedTools.AddRange(kvp.Value);
                    _logger.LogDebug("Including {ToolCount} tools from category '{Category}'", 
                        kvp.Value.Count, kvp.Key);
                }
            }

            // Final limit check - if still over, prioritize
            if (selectedTools.Count > MaxTools)
            {
                _logger.LogWarning("Selected tools ({Count}) still exceeds limit, truncating to {MaxTools}", 
                    selectedTools.Count, MaxTools);
                selectedTools = selectedTools.Take(MaxTools).ToList();
            }

            _logger.LogInformation("Tool routing selected {SelectedCount} tools from {CategoryCount} categories", 
                selectedTools.Count, categoryNames.Count);

            return selectedTools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool routing failed, falling back to truncated tool list");
            // Fallback: return first MaxTools tools
            return toolsByCategory.Values.SelectMany(t => t).Take(MaxTools).ToList();
        }
    }
}

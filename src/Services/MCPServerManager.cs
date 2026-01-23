using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Represents a tool definition that the AI agent can use.
/// Wraps a delegate function with metadata for agent discovery.
/// </summary>
public class AITool
{
    public Delegate Function { get; set; }
    public string Name { get; set; }

    public AITool(Delegate function, string name)
    {
        Function = function;
        Name = name;
    }
}

/// <summary>
/// Provides integration with Microsoft Learn documentation.
/// Acts as a bridge to search and retrieve Microsoft Learn content for agent use.
/// </summary>
public interface IMCPServerManager
{
    /// <summary>
    /// Gets AITool instances for Microsoft Learn documentation access.
    /// These tools can be used by agents via the AsAIAgent() pattern.
    /// </summary>
    Task<IList<AITool>> GetMicrosoftLearnToolsAsync();
}

public class MCPServerManager : IMCPServerManager
{
    private readonly ILogger<MCPServerManager> _logger;

    public MCPServerManager(ILogger<MCPServerManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Provides AITool instances for Microsoft Learn documentation search and retrieval.
    /// </summary>
    public async Task<IList<AITool>> GetMicrosoftLearnToolsAsync()
    {
        try
        {
            var tools = new List<AITool>
            {
                new AITool(SearchMicrosoftLearn, nameof(SearchMicrosoftLearn)),
                new AITool(GetDocumentation, nameof(GetDocumentation)),
                new AITool(SearchAzureDocumentation, nameof(SearchAzureDocumentation))
            };

            _logger.LogInformation("Created {ToolCount} Microsoft Learn tools for agent", tools.Count);
            return await Task.FromResult(tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Microsoft Learn tools");
            throw;
        }
    }

    private string SearchMicrosoftLearn(
        [Description("The search query or topic to search for")] string query)
    {
        _logger.LogInformation("MCP Tool Invoked: SearchMicrosoftLearn | Query: {Query}", query);
        try
        {
            // In a production implementation, this would call the Microsoft Learn API
            // For now, return a placeholder indicating the agent would search Learn
            var result = $"Searching Microsoft Learn for: {query}\n(In production, this would query learn.microsoft.com)";
            _logger.LogDebug("SearchMicrosoftLearn completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchMicrosoftLearn failed");
            throw;
        }
    }

    private string GetDocumentation(
        [Description("The specific documentation topic or URL path")] string topic,
        [Description("Optional query parameters for filtering")] string? filter = null)
    {
        _logger.LogInformation("MCP Tool Invoked: GetDocumentation | Topic: {Topic} | Filter: {Filter}", topic, filter ?? "(none)");
        try
        {
            // In a production implementation, this would fetch actual documentation
            var filterInfo = !string.IsNullOrEmpty(filter) ? $" with filter: {filter}" : "";
            var result = $"Retrieving Microsoft Learn documentation for: {topic}{filterInfo}\n(In production, this would fetch from learn.microsoft.com)";
            _logger.LogDebug("GetDocumentation completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDocumentation failed");
            throw;
        }
    }

    private string SearchAzureDocumentation(
        [Description("The Azure service or topic to search for")] string service,
        [Description("Optional documentation category (e.g., 'tutorial', 'reference', 'how-to')")] string? category = null)
    {
        _logger.LogInformation("MCP Tool Invoked: SearchAzureDocumentation | Service: {Service} | Category: {Category}", 
            service, category ?? "(none)");
        try
        {
            // In a production implementation, this would call Azure documentation APIs
            var categoryInfo = !string.IsNullOrEmpty(category) ? $" in {category} category" : "";
            var result = $"Searching Azure documentation for: {service}{categoryInfo}\n(In production, this would query Azure Learn resources)";
            _logger.LogDebug("SearchAzureDocumentation completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchAzureDocumentation failed");
            throw;
        }
    }
}

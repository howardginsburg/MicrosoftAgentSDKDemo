using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Factory for creating agents with Azure OpenAI.
/// Demonstrates the Agent Framework pattern for multi-agent scenarios.
/// </summary>
public interface IAgentFactory
{
    Task<IAgentService> CreateAgentAsync(string userId);
}

public interface IAgentService
{
    Task<string> ProcessMessageAsync(string threadId, string userMessage);
}

public class ChatAgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly IThreadManager _threadManager;
    private readonly IMCPServerManager _mcpServerManager;
    private readonly ILogger<ChatAgentFactory> _logger;

    public ChatAgentFactory(
        IConfiguration configuration,
        IThreadManager threadManager,
        IMCPServerManager mcpServerManager,
        ILogger<ChatAgentFactory> logger)
    {
        _configuration = configuration;
        _threadManager = threadManager;
        _mcpServerManager = mcpServerManager;
        _logger = logger;
    }

    public async Task<IAgentService> CreateAgentAsync(string userId)
    {
        var openAIConfig = _configuration.GetSection("AzureOpenAI");
        var endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        var deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        var systemInstructions = openAIConfig["SystemInstructions"] ?? throw new InvalidOperationException("SystemInstructions not configured");

        var credential = new AzureCliCredential();
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        var chatClient = azureOpenAIClient.GetChatClient(deploymentName);

        // Load Microsoft Learn documentation tools from MCPServerManager
        IList<AITool> learnTools = [];
        try
        {
            learnTools = await _mcpServerManager.GetMicrosoftLearnToolsAsync();
            _logger.LogInformation("Agent will have access to {ToolCount} Microsoft Learn documentation tools", learnTools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Microsoft Learn tools, agent will continue without them");
        }

        _logger.LogDebug("Agent created | UserId: {UserId} | Deployment: {DeploymentName} | ToolCount: {ToolCount}",
            userId, deploymentName, learnTools.Count);
        
        return await Task.FromResult(new AzureOpenAIAgent(chatClient, systemInstructions, _threadManager, learnTools, _logger));
    }
}

public class AzureOpenAIAgent : IAgentService
{
    private readonly ChatClient _chatClient;
    private readonly string _systemInstructions;
    private readonly IThreadManager _threadManager;
    private readonly IList<AITool> _learnTools;
    private readonly ILogger<ChatAgentFactory> _logger;

    public AzureOpenAIAgent(
        ChatClient chatClient,
        string systemInstructions,
        IThreadManager threadManager,
        IList<AITool> learnTools,
        ILogger<ChatAgentFactory> logger)
    {
        _chatClient = chatClient;
        _systemInstructions = systemInstructions;
        _threadManager = threadManager;
        _learnTools = learnTools;
        _logger = logger;
    }

    public async Task<string> ProcessMessageAsync(string threadId, string userMessage)
    {
        // This is designed to be called from agents or direct invocation
        // The pattern allows for future multi-agent orchestration via Workflows
        var parts = threadId.Split('|');
        if (parts.Length != 2)
            throw new InvalidOperationException("Invalid thread ID format");

        var userId = parts[0];
        var actualThreadId = parts[1];

        try
        {
            var thread = await _threadManager.GetThreadAsync(userId, actualThreadId);
            var chatMessages = new List<ChatMessage>();

            // Add system instructions
            chatMessages.Add(new SystemChatMessage(_systemInstructions));

            // Add conversation history
            if (thread != null)
            {
                foreach (var msg in thread.Messages)
                {
                    if (msg.Role == "user")
                        chatMessages.Add(new UserChatMessage(msg.Content));
                    else if (msg.Role == "assistant")
                        chatMessages.Add(new AssistantChatMessage(msg.Content));
                }
            }

            // Add current message
            chatMessages.Add(new UserChatMessage(userMessage));

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = 0.7f,
                MaxOutputTokenCount = 2048
            };

            // Log tool availability
            if (_learnTools.Count > 0)
            {
                var toolNames = string.Join(", ", _learnTools.Select(t => t.Name));
                _logger.LogInformation("Processing message with available MCP tools: {ToolNames}", toolNames);
            }

            var response = await _chatClient.CompleteChatAsync(chatMessages, chatOptions);
            var assistantResponse = response.Value.Content[0].Text ?? "";

            // Save message exchange
            await _threadManager.SaveMessageAsync(userId, actualThreadId, "user", userMessage);
            await _threadManager.SaveMessageAsync(userId, actualThreadId, "assistant", assistantResponse);

            _logger.LogInformation("Message processed | ThreadId: {ThreadId}", actualThreadId);

            return assistantResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message | ThreadId: {ThreadId}", threadId);
            throw;
        }
    }
}



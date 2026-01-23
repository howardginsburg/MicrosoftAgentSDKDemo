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
    private readonly ILogger<ChatAgentFactory> _logger;

    public ChatAgentFactory(IConfiguration configuration, IThreadManager threadManager, ILogger<ChatAgentFactory> logger)
    {
        _configuration = configuration;
        _threadManager = threadManager;
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

        _logger.LogDebug("Agent created | UserId: {UserId} | Deployment: {DeploymentName}", userId, deploymentName);
        
        return await Task.FromResult(new AzureOpenAIAgent(chatClient, systemInstructions, _threadManager, _logger));
    }
}

public class AzureOpenAIAgent : IAgentService
{
    private readonly ChatClient _chatClient;
    private readonly string _systemInstructions;
    private readonly IThreadManager _threadManager;
    private readonly ILogger<ChatAgentFactory> _logger;

    public AzureOpenAIAgent(
        ChatClient chatClient,
        string systemInstructions,
        IThreadManager threadManager,
        ILogger<ChatAgentFactory> logger)
    {
        _chatClient = chatClient;
        _systemInstructions = systemInstructions;
        _threadManager = threadManager;
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

            var response = await _chatClient.CompleteChatAsync(chatMessages);
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



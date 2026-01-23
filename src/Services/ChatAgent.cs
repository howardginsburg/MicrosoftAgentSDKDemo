using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Chat agent that orchestrates conversations with Azure OpenAI.
/// Designed to be part of a multi-agent framework ecosystem.
/// </summary>
public interface IChatAgent
{
    Task<string> ChatAsync(string userId, string threadId, string userMessage);
}

public class ChatAgent : IChatAgent
{
    private readonly ChatClient _chatClient;
    private readonly IThreadManager _threadManager;
    private readonly ILogger<ChatAgent> _logger;
    private readonly string _deploymentName;
    private readonly string _endpoint;
    private readonly string _systemInstructions;

    public ChatAgent(
        IConfiguration configuration,
        IThreadManager threadManager,
        ILogger<ChatAgent> logger)
    {
        _threadManager = threadManager;
        _logger = logger;

        var openAIConfig = configuration.GetSection("AzureOpenAI");
        _endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        _deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        _systemInstructions = openAIConfig["SystemInstructions"] ?? throw new InvalidOperationException("SystemInstructions not configured");

        var credential = new AzureCliCredential();
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(_endpoint), credential);
        _chatClient = azureOpenAIClient.GetChatClient(_deploymentName);

        _logger.LogDebug("ChatAgent initialized with deployment: {DeploymentName}", _deploymentName);
    }

    public async Task<string> ChatAsync(string userId, string threadId, string userMessage)
    {
        try
        {
            _logger.LogInformation("Chat request | UserId: {UserId} | ThreadId: {ThreadId} | Message: {Message}",
                userId, threadId, userMessage);

            // Load conversation history
            var thread = await _threadManager.GetThreadAsync(userId, threadId);
            var chatMessages = new List<ChatMessage>();

            // Add system instructions
            chatMessages.Add(new SystemChatMessage(_systemInstructions));

            if (thread != null)
            {
                foreach (var msg in thread.Messages)
                {
                    if (msg.Role == "user")
                    {
                        chatMessages.Add(new UserChatMessage(msg.Content));
                    }
                    else if (msg.Role == "assistant")
                    {
                        chatMessages.Add(new AssistantChatMessage(msg.Content));
                    }
                }
            }

            // Add current user message
            chatMessages.Add(new UserChatMessage(userMessage));

            _logger.LogDebug("Sending request to Azure OpenAI | ThreadId: {ThreadId} | MessageCount: {MessageCount}",
                threadId, chatMessages.Count);

            // Call Azure OpenAI
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await _chatClient.CompleteChatAsync(chatMessages, new ChatCompletionOptions
            {
                Temperature = 0.7f,
                MaxOutputTokenCount = 2048
            });
            sw.Stop();

            var assistantResponse = response.Value.Content[0].Text ?? "";
            var promptTokens = response.Value.Usage.InputTokenCount;
            var completionTokens = response.Value.Usage.OutputTokenCount;

            _logger.LogDebug("Received response from Azure OpenAI | Tokens - Prompt: {PromptTokens}, Completion: {CompletionTokens} | Latency: {LatencyMs}ms",
                promptTokens, completionTokens, sw.ElapsedMilliseconds);

            // Save both user and assistant messages
            await _threadManager.SaveMessageAsync(userId, threadId, "user", userMessage);
            await _threadManager.SaveMessageAsync(userId, threadId, "assistant", assistantResponse);

            _logger.LogInformation("Agent response sent | UserId: {UserId} | ThreadId: {ThreadId} | CompletionTokens: {CompletionTokens} | LatencyMs: {LatencyMs}",
                userId, threadId, completionTokens, sw.ElapsedMilliseconds);

            return assistantResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during chat | UserId: {UserId} | ThreadId: {ThreadId}", userId, threadId);
            throw;
        }
    }
}


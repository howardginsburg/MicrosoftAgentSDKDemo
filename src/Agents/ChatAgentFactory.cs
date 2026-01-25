using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Display;
using MicrosoftAgentSDKDemo.Integration;
using MicrosoftAgentSDKDemo.Storage;

namespace MicrosoftAgentSDKDemo.Agents;

/// <summary>
/// Factory for creating Azure OpenAI-based agents
/// </summary>
public interface IAgentFactory
{
    Task<AIAgent> CreateAgentAsync(string userId);
    Task<string> GetGreetingAsync(string username);
}

public class ChatAgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMCPServerManager _mcpServerManager;
    private readonly IStorage _storage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ChatAgentFactory> _logger;
    private readonly IImageGenerationService _imageService;
    private readonly string _agentNameFormat;

    public ChatAgentFactory(
        IConfiguration configuration,
        IMCPServerManager mcpServerManager,
        IStorage storage,
        ILoggerFactory loggerFactory,
        ILogger<ChatAgentFactory> logger,
        IImageGenerationService imageService)
    {
        _configuration = configuration;
        _mcpServerManager = mcpServerManager;
        _storage = storage;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _imageService = imageService;
        _agentNameFormat = configuration["Application:AgentNameFormat"] ?? "Agent-{0}";
    }

    public async Task<AIAgent> CreateAgentAsync(string userId)
    {
        var openAIConfig = _configuration.GetSection("AzureOpenAI");
        var endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        var deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        var systemInstructionsPath = openAIConfig["SystemInstructionsFile"] ?? "prompts/system-instructions.txt";
        
        // Load system instructions from file
        var fullPath = Path.Combine(AppContext.BaseDirectory, systemInstructionsPath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"System instructions file not found: {fullPath}");
        }
        var systemInstructions = await File.ReadAllTextAsync(fullPath);
        _logger.LogDebug("Loaded system instructions from {Path} ({Length} characters)", systemInstructionsPath, systemInstructions.Length);

        var credential = new AzureCliCredential();
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);

        // Get MCP tools from all configured MCP servers
        var mcpTools = await _mcpServerManager.GetToolsAsync();
        
        if (mcpTools.Any())
        {
            _logger.LogDebug("Agent will have access to {ToolCount} MCP tools: {ToolNames}", 
                mcpTools.Count, string.Join(", ", mcpTools.Select(t => t.Name)));
        }
        else
        {
            _logger.LogWarning("No MCP tools available - agent will run without external tool access");
        }

        // Create image generation tool
        var imageGenerationTool = AIFunctionFactory.Create(
            async (string prompt, string size = "1024x1024", string quality = "standard") =>
            {
                var result = await _imageService.GenerateImageAsync(prompt, size, quality);
                // Return a structured message that includes BOTH the success message AND the path
                return $"[IMAGE_GENERATED]Saved to: {result.LocalPath}[/IMAGE_GENERATED]";
            },
            name: "generate_image",
            description: "Generates an image based on a text prompt using DALL-E. Use this when the user asks to create, generate, or draw an image.");

        // Combine all tools
        var allTools = new List<AITool>(mcpTools) { imageGenerationTool };

        // Create agent with all tools and chat message store for persistence
        var chatOptions = new ChatOptions
        {
            Instructions = systemInstructions,
            Tools = allTools.ToArray()
        };

        // Create base chat client
        var baseChatClient = azureOpenAIClient
            .GetChatClient(deploymentName)
            .AsIChatClient();

        // Wrap with reasoning display client to show agent thinking process
        var reasoningClient = new ReasoningChatClient(
            baseChatClient,
            _loggerFactory.CreateLogger<ReasoningChatClient>());

        // Convert to agent with chat message store for persistence
        var agent = reasoningClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = string.Format(_agentNameFormat, userId),
                ChatOptions = chatOptions,
                ChatMessageStoreFactory = (ctx, ct) => new ValueTask<ChatMessageStore>(
                    new CosmosDbChatMessageStore(
                        _storage,
                        userId,
                        ctx.SerializedState,
                        _loggerFactory.CreateLogger<CosmosDbChatMessageStore>(),
                        ctx.JsonSerializerOptions))
            });

        _logger.LogDebug("AIAgent created | UserId: {UserId} | Deployment: {DeploymentName} | ToolCount: {ToolCount}",
            userId, deploymentName, allTools.Count);
        
        return agent;
    }

    public async Task<string> GetGreetingAsync(string username)
    {
        var openAIConfig = _configuration.GetSection("AzureOpenAI");
        var endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        var deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        var agentName = _configuration["Application:AgentName"] ?? "Agent";

        var credential = new AzureCliCredential();
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        var chatClient = azureOpenAIClient.GetChatClient(deploymentName).AsIChatClient();

        var greetingPrompt = $"You are {agentName}. Greet the user named {username} warmly and briefly introduce yourself in 1-2 sentences. Be friendly and professional.";
        
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, greetingPrompt)
        };
        
        var response = await chatClient.GetResponseAsync(messages);
        
        _logger.LogDebug("Generated greeting for user: {Username}", username);
        
        var greetingText = response.Messages.LastOrDefault()?.Text ?? $"Hello {username}, I'm {agentName}. How can I assist you today?";
        return greetingText;
    }
}

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
    Task<AIAgent> CreateAgentAsync(string userId, string? userQuery = null);
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
    private readonly AzureOpenAIClient _azureOpenAIClient;
    private readonly string _deploymentName;
    private readonly string _systemInstructions;

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
        
        // Initialize Azure OpenAI client once
        var openAIConfig = configuration.GetSection("AzureOpenAI");
        var endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        _deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        var systemInstructionsPath = openAIConfig["SystemInstructionsFile"] ?? "prompts/system-instructions.txt";
        
        // Load system instructions from file
        var fullPath = Path.Combine(AppContext.BaseDirectory, systemInstructionsPath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"System instructions file not found: {fullPath}");
        }
        _systemInstructions = File.ReadAllText(fullPath);
        _logger.LogDebug("Loaded system instructions from {Path} ({Length} characters)", systemInstructionsPath, _systemInstructions.Length);
        
        // Create shared Azure OpenAI client with Azure CLI credentials
        var credential = new AzureCliCredential();
        _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        
        _logger.LogDebug("ChatAgentFactory initialized | Endpoint: {Endpoint} | Deployment: {DeploymentName}", 
            endpoint, _deploymentName);
    }

    public async Task<AIAgent> CreateAgentAsync(string userId, string? userQuery = null)
    {
        // Get MCP tools grouped by server/category
        var toolsByCategory = await _mcpServerManager.GetToolsByCategoryAsync();
        
        // Add image generation as its own category
        var imageGenerationTool = AIFunctionFactory.Create(
            async (string prompt, string size = "1024x1024", string quality = "standard") =>
            {
                var result = await _imageService.GenerateImageAsync(prompt, size, quality);
                return $"[IMAGE_GENERATED]Saved to: {result.LocalPath}[/IMAGE_GENERATED]";
            },
            name: "generate_image",
            description: "Generates an image based on a text prompt using DALL-E. Use this when the user asks to create, generate, or draw an image.");
        
        toolsByCategory["Image Generation"] = new List<AITool> { imageGenerationTool };

        // Use tool routing if we have a user query and too many tools
        IList<AITool> selectedTools;
        var totalTools = toolsByCategory.Values.Sum(t => t.Count);
        
        if (!string.IsNullOrEmpty(userQuery) && totalTools > 128)
        {
            var toolRouter = new ToolRoutingService(
                _loggerFactory.CreateLogger<ToolRoutingService>(),
                _azureOpenAIClient,
                _deploymentName);
            
            selectedTools = await toolRouter.SelectRelevantToolsAsync(userQuery, toolsByCategory);
            
            // Always ensure image generation is available if query mentions image/draw/generate
            if (ContainsImageKeywords(userQuery) && !selectedTools.Contains(imageGenerationTool))
            {
                selectedTools.Add(imageGenerationTool);
            }
        }
        else
        {
            // Under the limit or no query - use all tools
            selectedTools = toolsByCategory.Values.SelectMany(t => t).ToList();
        }

        if (selectedTools.Any())
        {
            _logger.LogDebug("Agent will have access to {ToolCount} tools: {ToolNames}", 
                selectedTools.Count, string.Join(", ", selectedTools.Take(10).Select(t => t.Name)) + 
                (selectedTools.Count > 10 ? $"... and {selectedTools.Count - 10} more" : ""));
        }
        else
        {
            _logger.LogInformation("No tools selected for this query - agent will use general knowledge");
        }

        // Create agent with selected tools and chat message store for persistence
        var chatOptions = new ChatOptions
        {
            Instructions = _systemInstructions,
            Tools = selectedTools.ToArray()
        };

        // Create base chat client from shared Azure OpenAI client
        var baseChatClient = _azureOpenAIClient
            .GetChatClient(_deploymentName)
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
            userId, _deploymentName, selectedTools.Count);
        
        return agent;
    }

    private static bool ContainsImageKeywords(string query)
    {
        var keywords = new[] { "image", "picture", "draw", "generate", "create", "illustration", "artwork", "photo" };
        return keywords.Any(kw => query.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> GetGreetingAsync(string username)
    {
        var agentName = _configuration["Application:AgentName"] ?? "Agent";

        // Use the shared Azure OpenAI client for greeting generation
        var chatClient = _azureOpenAIClient.GetChatClient(_deploymentName).AsIChatClient();

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

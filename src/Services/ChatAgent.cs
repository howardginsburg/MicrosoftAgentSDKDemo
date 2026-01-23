using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.CosmosDb;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Factory for creating Azure OpenAI-based agents
/// </summary>
public interface IAgentFactory
{
    Task<AIAgent> CreateAgentAsync(string userId);
}

public class ChatAgentFactory : IAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMCPServerManager _mcpServerManager;
    private readonly IStorage _storage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ChatAgentFactory> _logger;

    public ChatAgentFactory(
        IConfiguration configuration,
        IMCPServerManager mcpServerManager,
        IStorage storage,
        ILoggerFactory loggerFactory,
        ILogger<ChatAgentFactory> logger)
    {
        _configuration = configuration;
        _mcpServerManager = mcpServerManager;
        _storage = storage;
        _loggerFactory = loggerFactory;
        _logger = logger;
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

        // Get MCP tools from Microsoft Docs server
        var mcpTools = await _mcpServerManager.GetMicrosoftLearnToolsAsync();
        
        if (mcpTools.Any())
        {
            _logger.LogDebug("Agent will have access to {ToolCount} MCP tools: {ToolNames}", 
                mcpTools.Count, string.Join(", ", mcpTools.Select(t => t.Name)));
        }
        else
        {
            _logger.LogWarning("No MCP tools available - agent will run without external tool access");
        }

        // Create agent with MCP tools and chat message store for persistence
        var chatOptions = new ChatOptions
        {
            Instructions = systemInstructions,
            Tools = mcpTools.ToArray()
        };

        var agent = azureOpenAIClient
            .GetChatClient(deploymentName)
            .AsIChatClient()
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = $"Agent-{userId}",
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
            userId, deploymentName, mcpTools.Count);
        
        return agent;
    }
}

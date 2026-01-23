using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
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

    public async Task<AIAgent> CreateAgentAsync(string userId)
    {
        var openAIConfig = _configuration.GetSection("AzureOpenAI");
        var endpoint = openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI Endpoint not configured");
        var deploymentName = openAIConfig["DeploymentName"] ?? "gpt-4";
        var systemInstructions = openAIConfig["SystemInstructions"] ?? throw new InvalidOperationException("SystemInstructions not configured");

        var credential = new AzureCliCredential();
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);

        // Get MCP tools from Microsoft Docs server
        var mcpTools = await _mcpServerManager.GetMicrosoftLearnToolsAsync();
        
        if (mcpTools.Any())
        {
            _logger.LogInformation("Agent will have access to {ToolCount} MCP tools: {ToolNames}", 
                mcpTools.Count, string.Join(", ", mcpTools.Select(t => t.Name)));
        }
        else
        {
            _logger.LogWarning("No MCP tools available - agent will run without external tool access");
        }

        // Create agent with MCP tools
        var agent = azureOpenAIClient
            .GetChatClient(deploymentName)
            .AsIChatClient()
            .AsAIAgent(
                instructions: systemInstructions,
                name: $"Agent-{userId}",
                tools: mcpTools.ToArray());

        _logger.LogDebug("AIAgent created | UserId: {UserId} | Deployment: {DeploymentName} | ToolCount: {ToolCount}",
            userId, deploymentName, mcpTools.Count);
        
        return agent;
    }
}

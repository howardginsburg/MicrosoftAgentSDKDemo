using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Models;
using ModelContextProtocol.Client;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Manages MCP client connections and provides tools from MCP servers.
/// Uses the official Model Context Protocol C# SDK.
/// Supports multiple MCP servers configured via appsettings.json.
/// </summary>
public interface IMCPServerManager
{
    Task<IList<AITool>> GetToolsAsync();
    Task DisposeAsync();
}

public class MCPServerManager : IMCPServerManager
{
    private readonly ILogger<MCPServerManager> _logger;
    private readonly MCPServersConfiguration _configuration;
    private readonly List<IMcpClient> _mcpClients = new();

    public MCPServerManager(
        ILogger<MCPServerManager> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        
        // Load MCP servers configuration
        _configuration = new MCPServersConfiguration();
        configuration.GetSection("MCPServers").Bind(_configuration);
        
        _logger.LogDebug("Loaded configuration for {ServerCount} MCP server(s)", _configuration.Servers.Count);
    }

    /// <summary>
    /// Connects to all configured MCP servers and retrieves available tools.
    /// </summary>
    public async Task<IList<AITool>> GetToolsAsync()
    {
        var allTools = new List<AITool>();

        if (_configuration.Servers.Count == 0)
        {
            _logger.LogWarning("No MCP servers configured. Agent will run without MCP tools.");
            return allTools;
        }

        foreach (var serverConfig in _configuration.Servers)
        {
            if (!serverConfig.Enabled)
            {
                _logger.LogDebug("Skipping disabled MCP server: {ServerName}", serverConfig.Name);
                continue;
            }

            if (string.IsNullOrEmpty(serverConfig.Endpoint))
            {
                _logger.LogWarning("MCP server '{ServerName}' has no endpoint configured - skipping", serverConfig.Name);
                continue;
            }

            try
            {
                _logger.LogDebug("Connecting to MCP server '{ServerName}' at {Endpoint}", 
                    serverConfig.Name, serverConfig.Endpoint);

                // Create MCP client for this server
                var transport = new SseClientTransport(new SseClientTransportOptions
                {
                    Endpoint = new Uri(serverConfig.Endpoint),
                    Name = serverConfig.Name
                });

                var mcpClient = await McpClientFactory.CreateAsync(transport);
                _mcpClients.Add(mcpClient);

                _logger.LogInformation("✓ Connected to MCP server: {ServerName}", serverConfig.Name);

                // Retrieve the list of tools from this MCP server
                var mcpTools = await mcpClient.ListToolsAsync();
                
                _logger.LogDebug("Retrieved {ToolCount} tool(s) from '{ServerName}': {ToolNames}", 
                    mcpTools.Count(), 
                    serverConfig.Name,
                    string.Join(", ", mcpTools.Select(t => t.Name)));

                // Add tools from this server to the complete list
                allTools.AddRange(mcpTools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server '{ServerName}' at {Endpoint}", 
                    serverConfig.Name, serverConfig.Endpoint);
                // Continue to next server instead of failing completely
            }
        }

        if (allTools.Count > 0)
        {
            _logger.LogInformation("Successfully loaded {ToolCount} MCP tool(s) from {ServerCount} server(s)", 
                allTools.Count, _mcpClients.Count);
        }
        else
        {
            _logger.LogWarning("No MCP tools available - all configured servers failed or returned no tools");
        }

        return allTools;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _mcpClients)
        {
            await client.DisposeAsync();
        }
        _mcpClients.Clear();
        _logger.LogDebug("Disposed {ClientCount} MCP client(s)", _mcpClients.Count);
    }
}

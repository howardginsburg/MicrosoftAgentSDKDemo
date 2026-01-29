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
/// Supports both SSE (HTTP) and Stdio (local process) transports.
/// </summary>
public interface IMCPServerManager
{
    Task<IList<AITool>> GetToolsAsync();
    Task<IDictionary<string, IList<AITool>>> GetToolsByCategoryAsync();
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
        var toolsByCategory = await GetToolsByCategoryAsync();
        return toolsByCategory.Values.SelectMany(t => t).ToList();
    }

    /// <summary>
    /// Connects to all configured MCP servers and retrieves available tools grouped by server name.
    /// </summary>
    public async Task<IDictionary<string, IList<AITool>>> GetToolsByCategoryAsync()
    {
        var toolsByCategory = new Dictionary<string, IList<AITool>>();

        if (_configuration.Servers.Count == 0)
        {
            _logger.LogWarning("No MCP servers configured. Agent will run without MCP tools.");
            return toolsByCategory;
        }

        foreach (var serverConfig in _configuration.Servers)
        {
            if (!serverConfig.Enabled)
            {
                _logger.LogDebug("Skipping disabled MCP server: {ServerName}", serverConfig.Name);
                continue;
            }

            try
            {
                var tools = serverConfig.TransportType switch
                {
                    MCPTransportType.Sse => await ConnectSseServerAsync(serverConfig),
                    MCPTransportType.Stdio => await ConnectStdioServerAsync(serverConfig),
                    _ => throw new InvalidOperationException($"Unknown transport type: {serverConfig.TransportType}")
                };

                if (tools.Any())
                {
                    toolsByCategory[serverConfig.Name] = tools.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server '{ServerName}'", serverConfig.Name);
                // Continue to next server instead of failing completely
            }
        }

        var totalTools = toolsByCategory.Values.Sum(t => t.Count);
        if (totalTools > 0)
        {
            _logger.LogInformation("Successfully loaded {ToolCount} MCP tool(s) from {ServerCount} server(s)", 
                totalTools, _mcpClients.Count);
        }
        else
        {
            _logger.LogWarning("No MCP tools available - all configured servers failed or returned no tools");
        }

        return toolsByCategory;
    }

    /// <summary>
    /// Connects to an SSE-based MCP server over HTTP.
    /// </summary>
    private async Task<IEnumerable<AITool>> ConnectSseServerAsync(MCPServerConfig serverConfig)
    {
        if (string.IsNullOrEmpty(serverConfig.Endpoint))
        {
            _logger.LogWarning("MCP server '{ServerName}' has no endpoint configured - skipping", serverConfig.Name);
            return Enumerable.Empty<AITool>();
        }

        _logger.LogDebug("Connecting to SSE MCP server '{ServerName}' at {Endpoint}", 
            serverConfig.Name, serverConfig.Endpoint);

        var transport = new SseClientTransport(new SseClientTransportOptions
        {
            Endpoint = new Uri(serverConfig.Endpoint),
            Name = serverConfig.Name
        });

        var mcpClient = await McpClientFactory.CreateAsync(transport);
        _mcpClients.Add(mcpClient);

        _logger.LogInformation("✓ Connected to SSE MCP server: {ServerName}", serverConfig.Name);

        return await GetToolsFromClientAsync(mcpClient, serverConfig.Name);
    }

    /// <summary>
    /// Connects to a Stdio-based MCP server by launching a local process.
    /// </summary>
    private async Task<IEnumerable<AITool>> ConnectStdioServerAsync(MCPServerConfig serverConfig)
    {
        if (string.IsNullOrEmpty(serverConfig.Command))
        {
            _logger.LogWarning("MCP server '{ServerName}' has no command configured - skipping", serverConfig.Name);
            return Enumerable.Empty<AITool>();
        }

        _logger.LogDebug("Starting Stdio MCP server '{ServerName}' with command: {Command} {Arguments}", 
            serverConfig.Name, 
            serverConfig.Command,
            string.Join(" ", serverConfig.Arguments));

        var transportOptions = new StdioClientTransportOptions
        {
            Name = serverConfig.Name,
            Command = serverConfig.Command,
            Arguments = serverConfig.Arguments
        };

        // Add environment variables if configured
        if (serverConfig.EnvironmentVariables.Count > 0)
        {
            transportOptions.EnvironmentVariables = serverConfig.EnvironmentVariables;
            _logger.LogDebug("Setting {EnvCount} environment variable(s) for '{ServerName}'", 
                serverConfig.EnvironmentVariables.Count, serverConfig.Name);
        }

        // Set working directory if configured
        if (!string.IsNullOrEmpty(serverConfig.WorkingDirectory))
        {
            transportOptions.WorkingDirectory = serverConfig.WorkingDirectory;
            _logger.LogDebug("Using working directory '{WorkingDirectory}' for '{ServerName}'", 
                serverConfig.WorkingDirectory, serverConfig.Name);
        }

        var transport = new StdioClientTransport(transportOptions);
        var mcpClient = await McpClientFactory.CreateAsync(transport);
        _mcpClients.Add(mcpClient);

        _logger.LogInformation("✓ Connected to Stdio MCP server: {ServerName}", serverConfig.Name);

        return await GetToolsFromClientAsync(mcpClient, serverConfig.Name);
    }

    /// <summary>
    /// Retrieves tools from a connected MCP client.
    /// </summary>
    private async Task<IEnumerable<AITool>> GetToolsFromClientAsync(IMcpClient mcpClient, string serverName)
    {
        var mcpTools = await mcpClient.ListToolsAsync();
        
        _logger.LogDebug("Retrieved {ToolCount} tool(s) from '{ServerName}': {ToolNames}", 
            mcpTools.Count(), 
            serverName,
            string.Join(", ", mcpTools.Select(t => t.Name)));

        return mcpTools;
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

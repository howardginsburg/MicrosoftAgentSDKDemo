using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Manages MCP client connections and provides tools from MCP servers.
/// Uses the official Model Context Protocol C# SDK.
/// </summary>
public interface IMCPServerManager
{
    Task<IList<AITool>> GetMicrosoftLearnToolsAsync();
    Task DisposeAsync();
}

public class MCPServerManager : IMCPServerManager
{
    private readonly ILogger<MCPServerManager> _logger;
    private IMcpClient? _mcpClient;

    public MCPServerManager(ILogger<MCPServerManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connects to Microsoft Docs MCP server and retrieves available tools.
    /// </summary>
    public async Task<IList<AITool>> GetMicrosoftLearnToolsAsync()
    {
        try
        {
            // Create MCP client connecting to Microsoft Docs HTTP server
            var transport = new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = new Uri("https://learn.microsoft.com/api/mcp"),
                Name = "MicrosoftDocsServer"
            });

            _mcpClient = await McpClientFactory.CreateAsync(transport);

            _logger.LogDebug("Connected to Microsoft Docs MCP server at https://learn.microsoft.com/api/mcp");

            // Retrieve the list of tools from the MCP server
            var mcpTools = await _mcpClient.ListToolsAsync();
            
            _logger.LogDebug("Retrieved {ToolCount} tools from MCP server: {ToolNames}", 
                mcpTools.Count(), string.Join(", ", mcpTools.Select(t => t.Name)));

            // McpClientTool extends AIFunction which implements AITool
            return mcpTools.ToList<AITool>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Microsoft Docs MCP server");
            // Return empty list if connection fails
            return new List<AITool>();
        }
    }

    public async Task DisposeAsync()
    {
        if (_mcpClient != null)
        {
            await _mcpClient.DisposeAsync();
            _logger.LogDebug("Disposed MCP client");
        }
    }
}

namespace MicrosoftAgentSDKDemo.Models;

/// <summary>
/// Configuration for MCP (Model Context Protocol) servers.
/// </summary>
public class MCPServersConfiguration
{
    /// <summary>
    /// List of MCP servers to connect to.
    /// </summary>
    public List<MCPServerConfig> Servers { get; set; } = new();
}

/// <summary>
/// Configuration for a single MCP server.
/// </summary>
public class MCPServerConfig
{
    /// <summary>
    /// Friendly name for the MCP server (used in logging).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The endpoint URL for the MCP server.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Whether this server is enabled. Set to false to temporarily disable without removing config.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional timeout in seconds for server connection. Defaults to 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

namespace MicrosoftAgentSDKDemo.Models;

/// <summary>
/// Transport type for MCP server connections.
/// </summary>
public enum MCPTransportType
{
    /// <summary>
    /// Server-Sent Events (SSE) transport over HTTP.
    /// </summary>
    Sse,

    /// <summary>
    /// Standard input/output (stdio) transport for local process-based servers.
    /// </summary>
    Stdio
}

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
    /// Transport type: "Sse" for HTTP SSE servers, "Stdio" for local process servers.
    /// Defaults to Sse for backward compatibility.
    /// </summary>
    public MCPTransportType TransportType { get; set; } = MCPTransportType.Sse;

    /// <summary>
    /// Whether this server is enabled. Set to false to temporarily disable without removing config.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional timeout in seconds for server connection. Defaults to 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    // ========== SSE Transport Properties ==========

    /// <summary>
    /// The endpoint URL for SSE-based MCP servers.
    /// Required when TransportType is Sse.
    /// </summary>
    public string? Endpoint { get; set; }

    // ========== Stdio Transport Properties ==========

    /// <summary>
    /// The command to execute for stdio-based MCP servers (e.g., "npx", "node", "python").
    /// Required when TransportType is Stdio.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Arguments to pass to the command (e.g., ["-y", "@azure/mcp@latest", "server", "start"]).
    /// Used when TransportType is Stdio.
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    /// Environment variables to set for the process.
    /// Used when TransportType is Stdio.
    /// </summary>
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Working directory for the process. If not specified, uses current directory.
    /// Used when TransportType is Stdio.
    /// </summary>
    public string? WorkingDirectory { get; set; }
}

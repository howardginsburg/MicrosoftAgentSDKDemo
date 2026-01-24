using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Runtime.CompilerServices;

namespace MicrosoftAgentSDKDemo.Display;

/// <summary>
/// Delegating chat client that displays agent reasoning and tool invocations to the console
/// </summary>
public class ReasoningChatClient : DelegatingChatClient
{
    private readonly ILogger<ReasoningChatClient> _logger;

    public ReasoningChatClient(IChatClient innerClient, ILogger<ReasoningChatClient> logger)
        : base(innerClient)
    {
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Display reasoning start
        DisplayReasoningStart(chatMessages);

        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        // Display tool invocations and reasoning
        DisplayToolCalls(response);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DisplayReasoningStart(chatMessages);

        var toolCallsInProgress = new Dictionary<int, StreamingToolCallInfo>();

        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            // Track tool calls as they stream in
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall && !string.IsNullOrEmpty(functionCall.CallId))
                    {
                        TrackStreamingToolCall(toolCallsInProgress, functionCall);
                    }
                }
            }

            yield return update;
        }

        // Display completed tool calls
        DisplayStreamingToolCalls(toolCallsInProgress);
    }

    private void DisplayReasoningStart(IEnumerable<ChatMessage> messages)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMessage != null)
        {
            AnsiConsole.MarkupLine($"[dim]💭 Analyzing request: [/][cyan]{lastUserMessage.Text?.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private void DisplayToolCalls(ChatResponse response)
    {
        if (response.Messages == null || !response.Messages.Any())
            return;

        var toolCalls = new List<AIContent>();
        foreach (var message in response.Messages)
        {
            if (message.Contents != null)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        toolCalls.Add(content);
                    }
                }
            }
        }

        if (toolCalls.Any())
        {
            // Log before displaying to avoid console output during table rendering
            foreach (var content in toolCalls)
            {
                if (content is FunctionCallContent functionCall)
                {
                    var toolName = functionCall.Name ?? "Unknown";
                    var arguments = FormatArguments(functionCall.Arguments);
                    _logger.LogDebug("Tool invoked: {ToolName} | Arguments: {Arguments}", toolName, arguments);
                }
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Yellow);

            table.AddColumn(new TableColumn("[yellow]Tool Name[/]"));
            table.AddColumn(new TableColumn("[yellow]Arguments[/]"));

            foreach (var content in toolCalls)
            {
                if (content is FunctionCallContent functionCall)
                {
                    var toolName = functionCall.Name ?? "Unknown";
                    var arguments = FormatArguments(functionCall.Arguments);

                    table.AddRow(
                        $"[bold yellow]{toolName.EscapeMarkup()}[/]",
                        $"[dim]{arguments.EscapeMarkup()}[/]"
                    );
                }
            }

            AnsiConsole.MarkupLine("[yellow]🔧 Tool Invocations:[/]");
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        // Don't show "no tools needed" message - it's confusing when agent makes multiple calls
    }

    private void DisplayStreamingToolCalls(Dictionary<int, StreamingToolCallInfo> toolCalls)
    {
        if (!toolCalls.Any())
            return;

        // Log before displaying to avoid console output during table rendering
        foreach (var (index, toolInfo) in toolCalls.OrderBy(kvp => kvp.Key))
        {
            var arguments = FormatArguments(toolInfo.Arguments);
            _logger.LogDebug("Tool invoked: {ToolName} | Arguments: {Arguments}", toolInfo.Name, arguments);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow);

        table.AddColumn(new TableColumn("[yellow]Tool Name[/]"));
        table.AddColumn(new TableColumn("[yellow]Arguments[/]"));

        foreach (var (index, toolInfo) in toolCalls.OrderBy(kvp => kvp.Key))
        {
            var arguments = FormatArguments(toolInfo.Arguments);
            table.AddRow(
                $"[bold yellow]{toolInfo.Name.EscapeMarkup()}[/]",
                $"[dim]{arguments.EscapeMarkup()}[/]"
            );
        }

        AnsiConsole.MarkupLine("[yellow]🔧 Tool Invocations:[/]");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private void TrackStreamingToolCall(Dictionary<int, StreamingToolCallInfo> toolCalls, FunctionCallContent functionCall)
    {
        if (string.IsNullOrEmpty(functionCall.CallId))
            return;

        // Use the call ID hash as the integer key
        var callIdHash = functionCall.CallId.GetHashCode();
        
        if (!toolCalls.TryGetValue(callIdHash, out var info))
        {
            info = new StreamingToolCallInfo
            {
                CallId = callIdHash,
                Name = functionCall.Name ?? string.Empty,
                Arguments = new System.Text.StringBuilder()
            };
            toolCalls[callIdHash] = info;
        }

        if (!string.IsNullOrEmpty(functionCall.Name))
        {
            info.Name = functionCall.Name;
        }

        // Track arguments if they exist
        if (functionCall.Arguments != null)
        {
            var argsJson = System.Text.Json.JsonSerializer.Serialize(functionCall.Arguments);
            info.Arguments.Clear();
            info.Arguments.Append(argsJson);
        }
    }

    private string FormatArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || !arguments.Any())
            return "No arguments";

        var parts = arguments
            .Where(kvp => kvp.Value != null)
            .Select(kvp =>
            {
                var value = kvp.Value?.ToString() ?? "null";
                // Truncate long values
                if (value.Length > 100)
                    value = value.Substring(0, 97) + "...";
                return $"{kvp.Key}: {value}";
            });

        return string.Join(", ", parts);
    }

    private string FormatArguments(System.Text.StringBuilder? arguments)
    {
        if (arguments == null || arguments.Length == 0)
            return "No arguments";

        var argsText = arguments.ToString();
        if (argsText.Length > 100)
            argsText = argsText.Substring(0, 97) + "...";

        return argsText;
    }

    private class StreamingToolCallInfo
    {
        public int CallId { get; set; }
        public string Name { get; set; } = string.Empty;
        public System.Text.StringBuilder Arguments { get; set; } = new();
    }
}

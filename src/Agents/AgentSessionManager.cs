using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Display;
using MicrosoftAgentSDKDemo.Storage;
using MicrosoftAgentSDKDemo.Integration;
using Spectre.Console;

namespace MicrosoftAgentSDKDemo.Agents;

/// <summary>
/// Result of command processing.
/// </summary>
public enum CommandResult
{
    NotACommand,
    Handled,
    Quit
}

/// <summary>
/// Manages agent sessions including thread creation, loading, and chat interactions.
/// </summary>
public class AgentSessionManager
{
    private readonly ILogger<AgentSessionManager> _logger;
    private readonly IConsoleUI _consoleUI;
    private readonly CosmosDbAgentThreadStore _threadStore;
    private readonly IAgentFactory _agentFactory;
    private readonly IFileAttachmentService _fileAttachmentService;
    private readonly IChatExportService _chatExportService;
    private readonly IMCPServerManager _mcpServerManager;
    private readonly Microsoft.Agents.Storage.IStorage _storage;
    private readonly ILoggerFactory _loggerFactory;

    public AgentSessionManager(
        ILogger<AgentSessionManager> logger,
        IConsoleUI consoleUI,
        CosmosDbAgentThreadStore threadStore,
        IAgentFactory agentFactory,
        IFileAttachmentService fileAttachmentService,
        IChatExportService chatExportService,
        IMCPServerManager mcpServerManager,
        Microsoft.Agents.Storage.IStorage storage,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _consoleUI = consoleUI;
        _threadStore = threadStore;
        _agentFactory = agentFactory;
        _fileAttachmentService = fileAttachmentService;
        _chatExportService = chatExportService;
        _mcpServerManager = mcpServerManager;
        _storage = storage;
        _loggerFactory = loggerFactory;
    }

    public async Task RunUserSessionAsync(string username)
    {
        var sessionStart = DateTime.UtcNow;
        _logger.LogInformation("Session started | UserId: {UserId}", username);

        // Display agent greeting
        var greeting = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Preparing...", async ctx => 
            {
                return await _agentFactory.GetGreetingAsync(username);
            });
        _consoleUI.DisplayGreeting(greeting);

        bool shouldLogout = false;

        // User session loop
        while (!shouldLogout)
        {
            try
            {
                // Get user's threads with metadata
                var threads = await _threadStore.GetUserThreadsAsync(username, limit: 10);
                var selection = await _consoleUI.GetThreadSelectionAsync(threads, username);

                if (selection.Type == ThreadSelectionType.Exit)
                {
                    _consoleUI.DisplayGoodbye();
                    var sessionDuration = DateTime.UtcNow - sessionStart;
                    _logger.LogInformation("Session ended | UserId: {UserId} | Duration: {DurationMs}ms", username, sessionDuration.TotalMilliseconds);
                    shouldLogout = true;
                    continue;
                }

                AgentThread? thread = null;
                string? threadId = null;

                if (selection.Type == ThreadSelectionType.New)
                {
                    // Get first message, handling commands until we get actual content
                    string? firstMessage = selection.FirstMessage;
                    string? filePaths = selection.FilePaths;
                    
                    while (true)
                    {
                        if (string.IsNullOrWhiteSpace(firstMessage))
                        {
                            // Empty first message - go back to thread selection
                            break;
                        }
                        
                        // Check if first message is a command
                        var commandResult = await HandleCommandIfApplicableAsync(firstMessage, username, null);
                        if (commandResult == CommandResult.Quit)
                        {
                            firstMessage = null; // Signal to exit this flow
                            break;
                        }
                        if (commandResult == CommandResult.Handled)
                        {
                            // Command was handled, ask for another first message
                            var (newMessage, newFilePaths) = await _consoleUI.GetFirstMessageWithAttachmentsAsync();
                            firstMessage = newMessage;
                            filePaths = newFilePaths;
                            continue;
                        }
                        
                        // Not a command - proceed with conversation
                        break;
                    }
                    
                    if (string.IsNullOrWhiteSpace(firstMessage))
                    {
                        continue; // Go back to thread selection
                    }
                    
                    // Update selection with potentially new first message
                    selection = selection with { FirstMessage = firstMessage, FilePaths = filePaths };

                    // Create agent with tool routing based on the first message
                    var baseAgent = await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(Style.Parse("cyan"))
                        .StartAsync("🔧 Selecting relevant tools...", async ctx => 
                        {
                            return await _agentFactory.CreateAgentAsync(username, selection.FirstMessage);
                        });
                    
                    var agent = new AIHostAgent(baseAgent, _threadStore);
                    (thread, threadId) = await HandleNewThreadAsync(agent, username, selection);
                    
                    // Chat loop for selected thread
                    if (thread != null && threadId != null)
                    {
                        await RunChatLoopAsync(agent, thread, threadId, username);
                    }
                }
                else if (selection.Type == ThreadSelectionType.Existing && selection.ThreadId != null)
                {
                    // For existing threads, load thread first then route tools per-message in chat loop
                    var baseAgent = await _agentFactory.CreateAgentAsync(username);
                    var agent = new AIHostAgent(baseAgent, _threadStore);
                    
                    (thread, threadId) = await HandleExistingThreadAsync(agent, username, selection.ThreadId);
                    
                    // Chat loop for selected thread (will re-route tools per message)
                    if (thread != null && threadId != null)
                    {
                        await RunChatLoopAsync(agent, thread, threadId, username);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in session loop | UserId: {UserId}", username);
                _consoleUI.DisplayError("An unexpected error occurred. Please try again.");
            }
        }
    }

    private async Task<(AgentThread? thread, string? threadId)> HandleNewThreadAsync(
        AIHostAgent agent, 
        string username, 
        ThreadSelection selection)
    {
        // Create new thread
        var thread = await agent.GetNewThreadAsync();
        var threadId = Guid.NewGuid().ToString();
        
        // Set current user and add to user index with the first message as title
        _threadStore.SetCurrentUserId(username);
        await _threadStore.AddThreadToUserIndexAsync(username, threadId, selection.FirstMessage!);
        
        _consoleUI.DisplayThreadCreated(threadId);

        // Process file attachments and send first message
        ChatMessage firstMessage = await CreateFirstMessageAsync(selection);

        // Send first message with status display
        var response = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("🤔 Agent is thinking...", async ctx => 
            {
                return await agent.RunAsync(firstMessage, thread);
            });
        
        // Save thread AFTER first message so chat history key is available
        await _threadStore.SaveThreadAsync(agent, threadId, thread);
        
        _consoleUI.DisplayAgentResponse(response.Text);
        
        return (thread, threadId);
    }

    private async Task<ChatMessage> CreateFirstMessageAsync(ThreadSelection selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.FilePaths))
        {
            var attachmentContents = await _fileAttachmentService.ProcessFileAttachmentsAsync(selection.FilePaths);
            _consoleUI.DisplayAttachmentsProcessed(attachmentContents.Count);
            
            if (attachmentContents.Any())
            {
                // Create multimodal ChatMessage with images and text
                var message = MultimodalMessageExtensions.CreateMultimodalMessage(selection.FirstMessage!, attachmentContents);
                
                if (attachmentContents.HasImageAttachments())
                {
                    _logger.LogInformation("First message includes image attachments - using vision model");
                }
                
                return message;
            }
        }
        
        return new ChatMessage(ChatRole.User, selection.FirstMessage!);
    }

    private async Task<(AgentThread? thread, string? threadId)> HandleExistingThreadAsync(
        AIHostAgent agent,
        string username,
        string selectedThreadId)
    {
        var threadId = selectedThreadId;
        AgentThread? thread = null;
        
        try
        {
            _threadStore.SetCurrentUserId(username);
            thread = await _threadStore.GetThreadAsync(agent, threadId);
            
            // Get the chat history key that was stored in the thread document
            var chatHistoryKey = _threadStore.LastChatHistoryKey;
            _logger.LogInformation("Chat history key from thread: {Key}", chatHistoryKey);
            
            if (!string.IsNullOrEmpty(chatHistoryKey))
            {
                var messageStore = new CosmosDbChatMessageStore(_storage, username, 
                    System.Text.Json.JsonSerializer.SerializeToElement(chatHistoryKey), 
                    _loggerFactory.CreateLogger<CosmosDbChatMessageStore>());
                var messages = await messageStore.GetMessagesAsync(chatHistoryKey);
                
                _logger.LogInformation("Retrieved {Count} messages", messages.Count());
                
                if (messages.Any())
                {
                    _consoleUI.DisplayConversationHistory(messages, username);
                }
                else
                {
                    _logger.LogWarning("No messages found in chat history");
                    _consoleUI.DisplayThreadLoaded(threadId);
                }
            }
            else
            {
                _logger.LogInformation("No chat history key - new thread or not yet used");
                _consoleUI.DisplayThreadLoaded(threadId);
            }
        }
        catch (InvalidOperationException)
        {
            _consoleUI.DisplayError($"Thread {threadId} not found");
            return (null, null);
        }
        
        return (thread, threadId);
    }

    private async Task RunChatLoopAsync(AIHostAgent agent, AgentThread thread, string threadId, string username)
    {
        while (true)
        {
            var input = await _consoleUI.GetChatInputAsync(username);

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Handle commands
            var commandResult = await HandleCommandIfApplicableAsync(input, username, threadId);
            if (commandResult == CommandResult.Handled)
                continue;
            if (commandResult == CommandResult.Quit)
                break;

            try
            {
                _logger.LogDebug("Processing message | UserId: {UserId} | ThreadId: {ThreadId}", username, threadId);
                
                // Re-route tools based on current message to support cross-MCP queries
                var baseAgent = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync("🔧 Selecting relevant tools...", async ctx => 
                    {
                        return await _agentFactory.CreateAgentAsync(username, input);
                    });
                
                // Create new host agent with updated tools but same thread store
                var routedAgent = new AIHostAgent(baseAgent, _threadStore);
                
                // Create simple text message (file attachments only available when starting new conversation)
                var chatMessage = new ChatMessage(ChatRole.User, input);
                
                // Send message through agent with status display
                var response = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync("🤔 Agent is thinking...", async ctx => 
                    {
                        return await routedAgent.RunAsync(chatMessage, thread);
                    });
                
                // Explicitly save thread after each interaction to persist conversation history
                await _threadStore.SaveThreadAsync(routedAgent, threadId, thread);
                
                _consoleUI.DisplayAgentResponse(response.Text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                _consoleUI.DisplayError(ex.Message);
            }
        }
    }

    private async Task HandleExportAsync(string username, string threadId)
    {
        try
        {
            var chatHistoryKey = _threadStore.LastChatHistoryKey;
            
            if (string.IsNullOrEmpty(chatHistoryKey))
            {
                _consoleUI.DisplayError("No messages to export yet.");
                return;
            }

            var messageStore = new CosmosDbChatMessageStore(
                _storage, 
                username,
                System.Text.Json.JsonSerializer.SerializeToElement(chatHistoryKey),
                _loggerFactory.CreateLogger<CosmosDbChatMessageStore>());
            
            var messages = await messageStore.GetMessagesAsync(chatHistoryKey);
            
            if (!messages.Any())
            {
                _consoleUI.DisplayError("No messages to export.");
                return;
            }

            var filePath = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("📄 Exporting conversation to PDF...", async ctx =>
                {
                    return await _chatExportService.ExportToPdfAsync(messages, username, threadId);
                });

            _consoleUI.DisplayExportCompleted(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export conversation");
            _consoleUI.DisplayError($"Export failed: {ex.Message}");
        }
    }

    private async Task HandleMcpCommandAsync()
    {
        try
        {
            var toolsByServer = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("🔌 Loading MCP servers...", async ctx =>
                {
                    return await _mcpServerManager.GetToolsByCategoryAsync();
                });

            _consoleUI.DisplayMcpServers(toolsByServer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get MCP server info");
            _consoleUI.DisplayError($"Failed to load MCP info: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles slash commands. Returns the result of command processing.
    /// </summary>
    private async Task<CommandResult> HandleCommandIfApplicableAsync(string input, string username, string? threadId)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return CommandResult.NotACommand;

        if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            _consoleUI.DisplayAvailableCommands();
            return CommandResult.Handled;
        }

        if (input.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await HandleMcpCommandAsync();
            return CommandResult.Handled;
        }

        if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Quit;
        }

        if (input.Equals("/export", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(threadId))
            {
                _consoleUI.DisplayError("Cannot export - no active conversation.");
            }
            else
            {
                await HandleExportAsync(username, threadId);
            }
            return CommandResult.Handled;
        }

        // Unknown command - treat as not a command (will be sent to agent)
        return CommandResult.NotACommand;
    }
}

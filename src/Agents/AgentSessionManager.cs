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
/// Manages agent sessions including thread creation, loading, and chat interactions.
/// </summary>
public class AgentSessionManager
{
    private readonly ILogger<AgentSessionManager> _logger;
    private readonly IConsoleUI _consoleUI;
    private readonly CosmosDbAgentThreadStore _threadStore;
    private readonly IAgentFactory _agentFactory;
    private readonly IFileAttachmentService _fileAttachmentService;
    private readonly MultimodalMessageHelper _multimodalHelper;
    private readonly Microsoft.Agents.Storage.IStorage _storage;
    private readonly ILoggerFactory _loggerFactory;

    public AgentSessionManager(
        ILogger<AgentSessionManager> logger,
        IConsoleUI consoleUI,
        CosmosDbAgentThreadStore threadStore,
        IAgentFactory agentFactory,
        IFileAttachmentService fileAttachmentService,
        MultimodalMessageHelper multimodalHelper,
        Microsoft.Agents.Storage.IStorage storage,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _consoleUI = consoleUI;
        _threadStore = threadStore;
        _agentFactory = agentFactory;
        _fileAttachmentService = fileAttachmentService;
        _multimodalHelper = multimodalHelper;
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

                if (selection.Type == ThreadSelectionType.New && !string.IsNullOrWhiteSpace(selection.FirstMessage))
                {
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
                var message = _multimodalHelper.CreateMultimodalMessage(selection.FirstMessage!, attachmentContents);
                
                if (_multimodalHelper.HasImageAttachments(attachmentContents))
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

            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                // Return to thread selection menu
                break;
            }

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
}

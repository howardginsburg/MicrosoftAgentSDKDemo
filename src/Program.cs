using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using MicrosoftAgentSDKDemo.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
    })
    .ConfigureServices((context, services) =>
    {
        // Register Agent Framework and persistence services
        services.AddSingleton<IThreadManager, ThreadManager>();
        services.AddSingleton<IMCPServerManager, MCPServerManager>();
        services.AddSingleton<IAgentFactory, ChatAgentFactory>();
        services.AddSingleton<ThreadTools>();
        services.AddSingleton<IConsoleUI, ConsoleUI>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var consoleUI = host.Services.GetRequiredService<IConsoleUI>();
var threadManager = host.Services.GetRequiredService<IThreadManager>();
var agentFactory = host.Services.GetRequiredService<IAgentFactory>();

logger.LogInformation("Application started | Framework: Microsoft Agent Framework");

// Get username
var username = await consoleUI.GetUsernameAsync();
var sessionStart = DateTime.UtcNow;
logger.LogInformation("Session started | UserId: {UserId}", username);

bool shouldExit = false;

while (!shouldExit)
{
    string? currentThreadId = null;
    string? currentThreadName = null;

    try
    {
        // Get user threads and display selection menu
        var userThreads = await threadManager.GetUserThreadsAsync(username, limit: 10);
        var selection = await consoleUI.GetThreadSelectionAsync(userThreads, username);

        if (selection.Type == ThreadSelectionType.Exit)
        {
            consoleUI.DisplayGoodbye();
            var sessionDuration = DateTime.UtcNow - sessionStart;
            logger.LogInformation("Session ended | UserId: {UserId} | Duration: {DurationMs}ms", username, sessionDuration.TotalMilliseconds);
            shouldExit = true;
            continue;
        }

        if (selection.Type == ThreadSelectionType.New && !string.IsNullOrWhiteSpace(selection.FirstMessage))
        {
            // Create new thread
            currentThreadId = await threadManager.CreateThreadAsync(username, selection.FirstMessage);
            var thread = await threadManager.GetThreadAsync(username, currentThreadId);
            currentThreadName = thread?.ThreadName ?? "New Thread";
            
            consoleUI.DisplayThreadCreated(currentThreadName, currentThreadId);

            // Create agent for this user
            var agent = await agentFactory.CreateAgentAsync(username);
            
            // Send first message through the agent
            await threadManager.SaveMessageAsync(username, currentThreadId, "user", selection.FirstMessage);
            var agentResponse = await agent.RunAsync(selection.FirstMessage);
            await threadManager.SaveMessageAsync(username, currentThreadId, "assistant", agentResponse.Text);
            
            consoleUI.DisplayAgentResponse(agentResponse.Text);
        }
        else if (selection.Type == ThreadSelectionType.Existing && selection.Thread != null)
        {
            // Load existing thread
            currentThreadId = selection.Thread.Id;
            currentThreadName = selection.Thread.ThreadName;
            
            logger.LogInformation("Thread switched | UserId: {UserId} | ThreadId: {ThreadId} | ThreadName: {ThreadName}", 
                username, currentThreadId, currentThreadName);
            consoleUI.DisplayThreadLoaded(currentThreadName);
            consoleUI.DisplayConversationHistory(username, selection.Thread.Messages);
        }

        // Chat loop for selected thread
        if (currentThreadId != null)
        {
            var agent = await agentFactory.CreateAgentAsync(username);
            
            while (true)
            {
                var input = await consoleUI.GetChatInputAsync(username, currentThreadName ?? "");

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    // Return to thread selection menu
                    break;
                }

                try
                {
                    logger.LogDebug("Processing message | UserId: {UserId} | ThreadId: {ThreadId}", username, currentThreadId);
                    
                    // Save user message to thread
                    await threadManager.SaveMessageAsync(username, currentThreadId, "user", input);
                    
                    // Send message through agent - framework will handle tool calls automatically
                    var agentResponse = await agent.RunAsync(input);
                    
                    // Save assistant response to thread
                    await threadManager.SaveMessageAsync(username, currentThreadId, "assistant", agentResponse.Text);
                    
                    consoleUI.DisplayAgentResponse(agentResponse.Text);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing message");
                    consoleUI.DisplayError(ex.Message);
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in main loop");
        consoleUI.DisplayError(ex.Message);
    }
}

await host.RunAsync();


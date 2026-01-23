using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.CosmosDb;
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
        // Configure Cosmos DB Storage using framework's IStorage
        var cosmosConfig = context.Configuration.GetSection("CosmosDB");
        var endpoint = cosmosConfig["Endpoint"] ?? throw new InvalidOperationException("CosmosDB Endpoint not configured");
        var accountKey = cosmosConfig["AccountKey"] ?? throw new InvalidOperationException("CosmosDB AccountKey not configured");
        var databaseName = cosmosConfig["DatabaseName"] ?? "agent-database";
        var containerId = cosmosConfig["ContainerId"] ?? "conversations";

        services.AddSingleton<IStorage>(sp =>
            new CosmosDbPartitionedStorage(
                new CosmosDbPartitionedStorageOptions
                {
                    CosmosDbEndpoint = endpoint,
                    AuthKey = accountKey,
                    DatabaseId = databaseName,
                    ContainerId = containerId
                }));

        // Register Agent Framework services
        services.AddSingleton<CosmosDbAgentThreadStore>();
        services.AddSingleton<IMCPServerManager, MCPServerManager>();
        services.AddSingleton<IAgentFactory, ChatAgentFactory>();
        services.AddSingleton<IConsoleUI, ConsoleUI>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var consoleUI = host.Services.GetRequiredService<IConsoleUI>();
var threadStore = host.Services.GetRequiredService<CosmosDbAgentThreadStore>();
var agentFactory = host.Services.GetRequiredService<IAgentFactory>();

logger.LogInformation("Application started | Framework: Microsoft Agent Framework");

// Get username
var username = await consoleUI.GetUsernameAsync();
var sessionStart = DateTime.UtcNow;
logger.LogInformation("Session started | UserId: {UserId}", username);

bool shouldExit = false;

while (!shouldExit)
{
    try
    {
        // Get user's threads with metadata
        var threads = await threadStore.GetUserThreadsAsync(username, limit: 10);
        var selection = await consoleUI.GetThreadSelectionAsync(threads, username);

        if (selection.Type == ThreadSelectionType.Exit)
        {
            consoleUI.DisplayGoodbye();
            var sessionDuration = DateTime.UtcNow - sessionStart;
            logger.LogInformation("Session ended | UserId: {UserId} | Duration: {DurationMs}ms", username, sessionDuration.TotalMilliseconds);
            shouldExit = true;
            continue;
        }

        // Create base agent
        var baseAgent = await agentFactory.CreateAgentAsync(username);
        
        // Wrap with AIHostAgent for automatic thread persistence
        var agent = new AIHostAgent(baseAgent, threadStore);

        AgentThread? thread = null;
        string? threadId = null;

        if (selection.Type == ThreadSelectionType.New && !string.IsNullOrWhiteSpace(selection.FirstMessage))
        {
            // Create new thread
            thread = await agent.GetNewThreadAsync();
            threadId = Guid.NewGuid().ToString();
            
            // Save thread and add to user index with the first message as title
            await threadStore.SaveThreadAsync(agent, threadId, thread);
            await threadStore.AddThreadToUserIndexAsync(username, threadId, selection.FirstMessage);
            
            consoleUI.DisplayThreadCreated(threadId);

            // Send first message
            var response = await agent.RunAsync(selection.FirstMessage, thread);
            consoleUI.DisplayAgentResponse(response.Text);
        }
        else if (selection.Type == ThreadSelectionType.Existing && selection.ThreadId != null)
        {
            // Load existing thread
            threadId = selection.ThreadId;
            
            try
            {
                thread = await threadStore.GetThreadAsync(agent, threadId);
                consoleUI.DisplayThreadLoaded(threadId);
            }
            catch (InvalidOperationException)
            {
                consoleUI.DisplayError($"Thread {threadId} not found");
                continue;
            }
        }

        // Chat loop for selected thread
        if (thread != null && threadId != null)
        {
            while (true)
            {
                var input = await consoleUI.GetChatInputAsync(username);

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    // Return to thread selection menu
                    break;
                }

                try
                {
                    logger.LogDebug("Processing message | UserId: {UserId} | ThreadId: {ThreadId}", username, threadId);
                    
                    // Send message through agent - framework handles conversation history
                    var response = await agent.RunAsync(input, thread);
                    
                    // Explicitly save thread after each interaction to persist conversation history
                    await threadStore.SaveThreadAsync(agent, threadId, thread);
                    
                    consoleUI.DisplayAgentResponse(response.Text);
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


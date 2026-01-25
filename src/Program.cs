using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.CosmosDb;
using Microsoft.Extensions.AI;
using MicrosoftAgentSDKDemo.Agents;
using MicrosoftAgentSDKDemo.Display;
using MicrosoftAgentSDKDemo.Storage;
using MicrosoftAgentSDKDemo.Integration;
using Spectre.Console;

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
        // Configure Cosmos DB Storage using framework's IStorage with Azure CLI credentials
        var cosmosConfig = context.Configuration.GetSection("CosmosDB");
        var endpoint = cosmosConfig["Endpoint"] ?? throw new InvalidOperationException("CosmosDB Endpoint not configured");
        var databaseName = cosmosConfig["DatabaseName"] ?? "agent-database";
        var containerId = cosmosConfig["ContainerId"] ?? "conversations";

        services.AddSingleton<IStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Cosmos DB: Using Azure CLI credential (RBAC)");
            
            return new CosmosDbPartitionedStorage(
                new CosmosDbPartitionedStorageOptions
                {
                    CosmosDbEndpoint = endpoint,
                    DatabaseId = databaseName,
                    ContainerId = containerId,
                    TokenCredential = new AzureCliCredential()
                });
        });

        // Register Agent Framework services
        services.AddSingleton<CosmosDbAgentThreadStore>();
        services.AddSingleton<IMCPServerManager, MCPServerManager>();
        services.AddSingleton<IImageGenerationService, AzureOpenAIImageService>();
        services.AddSingleton<IFileAttachmentService, FileAttachmentService>();
        services.AddSingleton<MultimodalMessageHelper>();
        services.AddSingleton<IAgentFactory, ChatAgentFactory>();
        services.AddSingleton<IConsoleUI, ConsoleUI>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var consoleUI = host.Services.GetRequiredService<IConsoleUI>();
var threadStore = host.Services.GetRequiredService<CosmosDbAgentThreadStore>();
var agentFactory = host.Services.GetRequiredService<IAgentFactory>();
var fileAttachmentService = host.Services.GetRequiredService<IFileAttachmentService>();
var multimodalHelper = host.Services.GetRequiredService<MultimodalMessageHelper>();

logger.LogInformation("Application started | Framework: Microsoft Agent Framework");

bool shouldExitApp = false;

// Main application loop - allows multiple users to log in sequentially
while (!shouldExitApp)
{
    // Display application logo
    consoleUI.DisplayLogo();
    
    // Verify Azure CLI login before proceeding
    try
    {
        var credential = new AzureCliCredential();
        
        // Get the authenticated account username
        var tokenRequestContext = new Azure.Core.TokenRequestContext(new[] { "https://management.azure.com/.default" });
        var token = await credential.GetTokenAsync(tokenRequestContext, default);
        
        // Decode the JWT token to get the user information
        string? userName = null;
        try
        {
            var parts = token.Token.Split('.');
            if (parts.Length >= 2)
            {
                var payload = parts[1];
                
                // Fix base64 padding
                var remainder = payload.Length % 4;
                if (remainder > 0)
                {
                    payload += new string('=', 4 - remainder);
                }
                
                var jsonBytes = Convert.FromBase64String(payload);
                var tokenJson = System.Text.Json.JsonDocument.Parse(jsonBytes);
                
                if (tokenJson.RootElement.TryGetProperty("upn", out var upn))
                {
                    userName = upn.GetString();
                }
                else if (tokenJson.RootElement.TryGetProperty("unique_name", out var uniqueName))
                {
                    userName = uniqueName.GetString();
                }
                else if (tokenJson.RootElement.TryGetProperty("email", out var email))
                {
                    userName = email.GetString();
                }
                
                logger.LogDebug("Token claims parsed. User: {User}", userName ?? "not found in token");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to parse JWT token for user information");
        }
        
        // Use Azure Resource Manager to validate authentication and get tenant info
        var armClient = new Azure.ResourceManager.ArmClient(credential);
        var subscriptions = armClient.GetSubscriptions();
        
        // Attempt to get first subscription to validate authentication
        Azure.ResourceManager.Resources.SubscriptionResource? subscription = null;
        await foreach (var sub in subscriptions)
        {
            subscription = sub;
            break;
        }
        
        if (subscription != null)
        {
            var tenants = armClient.GetTenants();
            Azure.ResourceManager.Resources.TenantResource? tenant = null;
            await foreach (var t in tenants)
            {
                tenant = t;
                break;
            }
            
            if (!string.IsNullOrEmpty(userName))
            {
                AnsiConsole.MarkupLine($"[dim]✓ Authenticated as [cyan]{userName.EscapeMarkup()}[/][/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]✓ Azure CLI authenticated[/]");
            }
            AnsiConsole.MarkupLine($"[dim]  Subscription: [cyan]{subscription.Data.DisplayName}[/][/]");
            if (tenant != null)
            {
                AnsiConsole.MarkupLine($"[dim]  Tenant: [cyan]{tenant.Data.TenantId}[/][/]");
            }
            AnsiConsole.WriteLine();
            logger.LogDebug("Azure CLI authenticated | User: {User} | Subscription: {Subscription}", userName ?? "unknown", subscription.Data.DisplayName);
        }
        else
        {
            if (!string.IsNullOrEmpty(userName))
            {
                AnsiConsole.MarkupLine($"[dim]✓ Authenticated as [cyan]{userName.EscapeMarkup()}[/][/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]✓ Azure CLI authenticated[/]");
            }
            AnsiConsole.WriteLine();
            logger.LogDebug("Azure CLI authenticated | User: {User}", userName ?? "unknown");
        }
    }
    catch (Azure.Identity.CredentialUnavailableException)
    {
        AnsiConsole.MarkupLine("[red]✗ Azure CLI authentication failed[/]");
        AnsiConsole.MarkupLine("[yellow]Please run 'az login' to authenticate with Azure before starting the application.[/]");
        AnsiConsole.WriteLine();
        logger.LogError("Azure CLI not authenticated. Application requires 'az login'.");
        return;
    }
    catch (Azure.RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
    {
        AnsiConsole.MarkupLine("[red]✗ Azure CLI authentication failed[/]");
        AnsiConsole.MarkupLine("[yellow]Please run 'az login' to authenticate with Azure before starting the application.[/]");
        AnsiConsole.WriteLine();
        logger.LogError("Azure CLI not authenticated. Application requires 'az login'.");
        return;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not verify Azure CLI authentication status");
    }

    // Get username
    var username = await consoleUI.GetUsernameAsync();
    var sessionStart = DateTime.UtcNow;
    logger.LogInformation("Session started | UserId: {UserId}", username);

    // Display agent greeting
    var greeting = await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("cyan"))
        .StartAsync("Preparing...", async ctx => 
        {
            return await agentFactory.GetGreetingAsync(username);
        });
    consoleUI.DisplayGreeting(greeting);

    bool shouldLogout = false;

    // User session loop
    while (!shouldLogout)
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
            shouldLogout = true;
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
            
            // Set current user and add to user index with the first message as title
            threadStore.SetCurrentUserId(username);
            await threadStore.AddThreadToUserIndexAsync(username, threadId, selection.FirstMessage);
            
            consoleUI.DisplayThreadCreated(threadId);

            // Process file attachments and send first message
            ChatMessage firstMessage;
            
            if (!string.IsNullOrWhiteSpace(selection.FilePaths))
            {
                var attachmentContents = await fileAttachmentService.ProcessFileAttachmentsAsync(selection.FilePaths);
                consoleUI.DisplayAttachmentsProcessed(attachmentContents.Count);
                
                if (attachmentContents.Any())
                {
                    // Create multimodal ChatMessage with images and text
                    firstMessage = multimodalHelper.CreateMultimodalMessage(selection.FirstMessage, attachmentContents);
                    
                    if (multimodalHelper.HasImageAttachments(attachmentContents))
                    {
                        logger.LogInformation("First message includes image attachments - using vision model");
                    }
                }
                else
                {
                    firstMessage = new ChatMessage(ChatRole.User, selection.FirstMessage);
                }
            }
            else
            {
                firstMessage = new ChatMessage(ChatRole.User, selection.FirstMessage);
            }

            // Send first message with status display
            var response = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("🤔 Agent is thinking...", async ctx => 
                {
                    return await agent.RunAsync(firstMessage, thread);
                });
            
            // Save thread AFTER first message so chat history key is available
            await threadStore.SaveThreadAsync(agent, threadId, thread);
            
            consoleUI.DisplayAgentResponse(response.Text);
        }
        else if (selection.Type == ThreadSelectionType.Existing && selection.ThreadId != null)
        {
            // Load existing thread
            threadId = selection.ThreadId;
            
            try
            {
                threadStore.SetCurrentUserId(username);
                thread = await threadStore.GetThreadAsync(agent, threadId);
                
                // Get the chat history key that was stored in the thread document
                var chatHistoryKey = threadStore.LastChatHistoryKey;
                logger.LogInformation("Chat history key from thread: {Key}", chatHistoryKey);
                
                if (!string.IsNullOrEmpty(chatHistoryKey))
                {
                    var storage = host.Services.GetRequiredService<IStorage>();
                    var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
                    var messageStore = new CosmosDbChatMessageStore(storage, username, 
                        System.Text.Json.JsonSerializer.SerializeToElement(chatHistoryKey), 
                        loggerFactory.CreateLogger<CosmosDbChatMessageStore>());
                    var messages = await messageStore.GetMessagesAsync(chatHistoryKey);
                    
                    logger.LogInformation("Retrieved {Count} messages", messages.Count());
                    
                    if (messages.Any())
                    {
                        consoleUI.DisplayConversationHistory(messages, username);
                    }
                    else
                    {
                        logger.LogWarning("No messages found in chat history");
                        consoleUI.DisplayThreadLoaded(threadId);
                    }
                }
                else
                {
                    logger.LogInformation("No chat history key - new thread or not yet used");
                    consoleUI.DisplayThreadLoaded(threadId);
                }
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
                    
                    // Create simple text message (file attachments only available when starting new conversation)
                    var chatMessage = new ChatMessage(ChatRole.User, input);
                    
                    // Send message through agent with status display
                    var response = await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(Style.Parse("cyan"))
                        .StartAsync("🤔 Agent is thinking...", async ctx => 
                        {
                            return await agent.RunAsync(chatMessage, thread);
                        });
                    
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
        logger.LogError(ex, "Unexpected error in session loop | UserId: {UserId}", username);
        consoleUI.DisplayError("An unexpected error occurred. Please try again.");
    }
    }
}

logger.LogInformation("Application exiting");

await host.RunAsync();


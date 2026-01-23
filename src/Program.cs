using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        // Configure Application Insights
        var instrumentationKey = context.Configuration["ApplicationInsights:InstrumentationKey"];
        
        services.AddLogging(builder =>
        {
            builder.AddApplicationInsights(instrumentationKey);
        });

        // Register framework-aware services
        services.AddSingleton<IThreadManager, ThreadManager>();
        services.AddSingleton<IChatAgent, ChatAgent>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var chatAgent = host.Services.GetRequiredService<IChatAgent>();
var threadManager = host.Services.GetRequiredService<IThreadManager>();

logger.LogInformation("Application started");

// Prompt for username
Console.Write("Enter your username: ");
var username = Console.ReadLine() ?? "User";

var sessionStart = DateTime.UtcNow;
logger.LogInformation("Session started | UserId: {UserId}", username);

bool shouldExit = false;

while (!shouldExit)
{
    string? currentThreadId = null;
    string? currentThreadName = null;

    // Get user threads and display selection
    try
    {
        var userThreads = await threadManager.GetUserThreadsAsync(username, limit: 10);
        
        Console.WriteLine("\nSelect a thread:");
        Console.WriteLine("  1. [NEW] - Start a new conversation");
        
        for (int i = 0; i < userThreads.Count; i++)
        {
            Console.WriteLine($"  {i + 2}. {userThreads[i].ThreadName}");
        }
        
        Console.WriteLine($"  {userThreads.Count + 2}. [QUIT] - Exit the application");
        
        Console.Write("\nEnter thread number: ");
        var selection = Console.ReadLine() ?? string.Empty;
        
        if (int.TryParse(selection, out var index))
        {
            if (index == 1)
            {
                // Start new thread
                Console.Write("First message: ");
                var firstMessage = Console.ReadLine() ?? string.Empty;
                
                if (!string.IsNullOrWhiteSpace(firstMessage))
                {
                    currentThreadId = await threadManager.CreateThreadAsync(username, firstMessage);
                    var thread = await threadManager.GetThreadAsync(username, currentThreadId);
                    currentThreadName = thread?.ThreadName ?? "New Thread";
                    
                    Console.WriteLine($"\nCreated new thread: {currentThreadName} (ID: {currentThreadId})\n");
                    
                    // Send first message and get response
                    var response = await chatAgent.ChatAsync(username, currentThreadId, firstMessage);
                    Console.WriteLine($"Agent: {response}\n");
                }
            }
            else if (index > 1 && index < userThreads.Count + 2)
            {
                // Load existing thread
                var selectedThread = userThreads[index - 2];
                currentThreadId = selectedThread.Id;
                currentThreadName = selectedThread.ThreadName;
                logger.LogInformation("Thread switched | UserId: {UserId} | ThreadId: {ThreadId} | ThreadName: {ThreadName}", username, currentThreadId, currentThreadName);
                Console.WriteLine($"\nLoaded thread: {currentThreadName}\n");
                
                // Display thread history
                if (selectedThread.Messages.Count > 0)
                {
                    Console.WriteLine("--- Conversation History ---");
                    foreach (var msg in selectedThread.Messages)
                    {
                        if (msg.Role == "user")
                        {
                            Console.WriteLine($"{username}: {msg.Content}");
                        }
                        else if (msg.Role == "assistant")
                        {
                            Console.WriteLine($"Agent: {msg.Content}");
                        }
                    }
                    Console.WriteLine("--- End of History ---\n");
                }
            }
            else if (index == userThreads.Count + 2)
            {
                // Exit application
                Console.WriteLine("Goodbye!");
                var sessionDuration = DateTime.UtcNow - sessionStart;
                logger.LogInformation("Session ended | UserId: {UserId} | Duration: {DurationMs}ms", username, sessionDuration.TotalMilliseconds);
                shouldExit = true;
                continue;
            }
        }
        
        // Chat loop for selected thread
        while (currentThreadId != null)
        {
            Console.WriteLine();
            var prompt = $"{username} [{currentThreadName}]> ";
            Console.Write(prompt);

            var input = Console.ReadLine() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                // Return to thread selection
                break;
            }

            // Send message to current thread
            try
            {
                var response = await chatAgent.ChatAsync(username, currentThreadId, input);
                Console.WriteLine($"\nAgent: {response}\n");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending message");
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in thread selection");
        Console.WriteLine($"Error: {ex.Message}");
    }
}

await host.RunAsync();


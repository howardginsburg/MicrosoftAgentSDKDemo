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
using Serilog;

// Configure Serilog with timestamped log file
var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDirectory);
var logFileName = $"agent_{DateTime.Now:yyyyMMdd_HHmmss}.log";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(
        Path.Combine(logsDirectory, logFileName),
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
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
        services.AddSingleton<IAzureAuthenticationService, AzureAuthenticationService>();
        services.AddSingleton<AgentSessionManager>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var consoleUI = host.Services.GetRequiredService<IConsoleUI>();
var authService = host.Services.GetRequiredService<IAzureAuthenticationService>();
var sessionManager = host.Services.GetRequiredService<AgentSessionManager>();

logger.LogInformation("Application started | Framework: Microsoft Agent Framework");
logger.LogInformation("Log file: {LogFile}", Path.Combine(logsDirectory, logFileName));

bool shouldExitApp = false;

// Main application loop - allows multiple users to log in sequentially
while (!shouldExitApp)
{
    // Display application logo
    consoleUI.DisplayLogo();
    
    // Verify Azure CLI login before proceeding
    var isAuthenticated = await authService.VerifyAndDisplayAuthenticationAsync();
    if (!isAuthenticated)
    {
        return;
    }

    // Get username
    var username = await consoleUI.GetUsernameAsync();
    
    // Run user session
    await sessionManager.RunUserSessionAsync(username);
}

logger.LogInformation("Application exiting");
Log.CloseAndFlush();

await host.RunAsync();


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
        services.AddSingleton<IAzureAuthenticationService, AzureAuthenticationService>();
        services.AddSingleton<AgentSessionManager>();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var consoleUI = host.Services.GetRequiredService<IConsoleUI>();
var authService = host.Services.GetRequiredService<IAzureAuthenticationService>();
var sessionManager = host.Services.GetRequiredService<AgentSessionManager>();

logger.LogInformation("Application started | Framework: Microsoft Agent Framework");

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

await host.RunAsync();


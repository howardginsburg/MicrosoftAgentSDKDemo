# Microsoft Agent Framework SDK Demo

A .NET 8.0 console application demonstrating a multi-turn conversational agent using the Microsoft Agent Framework SDK with Azure OpenAI and Cosmos DB.

## Features

- ✅ **Multi-turn conversations** with automatic thread management
- ✅ **Thread persistence** in Azure Cosmos DB (embedded messages)
- ✅ **Auto-named threads** based on first user message
- ✅ **Azure OpenAI integration** (GPT-4 support)
- ✅ **System instructions** for controlled agent behavior
- ✅ **Conversation history** display when loading threads
- ✅ **Comprehensive telemetry** with Application Insights
- ✅ **Simple CLI interface** with thread switching

## Quick Start

### Prerequisites
- .NET 8.0 SDK
- Azure subscription with Azure OpenAI and Cosmos DB
- Azure CLI logged in (`az login`)

### Setup

1. **Create Azure Resources** - See [SETUP.md](SETUP.md) for step-by-step instructions

2. **Configure appsettings.json**
   
   Edit [src/appsettings.json](src/appsettings.json) and replace the placeholder values:
   ```json
   {
     "AzureOpenAI": {
       "Endpoint": "https://your-resource.openai.azure.com/",
       "DeploymentName": "gpt-4o",
       "ApiVersion": "2025-01-01-preview",
       "SystemInstructions": "Your system instructions here"
     },
     "CosmosDB": {
       "Endpoint": "https://your-resource.documents.azure.com:443/",
       "AccountKey": "your-account-key"
     }
   }
   ```
   
   **Note:** Authentication uses `AzureCliCredential` (requires `az login`). AccountKey is needed for Cosmos DB.

3. **Run**
   ```powershell
   cd src
   dotnet run
   ```

## CLI Usage

```
Enter your username: Alice

Select a thread:
  1. [NEW] - Start a new conversation
  2. Previous thread name
  3. [QUIT] - Exit the application

Enter thread number: 1
First message: What is Azure?
(Agent responds and thread loads)

Alice [What is Azure?]> Tell me more about managed identities
(Continue conversation)

Alice [What is Azure?]> /quit
(Returns to thread selection)
```

## Architecture

- **ChatAgent** - Orchestrates conversations with Azure OpenAI, loads system instructions
- **ThreadManager** - Manages thread and message storage in Cosmos DB
- **TelemetryService** - Logs all interactions to Application Insights
- **CLI Interface** - Menu-driven thread selection and chat

All messages and full conversation context are persisted and logged.

## Project Structure

```
src/
├── Program.cs                        # CLI entry point and DI setup
├── appsettings.json                 # Configuration with system instructions
├── MicrosoftAgentSDKDemo.csproj      # .NET 8.0 project file
├── Models/
│   └── ThreadDocument.cs             # Thread with embedded messages
└── Services/
    ├── ChatAgent.cs                  # Azure OpenAI orchestration
    ├── ThreadManager.cs              # Cosmos DB CRUD operations
    └── TelemetryService.cs           # Application Insights events
```

## Configuration

See [SETUP.md](SETUP.md) for:
- Detailed Azure resource setup
- Connection string configuration
- System instructions setup
- Telemetry and monitoring
- Production deployment guidelines
- Security best practices

## Key Files

| File | Purpose |
|------|---------|
| [README.md](README.md) | This quick start guide |
| [SETUP.md](SETUP.md) | Complete setup and configuration guide |
| [src/Program.cs](src/Program.cs) | CLI entry point and dependency injection |
| [src/Services/ChatAgent.cs](src/Services/ChatAgent.cs) | Agent logic and OpenAI integration |
| [src/Services/ThreadManager.cs](src/Services/ThreadManager.cs) | Thread and message persistence |
| [src/Services/TelemetryService.cs](src/Services/TelemetryService.cs) | Application Insights integration |

## Telemetry

All interactions are logged to Application Insights including:
- User messages (full content)
- Agent responses (full content, tokens, latency)
- Thread creation and switching
- Session start/end times
- Errors and exceptions

View logs in Azure Portal under Application Insights → Logs

## Troubleshooting

Common issues and solutions are documented in [SETUP.md](SETUP.md#troubleshooting)

Quick checks:
1. Verify Azure resources exist: `az resource list --query "[].name"`
2. Test authentication: `az account show`
3. Check appsettings.json has correct endpoint and account key
4. Ensure Cosmos DB container exists with correct partition key

## Next Steps

1. Review [SETUP.md](SETUP.md) for detailed configuration
2. Create Azure resources using the provided Azure CLI commands
3. Edit src/appsettings.json with your connection strings
4. Run `dotnet run` in the src directory
5. Start chatting with the agent!

## Support

For detailed information, refer to [SETUP.md](SETUP.md) which includes:
- Step-by-step Azure resource setup
- Troubleshooting guide
- Production deployment guidelines
- Security best practices
- Resource links and documentation

## License

MIT License

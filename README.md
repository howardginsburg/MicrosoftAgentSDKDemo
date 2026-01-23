# Microsoft Agent SDK Demo

A console-based AI agent application built with the Microsoft Agent Framework, featuring multi-user conversations, Azure OpenAI integration, Model Context Protocol (MCP) tools, and persistent chat history.

## Features

- 🤖 **AI Agent powered by Azure OpenAI** - Uses GPT-4o for intelligent conversations
- 📚 **MCP Integration** - Connects to Microsoft Learn documentation via Model Context Protocol
- 💾 **Persistent Storage** - Conversation history stored in Azure Cosmos DB
- 👥 **Multi-User Support** - Isolated conversations per user with data partitioning
- 🎨 **Rich Console UI** - Beautiful terminal interface using Spectre.Console
- 🔄 **Thread Management** - Create, resume, and manage conversation threads
- 📜 **Conversation History** - Full chat history displayed when loading threads

## Prerequisites

- .NET 8.0 SDK
- Azure CLI
- Azure Subscription with:
  - Azure OpenAI Service (GPT-4o deployment)
  - Azure Cosmos DB account
  - (Optional) Application Insights

## Quick Start

### Setup

1. **Clone and navigate to the project**
   ```bash
   git clone <repository-url>
   cd MicrosoftAgentSDKDemo
   ```

2. **Login to Azure**
   ```bash
   az login
   ```
   Ensure your account has `Cognitive Services OpenAI User` role on the Azure OpenAI resource.

3. **Configure the application**
   ```bash
   cd src
   cp appsettings.json.sample appsettings.json
   ```
   Edit `appsettings.json` with your Azure resource details:
   - `AzureOpenAI:Endpoint` - Your Azure OpenAI endpoint URL
   - `AzureOpenAI:DeploymentName` - Your GPT-4o deployment name
   - `CosmosDB:Endpoint` - Your Cosmos DB account endpoint
   - `CosmosDB:AccountKey` - Your Cosmos DB account key

4. **Create Cosmos DB resources**
   
   Create a database `agent-database` and container `conversations` with partition key `/id`

5. **Build and run**
   ```bash
   dotnet build
   dotnet run
   ```

## Usage

- Enter your username to start a session
- Use **arrow keys** to navigate the thread selection menu
- Select **"📝 Start a new conversation"** to begin
- Ask questions about Azure (the agent has access to Microsoft Learn docs)
- Type `quit` in chat to return to thread menu
- Select **"🚪 Logout"** at thread menu to switch users

### Example Session

```
╭─────────────────────────────────────╮
│                                     │
│    _                    _           │
│   / \   __ _  ___ _ __ | |_         │
│  / _ \ / _` |/ _ \ '_ \| __|        │
│ / ___ \ (_| |  __/ | | | |_         │
│/_/   \_\__, |\___|_| |_|\__|        │
│        |___/                        │
│   ____  ____  _  __                 │
│  / ___||  _ \| |/ /                 │
│  \___ \| | | | ' /                  │
│   ___) | |_| | . \                  │
│  |____/|____/|_|\_\                 │
│   ____                              │
│  |  _ \  ___ _ __ ___   ___         │
│  | | | |/ _ \ '_ ` _ \ / _ \        │
│  | |_| |  __/ | | | | | (_) |       │
│  |____/ \___|_| |_| |_|\___/        │
│                                     │
╰─────────────────────────────────────╯

Enter your username: Howard

────────── Howard's Conversation Threads ──────────

  ↑↓ Select an option:
  > 📝 Start a new conversation
    💬 what is azure sql
    💬 how does Fabric Spark compare to Databricks Spark?
    🚪 Logout
```

## Project Structure

```
src/
├── Program.cs                          # Main application entry point
├── Services/
│   ├── ChatAgent.cs                    # Azure OpenAI agent factory
│   ├── ConsoleUI.cs                    # Spectre.Console UI implementation
│   ├── MCPServerManager.cs             # MCP server connection manager
│   ├── CosmosDbAgentThreadStore.cs     # Thread persistence layer
│   └── CosmosDbChatMessageStore.cs     # Message persistence layer
├── prompts/
│   └── system-instructions.txt         # Agent behavior instructions
├── appsettings.json                    # Configuration (not in source control)
└── appsettings.json.sample             # Sample configuration template
```

## Configuration

See `appsettings.json.sample` for a complete configuration template. The application uses:
- **Azure CLI authentication** for Azure OpenAI (requires `az login` and `Cognitive Services OpenAI User` role)
- **Account key** for Cosmos DB
- **System instructions** in `src/prompts/system-instructions.txt` (customize agent behavior)

Key settings in `appsettings.json`:
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.cognitiveservices.azure.com",
    "DeploymentName": "gpt-4o",
    "SystemInstructionsFile": "prompts/system-instructions.txt"
  },
  "CosmosDB": {
    "Endpoint": "https://your-cosmos.documents.azure.com:443/",
    "AccountKey": "your-cosmos-key",
    "DatabaseName": "agent-database",
    "ContainerId": "conversations"
  }
}
```

## Architecture

Built on the Microsoft Agent Framework with:
- **AIHostAgent** - Wraps base agents with automatic thread persistence
- **CosmosDbAgentThreadStore** - Custom thread store using IStorage interface
- **CosmosDbChatMessageStore** - Custom message store for conversation history
- **MCP Integration** - Model Context Protocol for external tool access (Microsoft Learn)
- **Spectre.Console** - Rich terminal UI with interactive menus

For detailed architecture documentation, see [.github/copilot-instructions.md](.github/copilot-instructions.md)

## Documentation

- [.github/copilot-instructions.md](.github/copilot-instructions.md) - Comprehensive technical documentation
  - Architecture overview
  - Storage patterns
  - Critical patterns and conventions
  - Testing checklist
  - Common tasks and troubleshooting

## Technologies

- [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI) v1.0.0-preview - Agent framework
- [Microsoft.Agents.AI.Hosting](https://www.nuget.org/packages/Microsoft.Agents.AI.Hosting) v1.0.0-preview - AIHostAgent wrapper
- [Azure.AI.OpenAI](https://www.nuget.org/packages/Azure.AI.OpenAI) v2.1.0 - Azure OpenAI integration
- [ModelContextProtocol.Core](https://www.nuget.org/packages/ModelContextProtocol.Core) v0.2.0-preview.3 - MCP SDK
- [Spectre.Console](https://spectreconsole.net/) v0.54.0 - Rich terminal UI
- [Microsoft.Agents.Storage.CosmosDb](https://www.nuget.org/packages/Microsoft.Agents.Storage.CosmosDb) v1.3.176 - Cosmos DB storage

## Troubleshooting

**Authentication**: Run `az login` and verify with `az account show`. Ensure you have `Cognitive Services OpenAI User` role.

**Cosmos DB**: Verify container `conversations` exists with partition key `/id` and account key is correct.

**MCP Connection**: Requires internet access to https://learn.microsoft.com/api/mcp

For detailed troubleshooting, see [SETUP.md](SETUP.md).

## License

This is a demonstration project for educational purposes.

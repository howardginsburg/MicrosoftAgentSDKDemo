# Microsoft Agent SDK Demo - Setup Guide

## Project Overview

This is a .NET 8.0 console application that implements a multi-turn conversational agent using:
- **Microsoft Agent Framework** (AIHostAgent, AgentThreadStore patterns)
- **Azure AI Foundry** (GPT-4o) for AI conversations
- **Model Context Protocol (MCP)** for Microsoft Learn documentation access
- **Azure Cosmos DB** for persistent storage (IStorage interface)
- **Spectre.Console** for rich terminal UI
- **Azure CLI Credentials** for authentication

## Architecture

### Core Components

1. **ChatAgentFactory** (factory) - Creates Azure AI Foundry agents with MCP tools and chat history
2. **ReasoningChatClient** - Middleware that displays agent reasoning and tool invocations
3. **CosmosDbAgentThreadStore** - Manages thread metadata and user thread index
4. **CosmosDbChatMessageStore** - Persists conversation messages
5. **MCPServerManager** - Connects to configured MCP servers (supports multiple endpoints)
6. **FileAttachmentService** - Processes text and image file attachments
7. **MultimodalMessageHelper** - Constructs multimodal ChatMessages with content arrays
8. **ImageGenerationService** - DALL-E 3 image generation and local storage
9. **ConsoleUI** - Rich terminal interface with Spectre.Console
10. **AIHostAgent** - Framework wrapper providing automatic persistence

### Storage Architecture

Cosmos DB container `conversations` with partition key `/id` contains:

**Thread Documents**:
```json
{
  "id": "{userId}:{threadId}",
  "userId": "{userId}",
  "threadData": {
    "storeState": "chat-history-{guid}"
  }
}
```

**Thread Index Documents**:
```json
{
  "id": "thread-index:{userId}",
  "threads": [
    {
      "ThreadId": "{guid}",
      "Title": "First user message",
      "CreatedAt": "2026-01-23T..."
    }
  ]
}
```

**Chat History Documents**:
```json
{
  "id": "chat-history-{guid}",
  "userId": "{userId}",
  "messages": [
    {"Role": "user", "Text": "...", "Contents": [...]},
    {"Role": "assistant", "Text": "...", "Contents": [...]}
  ]
}
```

## Prerequisites

### Local Development
- **.NET 8.0 SDK** or higher - [Download](https://dotnet.microsoft.com/download)
- **PowerShell 7.0+** or Command Prompt
- **Azure CLI** - [Download](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- **Azure CLI logged in** - Run `az login`

### Azure Resources Required

You'll need the following Azure resources (created before running the app):

1. **Azure AI Foundry (for GPT-4o and DALL-E 3)**
   - Resource name: `{your-foundry-resource-name}` (e.g., `agent-demo-foundry`)
   - Project name: `{your-project-name}` (e.g., `agent-demo-project`)
   - Deployments: `gpt-4o` and `dall-e-3`
   - Endpoint: `https://{your-foundry-resource-name}.cognitiveservices.azure.com`
   - Region: East US, Sweden Central, or other available regions
   - Kind: AIServices (with --allow-project-management)

2. **Azure Cosmos DB Account**
   - Database: `agent-database`
   - Container: `conversations`
   - Partition Key: `/id` (single path)

3. **Application Insights** (Optional but recommended)
   - Instrumentation Key for telemetry
   - Log Analytics Workspace for detailed logs

### Azure Authentication

The app uses **AzureCliCredential** for all Azure service authentication:
- **Azure AI Foundry**: `AzureCliCredential` (requires `Azure AI Developer` role)
- **Cosmos DB**: `AzureCliCredential` (requires `Cosmos DB Built-in Data Contributor` role via RBAC script)

You must have:
- Azure CLI logged in: `az login`
- Required RBAC roles assigned to your user (see setup steps below)

## Setup Steps

### 1. Create Azure Resources

#### Create Resource Group
```bash
resourceGroup="agent-demo-rg"
location="eastus"

az group create --name $resourceGroup --location $location
```

#### Create Azure AI Foundry Resource
```bash
# Create the Foundry resource
az cognitiveservices account create \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --kind AIServices \
  --sku s0 \
  --location $location \
  --allow-project-management

# Create a custom subdomain (must be globally unique)
az cognitiveservices account update \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --custom-domain agent-demo-foundry

# Create the project
az cognitiveservices account project create \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --project-name agent-demo-project \
  --location $location

# Deploy GPT-4o model
az cognitiveservices account deployment create \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --deployment-name gpt-4o \
  --model-name gpt-4o \
  --model-version "2024-11-20" \
  --model-format OpenAI \
  --sku-name GlobalStandard \
  --sku-capacity 10

# Deploy DALL-E 3 model
az cognitiveservices account deployment create \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --deployment-name dall-e-3 \
  --model-name dall-e-3 \
  --model-version "3.0" \
  --model-format OpenAI \
  --sku-name Standard \
  --sku-capacity 1
```

#### Create Cosmos DB Account
```bash
az cosmosdb create \
  --name agent-demo-cosmos \
  --resource-group $resourceGroup \
  --locations regionName=$location failoverPriority=0 \
  --default-consistency-level "Session"

# Create database
az cosmosdb sql database create \
  --account-name agent-demo-cosmos \
  --resource-group $resourceGroup \
  --name agent-database

# Create container with partition key /id (framework default)
az cosmosdb sql container create \
  --account-name agent-demo-cosmos \
  --database-name agent-database \
  --name conversations \
  --partition-key-path "/id" \
  --resource-group $resourceGroup \
  --throughput 400
```

#### Create Application Insights (Optional)
```bash
az monitor app-insights component create \
  --app agent-demo-insights \
  --location $location \
  --resource-group $resourceGroup
```

### 2. Assign Required RBAC Roles

#### For Azure AI Foundry
```bash
# Get the project's resource ID
projectId=$(az cognitiveservices account project show \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --project-name agent-demo-project \
  --query id -o tsv)

# Assign Azure AI Developer role
az role assignment create \
  --role "Azure AI Developer" \
  --assignee $(az ad signed-in-user show --query id -o tsv) \
  --scope $projectId
```

#### For Azure Cosmos DB (Required - Enable RBAC)

The application uses Azure CLI credentials for Cosmos DB authentication. Use the provided script to grant permissions:

```bash
# Make script executable (Linux/macOS)
chmod +x scripts/grant-cosmos-rbac.sh

# Run the script
./scripts/grant-cosmos-rbac.sh agent-demo-rg agent-demo-cosmos

# On Windows (Git Bash)
bash scripts/grant-cosmos-rbac.sh agent-demo-rg agent-demo-cosmos
```

This assigns the "Cosmos DB Built-in Data Contributor" role to your Azure CLI user.

See [scripts/README.md](scripts/README.md) for more details.

### 3. Get Connection Strings and Keys

```bash
# Azure AI Foundry
foundryEndpoint=$(az cognitiveservices account show \
  --name agent-demo-foundry \
  --resource-group $resourceGroup \
  --query properties.endpoint -o tsv)

echo "Foundry Endpoint: $foundryEndpoint"

# Cosmos DB Endpoint
cosmosEndpoint=$(az cosmosdb show \
  --name agent-demo-cosmos \
  --resource-group $resourceGroup \
  --query documentEndpoint -o tsv)

echo "Cosmos DB Endpoint: $cosmosEndpoint"

# Application Insights
aiKey=$(az monitor app-insights component show \
  --app agent-demo-insights \
  --resource-group $resourceGroup \
  --query instrumentationKey -o tsv)

echo "Application Insights Key: $aiKey"
```

### 4. Configure Application

Copy the sample configuration and update with your Azure resource details:

```bash
cd src
cp appsettings.json.sample appsettings.json
```

Edit `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MicrosoftAgentSDKDemo": "Information",
      "Microsoft": "Warning"
    }
  },
  "ApplicationInsights": {
    "InstrumentationKey": "your-instrumentation-key-optional"
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-foundry-resource.cognitiveservices.azure.com",
    "DeploymentName": "gpt-4o",
    "DallEEndpoint": "https://your-foundry-resource.cognitiveservices.azure.com",
    "DallEDeploymentName": "dall-e-3",
    "SystemInstructionsFile": "prompts/system-instructions.txt"
  },
  "CosmosDB": {
    "Endpoint": "https://your-cosmos.documents.azure.com:443/",
    "DatabaseName": "agent-database",
    "ContainerId": "conversations"
  }
}
```

Replace:
- `your-foundry-resource.cognitiveservices.azure.com` - your Azure AI Foundry endpoint
- `your-cosmos.documents.azure.com` - your Cosmos DB endpoint
- `your-instrumentation-key-optional` - Application Insights key (optional)

### 5. Run the Application

```bash
cd src
dotnet run

# You'll be prompted for username
# Select a thread or create a new one
```

## Application Usage

Once the app is running:

```
╭─────────────────────────────────────╮
│    Agent SDK Demo (ASCII art)      │
╰─────────────────────────────────────╯

Enter your username: Alice

────────── Alice's Conversation Threads ──────────

  ↑↓ Use arrow keys to select:
  > 📝 Start a new conversation
    💬 what is azure sql
    💬 analyze this architecture diagram
    💬 how does Fabric Spark compare to Databricks?
    🚪 Logout

(Select existing thread to see full conversation history)

> 📝 Start a new conversation

What would you like to talk about? Analyze this diagram and explain the architecture

Attach files? (Enter file paths separated by commas, or press Enter to skip)
Note: File attachments are only available when starting a new conversation
Files > C:\Users\Alice\documents\architecture.png

📎 Attached 1 file(s)

🤔 Agent is thinking...

╭──────────────────── 🤖 Agent ─────────────────────╮
│ Based on the architecture diagram, I can see...   │
│ [Analysis of the architecture diagram]            │
╰────────────────────────────────────────────────────╯

You> tell me the difference between azure synapse and fabric

🤔 Agent is thinking...

💭 Analyzing request: tell me the difference between azure synapse and fabric

🔧 Tool Invocations:
╭───────────────────────┬─────────────────────────────────────────────────────╮
│ Tool Name             │ Arguments                                           │
├───────────────────────┼─────────────────────────────────────────────────────┤
│ microsoft_docs_search │ query: Azure Synapse vs Microsoft Fabric comparison │
╰───────────────────────┴─────────────────────────────────────────────────────╯

╭──────────────────── 🤖 Agent ─────────────────────╮
│ Azure Synapse and Microsoft Fabric...             │
╰────────────────────────────────────────────────────╯

You> generate an image of an Azure architecture diagram

🤔 Agent is thinking...

╭──────── 🎨 Image Generated and Saved! ────────────╮
│ C:\...\images\dalle_20260124_161103.png          │
╰────────────────────────────────────────────────────╯
Opening image in default viewer...

You> quit
(Returns to thread selection)
```

### Image Generation

The agent can generate images using DALL-E 3:
- Simply ask the agent to "generate an image of..."
- Images are saved to `src/images/` folder with timestamp filenames
- Images automatically open in your default image viewer
- Supported formats: PNG (1024x1024, 1024x1792, 1792x1024)

### File Attachments

Attach files when starting a new conversation:

**Supported Text File Types** (up to 10MB):
- Code files: `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`
- Documentation: `.txt`, `.md`
- Data files: `.json`, `.xml`, `.csv`, `.log`
- Web files: `.html`, `.css`
- Configuration: `.yaml`, `.yml`, `.toml`, `.ini`, `.config`

**Supported Image Types** (up to 10MB):
- `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.webp`

**Usage**:
1. Select "📝 Start a new conversation"
2. Enter your message/question
3. When prompted for file attachments, enter comma-separated paths:
   ```
   Files > C:\code\api.cs, C:\images\diagram.png
   ```
4. Press Enter to skip if no files needed

**Important Notes**:
- File attachments are **only available when starting new conversations**
- Cannot attach files in ongoing conversations (by design)
- Images enable vision model analysis
- Text files are formatted as code blocks for better readability
- Files exceeding 10MB are rejected with an error message

### Tool Invocation Display

The agent displays its reasoning process:
- Shows when analyzing user requests
- Displays a table of MCP tools being invoked (e.g., microsoft_docs_search)
- Shows tool arguments being passed
- Helps understand how the agent is researching and responding

## MCP Integration

The application connects to configured MCP servers via `appsettings.json`:

```json
"MCPServers": {
  "Servers": [
    {
      "Name": "Microsoft Learn",
      "Endpoint": "https://learn.microsoft.com/api/mcp",
      "Enabled": true,
      "TimeoutSeconds": 30
    }
  ]
}
```

### MCP Server Configuration

Each MCP server has the following properties:
- **Name**: Friendly name for logging and identification
- **Endpoint**: The HTTPS endpoint URL for the MCP server
- **Enabled**: Set to `false` to temporarily disable without removing configuration
- **TimeoutSeconds**: Connection timeout (default: 30)

### Microsoft Learn MCP Server

The default configuration includes Microsoft Learn which provides:
- **microsoft_docs_search** - Search official Microsoft documentation
- **microsoft_code_sample_search** - Find code examples
- **microsoft_docs_fetch** - Retrieve full documentation pages

### Adding Additional MCP Servers

To add more MCP servers, simply add entries to the `Servers` array:

```json
"MCPServers": {
  "Servers": [
    {
      "Name": "Microsoft Learn",
      "Endpoint": "https://learn.microsoft.com/api/mcp",
      "Enabled": true,
      "TimeoutSeconds": 30
    },
    {
      "Name": "Your Custom Server",
      "Endpoint": "https://your-server.com/api/mcp",
      "Enabled": true,
      "TimeoutSeconds": 60
    }
  ]
}
```

All tools from all enabled MCP servers are automatically integrated with the agent.

## System Instructions

Customize agent behavior by editing `src/prompts/system-instructions.txt`:

```text
You are a helpful AI assistant with access to Microsoft Learn documentation...
```

The file path is configured in appsettings.json:
```json
"SystemInstructionsFile": "prompts/system-instructions.txt"
```

Benefits of separate file:
- No JSON escaping needed
- Better version control for prompt changes
- Easier collaboration with prompt engineers
- Support for multi-line instructions with proper formatting

## Development Tips

### Rebuild
```bash
cd src
dotnet build
```

### Run with Debug Logging
```bash
cd src
dotnet run
```

Adjust `LogLevel.MicrosoftAgentSDKDemo` to `Debug` in appsettings.json for verbose logging.

### Clean Build
```bash
cd src
dotnet clean
dotnet build
```

## Troubleshooting

### Issue: "Endpoint not configured"
**Solution**: Verify `appsettings.json` has `AzureOpenAI:Endpoint` with your actual Azure AI Foundry endpoint URL

### Issue: "Authentication failed" for Azure AI Foundry
**Solution**:
- Run `az login` to authenticate with Azure
- Verify your account has `Azure AI Developer` role:
  ```bash
  az role assignment list --assignee <your-email>
  ```
- Check subscription: `az account show`

### Issue: "CosmosDB connection failed"
**Solution**: 
- Verify Cosmos DB endpoint and account key in appsettings.json
- Ensure container `conversations` exists with partition key `/id`
- Check network connectivity to Cosmos DB

### Issue: "MCP connection failed"
**Solution**:
- Requires internet connectivity to https://learn.microsoft.com/api/mcp
- Check firewall/proxy settings
- Verify corporate network allows SSE (Server-Sent Events)

### Issue: "No threads found" on first run
**Solution**: This is normal. Select "📝 Start a new conversation" to create your first thread.

### Issue: Thread history not loading
**Solution**: 
- Verify userId is consistent (case-sensitive)
- Check Cosmos DB for documents with id format `{userId}:{threadId}`
- Ensure chat history documents exist (id format `chat-history-{guid}`)

### Issue: File attachments not working
**Solution**:
- Verify file paths are correct and files exist
- Check file size is under 10MB limit
- Ensure file extensions are supported (see File Attachments section)
- File attachments only work when **starting new conversations**, not in ongoing chats
- Use comma-separated paths for multiple files
- Check logs for specific file processing errors

### Issue: Images not being analyzed
**Solution**:
- Verify image file format is supported (.jpg, .jpeg, .png, .gif, .bmp, .webp)
- Ensure GPT-4o deployment supports vision capabilities
- Check that FileAttachmentService is properly processing images as DataContent
- Review logs for "image attachments - using vision model" message

## File Structure

```
src/
├── Program.cs                        # Entry point with nested loop for multi-user sessions
├── Agents/
│   └── ChatAgentFactory.cs           # Azure AI Foundry agent factory with tools
├── Display/
│   ├── ConsoleUI.cs                  # Spectre.Console UI components
│   └── ReasoningChatClient.cs        # Tool invocation display middleware
├── Storage/
│   ├── CosmosDbAgentThreadStore.cs   # Thread metadata persistence
│   └── CosmosDbChatMessageStore.cs   # Conversation message persistence
├── Integration/
│   ├── MCPServerManager.cs           # Configurable MCP server connection manager
│   ├── ImageGenerationService.cs     # DALL-E 3 image generation service
│   ├── FileAttachmentService.cs      # File attachment processing (text and images)
│   └── MultimodalMessageHelper.cs    # Multimodal ChatMessage construction
├── Models/
│   └── MCPServerConfiguration.cs     # MCP server configuration models
├── prompts/
│   └── system-instructions.txt       # Agent system instructions
└── images/                           # Generated images (auto-created)

.github/
└── copilot-instructions.md           # Comprehensive technical documentation

scripts/
├── README.md                         # Script documentation
├── grant-cosmos-rbac.sh              # Grant Cosmos DB RBAC permissions
└── grant-foundry-rbac.sh             # Grant Azure AI Foundry RBAC permissions
```

## Security Considerations

### For Production
1. Use **Managed Identities** instead of account keys
2. Store secrets in **Azure Key Vault**
3. Enable **network restrictions** on Cosmos DB and OpenAI
4. Use **application-level encryption** for sensitive data
5. Implement **audit logging** and monitoring
6. Rotate API keys regularly
7. Never commit appsettings.json with real credentials

### For Development
1. Add appsettings.json to .gitignore
2. Keep test keys separate from production keys
3. Use separate Cosmos DB containers for dev/test/prod
4. Consider using Azure Key Vault for managing secrets

## For Developers

For comprehensive technical documentation including:
- Architecture details
- Storage patterns and document structures
- Critical patterns and conventions
- Testing checklist
- Common tasks and debugging

See: [.github/copilot-instructions.md](.github/copilot-instructions.md)

## Next Steps

1. **Customize system instructions** for your domain
2. **Add custom MCP servers** for specialized tools (now configurable!)
3. **Implement conversation export** functionality
4. **Add thread search/filtering** capabilities
5. **Enable streaming responses** for real-time feedback
6. **Deploy to Azure Container Apps** or App Service
7. **Build web UI** with Blazor or React
8. **Extend file attachment support** to ongoing conversations
9. **Add file type validation** and preview functionality
10. **Implement file attachment history** display in conversation threads

## Resources

- [Microsoft Agent Framework](https://www.nuget.org/packages/Microsoft.Agents.AI)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Spectre.Console](https://spectreconsole.net/)
- [Azure Cosmos DB Documentation](https://learn.microsoft.com/azure/cosmos-db/)

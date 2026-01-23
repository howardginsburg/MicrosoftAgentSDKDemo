# Microsoft Agent SDK Demo - Setup Guide

## Project Overview

This is a .NET 8.0 console application that implements a multi-turn conversational agent using:
- **Microsoft Agent Framework** (AIHostAgent, AgentThreadStore patterns)
- **Azure OpenAI** (GPT-4o) for AI conversations
- **Model Context Protocol (MCP)** for Microsoft Learn documentation access
- **Azure Cosmos DB** for persistent storage (IStorage interface)
- **Spectre.Console** for rich terminal UI
- **Azure CLI Credentials** for authentication

## Architecture

### Core Components

1. **ChatAgent** (factory) - Creates Azure OpenAI agents with MCP tools and chat history
2. **CosmosDbAgentThreadStore** - Manages thread metadata and user thread index
3. **CosmosDbChatMessageStore** - Persists conversation messages
4. **MCPServerManager** - Connects to Microsoft Learn MCP server
5. **ConsoleUI** - Rich terminal interface with Spectre.Console
6. **AIHostAgent** - Framework wrapper providing automatic persistence

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

1. **Azure OpenAI Service**
   - Resource name: `{your-resource-name}`
   - Deployment: `gpt-4o` (or your preferred model)
   - Endpoint: `https://{your-resource-name}.openai.azure.com/`
   - Region: East US, France Central, or other available regions

2. **Azure Cosmos DB Account**
   - Database: `agent-database`
   - Container: `conversations`
   - Partition Key: `/userId` (single path)

3. **Application Insights** (Optional but recommended)
   - Instrumentation Key for telemetry
   - Log Analytics Workspace for detailed logs

### Azure Authentication

The app uses **AzureCliCredential** for authentication:
- Azure OpenAI: `AzureCliCredential`
- Cosmos DB: Uses account key from configuration

You must have:
- Azure CLI logged in: `az login`
- Required RBAC roles assigned to your user

## Setup Steps

### 1. Create Azure Resources

#### Create Resource Group
```powershell
$resourceGroup = "agent-demo-rg"
$location = "eastus"

az group create --name $resourceGroup --location $location
```

#### Create Azure OpenAI Resource
```powershell
az cognitiveservices account create `
  --name agent-demo-openai `
  --resource-group $resourceGroup `
  --location $location `
  --kind OpenAI `
  --sku s0

# Deploy GPT-4o model
az cognitiveservices account deployment create `
  --name agent-demo-openai `
  --resource-group $resourceGroup `
  --deployment-name gpt-4o `
  --model-name gpt-4 `
  --model-version "turbo-2024-04-09" `
  --sku-name "standard" `
  --sku-capacity 1
```

#### Create Cosmos DB Account
```powershell
az cosmosdb create `
  --name agent-demo-cosmos `
  --resource-group $resourceGroup `
  --locations regionName=$location failoverPriority=0 `
  --default-consistency-level "Session"

# Create database
az cosmosdb sql database create `
  --account-name agent-demo-cosmos `
  --resource-group $resourceGroup `
  --name agent-database

# Create container with partition key /id (framework default)
az cosmosdb sql container create `
  --account-name agent-demo-cosmos `
  --database-name agent-database `
  --name conversations `
  --partition-key-path "/id" `
  --resource-group $resourceGroup `
  --throughput 400
```

#### Create Application Insights (Optional)
```powershell
az monitor app-insights component create `
  --app agent-demo-insights `
  --location $location `
  --resource-group $resourceGroup
```

### 2. Assign Required RBAC Roles

#### For Azure OpenAI
```powershell
# Get your user object ID
$userObjectId = az ad signed-in-user show --query id -o tsv

# Assign Cognitive Services OpenAI User role
az role assignment create `
  --role "Cognitive Services OpenAI User" `
  --assignee $userObjectId `
  --scope /subscriptions/{subscription-id}/resourceGroups/$resourceGroup/providers/Microsoft.CognitiveServices/accounts/agent-demo-openai
```

### 3. Get Connection Strings and Keys

```powershell
# Azure OpenAI
$openAIEndpoint = az cognitiveservices account show `
  --name agent-demo-openai `
  --resource-group $resourceGroup `
  --query properties.endpoint -o tsv

Write-Host "OpenAI Endpoint: $openAIEndpoint"

# Cosmos DB Endpoint
$cosmosEndpoint = az cosmosdb show `
  --name agent-demo-cosmos `
  --resource-group $resourceGroup `
  --query documentEndpoint -o tsv

Write-Host "Cosmos DB Endpoint: $cosmosEndpoint"

# Cosmos DB Account Key
$accountKey = az cosmosdb keys list `
  --name agent-demo-cosmos `
  --resource-group $resourceGroup `
  --query primaryMasterKey -o tsv

Write-Host "Cosmos DB Account Key: $accountKey"

# Application Insights
$aiKey = az monitor app-insights component show `
  --app agent-demo-insights `
  --resource-group $resourceGroup `
  --query instrumentationKey -o tsv

Write-Host "Application Insights Key: $aiKey"
```

### 4. Configure Application

Copy the sample configuration and update with your Azure resource details:

```powershell
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
    "Endpoint": "https://your-openai.cognitiveservices.azure.com",
    "DeploymentName": "gpt-4o",
    "SystemInstructions": "You are a helpful AI assistant with access to Microsoft Learn documentation. Answer questions accurately and cite sources when possible."
  },
  "CosmosDB": {
    "Endpoint": "https://your-cosmos.documents.azure.com:443/",
    "AccountKey": "your-cosmos-account-key",
    "DatabaseName": "agent-database",
    "ContainerId": "conversations"
  }
}
```

Replace:
- `your-openai.cognitiveservices.azure.com` - your Azure OpenAI endpoint
- `your-cosmos.documents.azure.com` - your Cosmos DB endpoint
- `your-cosmos-account-key` - Cosmos DB account key
- `your-instrumentation-key-optional` - Application Insights key (optional)

### 5. Run the Application

```powershell
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
    💬 how does Fabric Spark compare to Databricks?
    🚪 Logout

(Select existing thread to see full conversation history)

What would you like to talk about? Tell me about managed identities

🤔 Agent is thinking...

╭──────────────────── 🤖 Agent ─────────────────────╮
│ Managed identities are a feature of Azure...      │
╰────────────────────────────────────────────────────╯

You> quit
(Returns to thread selection)
```

## MCP Integration

The application connects to Microsoft Learn's MCP server to provide:
- **microsoft_docs_search** - Search official Microsoft documentation
- **microsoft_code_sample_search** - Find code examples
- **microsoft_docs_fetch** - Retrieve full documentation pages

Endpoint: https://learn.microsoft.com/api/mcp (no configuration needed)

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
```powershell
cd src
dotnet build
```

### Run with Debug Logging
```powershell
cd src
dotnet run
```

Adjust `LogLevel.MicrosoftAgentSDKDemo` to `Debug` in appsettings.json for verbose logging.

### Clean Build
```powershell
cd src
dotnet clean
dotnet build
```

## Troubleshooting

### Issue: "Endpoint not configured"
**Solution**: Verify `appsettings.json` has `AzureOpenAI:Endpoint` with your actual Azure OpenAI endpoint URL

### Issue: "Authentication failed" for Azure OpenAI
**Solution**:
- Run `az login` to authenticate with Azure
- Verify your account has `Cognitive Services OpenAI User` role:
  ```powershell
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

## File Structure

```
src/
├── Program.cs                        # Entry point with nested loop for multi-user sessions
├── appsettings.json                  # Configuration (not in source control)
├── appsettings.json.sample           # Sample configuration template
├── MicrosoftAgentSDKDemo.csproj      # Project file with NuGet packages
├── prompts/
│   └── system-instructions.txt       # Agent behavior instructions (edit to customize)
└── Services/
    ├── ChatAgent.cs                  # Azure OpenAI agent factory
    ├── ConsoleUI.cs                  # Spectre.Console UI implementation
    ├── MCPServerManager.cs           # MCP server connection manager
    ├── CosmosDbAgentThreadStore.cs   # Thread metadata persistence
    └── CosmosDbChatMessageStore.cs   # Conversation message persistence

.github/
└── copilot-instructions.md           # Comprehensive technical documentation
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
2. **Add custom MCP servers** for specialized tools
3. **Implement conversation export** functionality
4. **Add thread search/filtering** capabilities
5. **Enable streaming responses** for real-time feedback
6. **Deploy to Azure Container Apps** or App Service
7. **Build web UI** with Blazor or React

## Resources

- [Microsoft Agent Framework](https://www.nuget.org/packages/Microsoft.Agents.AI)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Spectre.Console](https://spectreconsole.net/)
- [Azure Cosmos DB Documentation](https://learn.microsoft.com/azure/cosmos-db/)

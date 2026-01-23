# Microsoft Agent Framework SDK Demo - Setup Guide

## Project Overview

This is a .NET 8.0 console application that implements a multi-turn conversational agent using:
- **Microsoft Agent Framework SDK** for storage with Cosmos DB
- **Azure OpenAI** for AI intelligence (GPT-4)
- **Azure Cosmos DB** for thread and message persistence (with embedded messages)
- **Application Insights** for telemetry and monitoring
- **Azure CLI Credentials** for authentication

## Architecture

### Core Components

1. **ChatAgent** - Manages conversation flow with Azure OpenAI, applies system instructions
2. **ThreadManager** - Handles thread/conversation storage in Cosmos DB
3. **TelemetryService** - Tracks all interactions to Application Insights
4. **CLI Interface** - Menu-driven thread selection and chat

### Data Model

**Threads** (Cosmos DB documents with embedded messages):
- Id (GUID)
- UserId (username)
- ThreadName (auto-generated from first user message)
- CreatedDate, LastActivity timestamps
- MessageCount
- Messages[] (array with embedded message objects)

**Messages** (embedded in thread):
- Id (GUID)
- Role ("user" or "assistant")
- Content (full message text)
- Timestamp

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

# Create container with partition key
az cosmosdb sql container create `
  --account-name agent-demo-cosmos `
  --database-name agent-database `
  --name conversations `
  --partition-key-paths "/userId" `
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

#### For Cosmos DB
```powershell
# Assign Cosmos DB Built-in Data Contributor role
az cosmosdb sql role assignment create `
  --account-name agent-demo-cosmos `
  --resource-group $resourceGroup `
  --role-definition-name "Cosmos DB Built-in Data Contributor" `
  --principal-id $userObjectId `
  --scope "/"
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

Edit [src/appsettings.json](src/appsettings.json) and update with your Azure resource details:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MicrosoftAgentSDKDemo": "Information",
      "MicrosoftAgentSDKDemo.Services.TelemetryService": "Warning",
      "Microsoft": "Warning"
    }
  },
  "ApplicationInsights": {
    "InstrumentationKey": "your-instrumentation-key"
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "DeploymentName": "gpt-4o",
    "ApiVersion": "2025-01-01-preview",
    "SystemInstructions": "You are a helpful agent that answers questions about Azure. Respond with factual information, and do not deviate. If asked anything else, just say you do not know."
  },
  "CosmosDB": {
    "Endpoint": "https://your-resource.documents.azure.com:443/",
    "AccountKey": "your-account-key",
    "DatabaseName": "agent-database",
    "ContainerId": "conversations"
  }
}
```

Replace:
- `your-instrumentation-key` - from Application Insights
- `your-resource.openai.azure.com` - your OpenAI endpoint
- `your-resource.documents.azure.com` - your Cosmos DB endpoint
- `your-account-key` - Cosmos DB account key

### 5. Run the Application

```powershell
cd src
dotnet run

# You'll be prompted for username
# Select a thread or create a new one
```

## CLI Usage

Once the app is running:

```
Enter your username: Alice

Select a thread:
  1. [NEW] - Start a new conversation
  2. What is Azure?
  3. [QUIT] - Exit the application

Enter thread number: 2

Loaded thread: What is Azure?
--- Conversation History ---
Alice: What is Azure?
Agent: Azure is a cloud computing platform...
--- End of History ---

Alice [What is Azure?]> Tell me about managed identities
(Agent responds)

Alice [What is Azure?]> /quit
(Returns to thread selection)
```

## System Instructions

Customize agent behavior by editing the `SystemInstructions` field in appsettings.json:

```json
"SystemInstructions": "You are a helpful agent that answers questions about Azure. Respond with factual information, and do not deviate. If asked anything else, just say you do not know."
```

## Telemetry

### What Gets Logged
- User messages
- Agent responses with token counts
- Thread creation and switching
- Session start/end with duration
- API latency
- Errors and exceptions

### View Telemetry

In **Azure Portal**:
1. Go to Application Insights resource
2. **Logs** tab → Run KQL queries
3. Check **customEvents** table

Example queries:
```kusto
// All user messages
customEvents
| where name == "MessageSent"

// Agent response metrics
customEvents
| where name == "AgentResponse"
| project timestamp, completionTokens = customMetrics.CompletionTokens, latency = customMetrics.LatencyMs

// Session statistics
customEvents
| where name == "SessionEnd"
| summarize count() by tostring(customDimensions.UserId)
```

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

### Issue: "The input content is invalid because the required properties - 'id' - are missing"
**Solution**: This indicates JSON serialization mismatch. Ensure models use `Newtonsoft.Json` attributes (not `System.Text.Json`).

### Issue: "Endpoint not configured"
**Solution**: Verify appsettings.json has AzureOpenAI:Endpoint with your actual Azure endpoint URL

### Issue: "CosmosDB connection failed"
**Solution**: 
- Verify Cosmos DB endpoint and account key
- Check if Cosmos DB account is accessible from your network
- Ensure container exists with correct partition key (/userId)
- Verify RBAC role assignment

### Issue: "Authentication failed" on Azure services
**Solution**:
- Run `az login` to authenticate with Azure
- Verify your account has required roles:
  - `Cognitive Services OpenAI User` for Azure OpenAI
  - `Cosmos DB Built-in Data Contributor` for Cosmos DB
- Check if using the correct subscription: `az account show`

### Issue: "No threads found" on first run
**Solution**: This is normal. Select option 1 to create your first thread.

## File Structure

```
src/
├── Program.cs                    # Entry point, DI setup, CLI loop
├── appsettings.json             # Configuration with Azure credentials
├── MicrosoftAgentSDKDemo.csproj  # Project file with NuGet packages
├── Models/
│   └── ThreadDocument.cs        # Thread with embedded messages
└── Services/
    ├── ThreadManager.cs          # Cosmos DB operations
    ├── TelemetryService.cs       # Application Insights events
    └── ChatAgent.cs              # Azure OpenAI orchestration
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

## Next Steps

1. **Customize system instructions** for your use case
2. **Add conversation summarization** for long chats
3. **Implement multi-agent routing** for complex tasks
4. **Add RAG capabilities** with Azure AI Search
5. **Build web UI** with Blazor or React
6. **Deploy to Azure** as App Service or Container Instance
7. **Add CI/CD pipeline** with GitHub Actions

## Support and Resources

- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure Cosmos DB Documentation](https://learn.microsoft.com/en-us/azure/cosmos-db/)
- [Application Insights](https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview)
- [Azure CLI Documentation](https://learn.microsoft.com/en-us/cli/azure/)

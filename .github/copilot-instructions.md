# Microsoft Agent SDK Demo - Copilot Instructions

## Project Overview

This is a console-based AI agent application built with the Microsoft Agent Framework. It demonstrates:
- Multi-user conversational AI with persistent thread management
- Integration with Azure OpenAI (GPT-4)
- Model Context Protocol (MCP) integration with Microsoft Learn documentation
- Cosmos DB storage for conversation history and thread metadata
- Rich terminal UI using Spectre.Console

## Getting Started

### Prerequisites

1. **.NET 8.0 SDK** - Install from https://dotnet.microsoft.com/download
2. **Azure CLI** - Install from https://docs.microsoft.com/cli/azure/install-azure-cli
3. **Azure Subscription** with the following resources:
   - Azure OpenAI Service with GPT-4o deployment
   - Azure Cosmos DB account
   - (Optional) Application Insights for telemetry

### Initial Setup

1. **Clone the repository** and navigate to the project directory

2. **Configure Azure CLI Authentication**
   ```bash
   az login
   ```
   Ensure you have access to the Azure OpenAI resource

3. **Create appsettings.json**
   ```bash
   cd src
   cp appsettings.json.sample appsettings.json
   ```

4. **Update appsettings.json** with your Azure resource details:
   - `AzureOpenAI:Endpoint` - Your Azure OpenAI endpoint URL
   - `AzureOpenAI:DeploymentName` - Your GPT-4o deployment name
   - `CosmosDB:Endpoint` - Your Cosmos DB account endpoint
   - `CosmosDB:AccountKey` - Your Cosmos DB account key
   - `ApplicationInsights:InstrumentationKey` - (Optional) Your Application Insights key

5. **Create Cosmos DB Database and Container**
   
   The application requires a Cosmos DB container with these settings:
   - Database Name: `agent-database` (or as configured in appsettings.json)
   - Container Name: `conversations`
   - Partition Key: `/id`
   
   You can create this via Azure Portal, Azure CLI, or the first run will create it automatically.

6. **Verify Azure RBAC Permissions**
   
   Ensure your Azure CLI login has the following roles on the Azure OpenAI resource:
   - `Cognitive Services OpenAI User` or
   - `Cognitive Services OpenAI Contributor`

### Build and Run

```bash
# Build the project
dotnet build

# Run the application
dotnet run
```

### First Run Experience

1. Enter a username when prompted
2. Select "Start a new conversation"
3. Ask a question about Azure (e.g., "what is azure sql")
4. The agent will use MCP tools to search Microsoft Learn documentation
5. Type `quit` to return to thread selection
6. Type `quit` again at thread selection to logout

## Architecture

### Core Framework
- **Microsoft.Agents.AI** (v1.0.0-preview.260121.1) - Core agent framework
- **Microsoft.Agents.AI.Hosting** (v1.0.0-preview.260121.1) - AIHostAgent wrapper for automatic persistence
- **Microsoft.Agents.Storage.CosmosDb** (v1.3.176) - IStorage implementation
- **Azure.AI.OpenAI** (v2.1.0) - OpenAI client with AzureCliCredential authentication
- **ModelContextProtocol.Core** (v0.2.0-preview.3) - MCP SDK for external tool integration
- **Spectre.Console** (v0.54.0) - Rich terminal UI

### Key Components

#### 1. Program.cs
**Purpose**: Application entry point with main orchestration loop

**Key Features**:
- Nested loop structure: outer loop for multi-user sessions, inner loop for user threads
- User authentication with username prompt
- Thread selection menu (new conversation, existing threads, logout)
- Chat loop with "quit" to return to thread menu
- Status spinners during agent thinking ("🤔 Agent is thinking...")

**Important Pattern**:
```csharp
// Thread save happens AFTER first message to ensure chat history key exists
await agent.RunAsync(firstMessage, thread);
await threadStore.SaveThreadAsync(agent, threadId, thread); // Save after, not before
```

#### 2. ChatAgentFactory (Services/ChatAgent.cs)
**Purpose**: Creates configured AI agents with MCP tools and chat persistence

**Key Responsibilities**:
- Configures Azure OpenAI client with AzureCliCredential
- Retrieves MCP tools from Microsoft Learn endpoint
- Sets up ChatMessageStoreFactory for automatic conversation persistence
- Creates per-user named agents

**Critical Configuration**:
- System instructions from appsettings.json
- Deployment name (default: "gpt-4o")
- Tools added via ChatOptions.Tools, NOT directly on ChatClientAgentOptions
- ChatMessageStoreFactory receives userId for data isolation

#### 3. CosmosDbAgentThreadStore (Services/CosmosDbAgentThreadStore.cs)
**Purpose**: Custom AgentThreadStore implementation using IStorage for thread metadata

**Storage Pattern**:
```json
{
  "id": "{userId}:{threadId}",
  "userId": "{userId}",
  "threadData": {
    "storeState": "chat-history-{guid}"
  }
}
```

**Key Features**:
- `SetCurrentUserId()` must be called before save/load operations for data isolation
- Thread keys: `{userId}:{threadId}` format for partition isolation
- Index keys: `thread-index:{userId}` format
- Stores ThreadMetadata with title (first message) and createdAt timestamp
- `LastChatHistoryKey` property exposes chat history key from threadData.storeState
- Handles both JsonElement and Dictionary<string, object> formats from CosmosDbPartitionedStorage
- Unwraps nested "document" property that CosmosDbPartitionedStorage adds

**Important Methods**:
- `GetUserThreadsAsync()`: Returns Dictionary<threadId, title> for UI display
- `AddThreadToUserIndexAsync()`: Stores thread with title for better UX
- `GetThreadAsync()`: Loads thread and extracts chat history key to LastChatHistoryKey

#### 4. CosmosDbChatMessageStore (Services/CosmosDbChatMessageStore.cs)
**Purpose**: Custom ChatMessageStore for persisting conversation messages

**Storage Pattern**:
```json
{
  "id": "chat-history-{guid}",
  "userId": "{userId}",
  "messages": [...],
  "lastUpdated": "2026-01-23T..."
}
```

**Key Features**:
- ThreadDbKey generated on first use: `chat-history-{guid}`
- Messages serialized with JsonSerializer.SerializeToElement using custom JsonSerializerOptions
- Serialization options: `DefaultIgnoreCondition.WhenWritingNull` to preserve Contents arrays
- InvokingAsync retrieves existing messages at conversation start
- InvokedAsync appends new messages (RequestMessages + AIContextProviderMessages + ResponseMessages)
- Public `GetMessagesAsync()` method for history display
- Handles nested "document" wrapper from CosmosDbPartitionedStorage

**Critical Pattern**:
```csharp
// Must serialize with proper options to preserve message structure
var messageElement = JsonSerializer.SerializeToElement(message, s_jsonOptions);
```

#### 5. MCPServerManager (Services/MCPServerManager.cs)
**Purpose**: Manages MCP server connections and tool retrieval

**Configuration**:
- Endpoint: https://learn.microsoft.com/api/mcp
- Transport: SseClientTransport
- Returns 3 tools: microsoft_docs_search, microsoft_code_sample_search, microsoft_docs_fetch

**Pattern**:
```csharp
var client = await McpClientFactory.CreateAsync(transport, cancellationToken);
var tools = await client.ListToolsAsync(cancellationToken);
```

#### 6. ConsoleUI (Services/ConsoleUI.cs)
**Purpose**: Rich terminal UI using Spectre.Console

**Key Features**:
- ASCII art banner on startup (FigletText)
- Interactive selection prompts with arrow key navigation
- Color-coded output (cyan for user, blue for agent)
- Agent responses in bordered panels
- Loading spinners during agent processing
- Conversation history display with formatted messages

**Display Methods**:
- `GetUsernameAsync()`: Validated text input
- `GetThreadSelectionAsync()`: Arrow-key selection menu
- `DisplayConversationHistory()`: Shows full chat with user's name
- `DisplayAgentResponse()`: Bordered panel with agent response

## Storage Architecture

### Cosmos DB Configuration
- **Container**: "conversations"
- **Partition Key**: `/id` (framework default, cannot be changed)
- **Database**: "agent-database"

### Document Types

1. **Thread Documents**
   - ID: `{userId}:{threadId}`
   - Contains: threadData with storeState (chat history key)
   - Partition: By document ID

2. **Thread Index Documents**
   - ID: `thread-index:{userId}`
   - Contains: Array of ThreadMetadata objects
   - Format: `{ threads: [{ threadId, title, createdAt }] }`
   - Supports backward compatibility with old format (threadIds array)

3. **Chat History Documents**
   - ID: `chat-history-{guid}`
   - Contains: Array of ChatMessage objects
   - Each message has: Role, Text, Contents array

### Data Isolation
- Thread keys include userId prefix: `{userId}:{threadId}`
- Index keys include userId prefix: `thread-index:{userId}`
- Chat history includes userId field for analytics
- Partition key is document ID, so userId in key ensures isolation

## Important Patterns & Conventions

### 1. Thread Lifecycle
```csharp
// Creating new thread
var thread = await agent.GetNewThreadAsync();
var threadId = Guid.NewGuid().ToString();

// Add to index BEFORE first message (for immediate visibility)
await threadStore.AddThreadToUserIndexAsync(username, threadId, firstMessage);

// Send message
await agent.RunAsync(message, thread);

// Save AFTER message (so chat history key exists)
await threadStore.SaveThreadAsync(agent, threadId, thread);
```

### 2. Loading Existing Thread
```csharp
// Set user context
threadStore.SetCurrentUserId(username);

// Load thread (populates LastChatHistoryKey)
var thread = await threadStore.GetThreadAsync(agent, threadId);

// Get chat history for display
var chatHistoryKey = threadStore.LastChatHistoryKey;
if (!string.IsNullOrEmpty(chatHistoryKey))
{
    var messages = await messageStore.GetMessagesAsync(chatHistoryKey);
    consoleUI.DisplayConversationHistory(messages, username);
}
```

### 3. CosmosDbPartitionedStorage Handling
All documents from CosmosDbPartitionedStorage have nested structure:
```json
{
  "id": "actual-id",
  "realId": "actual-id",
  "document": {
    // Your actual document here
  },
  "partitionKey": "actual-id"
}
```

**Always unwrap the "document" property** when reading:
```csharp
if (docElement.TryGetProperty("document", out var nestedDoc))
{
    docElement = nestedDoc;
}
```

### 4. Message Serialization
Messages must be serialized with proper options to preserve structure:
```csharp
private static readonly JsonSerializerOptions s_jsonOptions = new()
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var messageElement = JsonSerializer.SerializeToElement(message, s_jsonOptions);
```

### 5. Authentication
- Uses AzureCliCredential for Azure OpenAI access
- Run `az login` before starting the application
- Requires appropriate RBAC roles on Azure OpenAI resource

## Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MicrosoftAgentSDKDemo": "Information",
      "Microsoft": "Warning"
    }
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.cognitiveservices.azure.com",
    "DeploymentName": "gpt-4o",
    "SystemInstructions": "You are a helpful agent..."
  },
  "CosmosDB": {
    "Endpoint": "https://your-cosmos.documents.azure.com:443/",
    "AccountKey": "your-key",
    "DatabaseName": "agent-database",
    "ContainerId": "conversations"
  }
}
```

## Common Tasks

### Adding a New Feature
1. Update interface in ConsoleUI if UI changes needed
2. Implement in ConsoleUI class
3. Update Program.cs orchestration if workflow changes
4. Test with multiple users to ensure data isolation

### Debugging Storage Issues
1. Check logs for "Thread state ValueKind" to understand serialization format
2. Verify userId is set: `threadStore.SetCurrentUserId(username)`
3. Check Cosmos DB documents for nested "document" wrapper
4. Verify chat history key exists in threadData.storeState

### Adding New MCP Tools
1. Update MCPServerManager to connect to new MCP server
2. Tools automatically integrated via ChatOptions.Tools
3. No code changes needed in agent creation

### Modifying Thread Metadata
1. Update ThreadMetadata class in CosmosDbAgentThreadStore
2. Update GetUserThreadsAsync deserialization logic
3. Update AddThreadToUserIndexAsync serialization logic
4. Consider backward compatibility with existing data

## Watch Out For

1. **Thread Save Timing**: Always save thread AFTER sending first message, not before
2. **UserId Context**: Call SetCurrentUserId() before any thread store operations
3. **Nested Documents**: Always unwrap "document" property from CosmosDbPartitionedStorage
4. **Message Serialization**: Use proper JsonSerializerOptions to preserve Contents arrays
5. **Tool Configuration**: Add tools via ChatOptions.Tools, not ChatClientAgentOptions directly
6. **Case Sensitivity**: ThreadMetadata deserialization requires PropertyNameCaseInsensitive
7. **Empty Messages**: Chat history display skips empty messages and tool calls
8. **Logout vs Exit**: "Quit" at thread menu logs out; "quit" in chat returns to thread menu

## Testing Checklist

- [ ] Create new thread and verify it appears in list with title
- [ ] Send multiple messages and verify conversation history persists
- [ ] Logout and login as different user, verify data isolation
- [ ] Load existing thread and verify conversation history displays
- [ ] Send new message in loaded thread and verify it appends correctly
- [ ] Test backward compatibility with old thread format (if applicable)
- [ ] Verify MCP tools are working (search Microsoft docs)
- [ ] Check Cosmos DB documents for correct structure
- [ ] Test with multiple concurrent users (different usernames)

## Performance Considerations

- Thread index is loaded on every thread selection (limit: 10 threads)
- Chat history loaded only when selecting existing thread
- Messages retrieved from single document per thread
- Each user's threads are in separate documents for isolation
- Cosmos DB queries use partition key for efficient lookups

## Future Enhancements

Consider these for future iterations:
- Add thread deletion capability
- Implement thread search/filtering
- Add conversation export functionality
- Support for shared threads (multiple users)
- Streaming responses for real-time feedback
- Token usage tracking and display
- Thread summarization for long conversations
- Custom MCP server integration beyond Microsoft Learn

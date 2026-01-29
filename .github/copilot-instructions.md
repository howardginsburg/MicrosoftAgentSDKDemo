# GitHub Copilot Instructions for Microsoft Agent SDK Demo

## Project Type & Purpose

You are working on a console-based AI agent application built with the Microsoft Agent Framework (preview v1.0.0). This codebase demonstrates multi-user conversational AI with persistent thread management, Azure OpenAI integration (GPT-4o and DALL-E 3), Model Context Protocol (MCP) tool integration, and Cosmos DB storage.

## Critical Rules - Read First

### 1. Project Organization
**ALWAYS** place new files in the correct folder based on their responsibility:
- `Agents/` - Agent creation and configuration (e.g., ChatAgentFactory.cs)
- `Display/` - UI and console rendering (e.g., ConsoleUI.cs, ReasoningChatClient.cs)
- `Storage/` - Data persistence implementations (e.g., CosmosDbAgentThreadStore.cs, CosmosDbChatMessageStore.cs)
- `Integration/` - External service integrations (e.g., MCPServerManager.cs, ImageGenerationService.cs)
- `Models/` - Data transfer objects and domain models (e.g., MCPServerConfiguration.cs)
- `prompts/` - System instructions and prompt templates

**ALWAYS** use the matching namespace for the folder (e.g., `MicrosoftAgentSDKDemo.Agents` for files in `Agents/`).

### 2. Thread Lifecycle - CRITICAL ORDERING
When creating new conversation threads, **ALWAYS** follow this exact sequence:

```csharp
// 1. Create new thread
var thread = await agent.GetNewThreadAsync();
var threadId = Guid.NewGuid().ToString();

// 2. Add to index BEFORE sending message (for UI visibility)
await threadStore.AddThreadToUserIndexAsync(username, threadId, firstMessage);

// 3. Send the first message
await agent.RunAsync(message, thread);

// 4. Save thread AFTER message (chat history key must exist first)
await threadStore.SaveThreadAsync(agent, threadId, thread);
```

**NEVER** save the thread before sending the first message - the chat history key won't exist yet and the save will fail.

### 3. Data Isolation - ALWAYS Required
Before any thread store operations, **ALWAYS** call `SetCurrentUserId(username)`:

```csharp
threadStore.SetCurrentUserId(username);
var thread = await threadStore.GetThreadAsync(agent, threadId);
```

This ensures proper multi-user data isolation in Cosmos DB.

### 4. CosmosDbPartitionedStorage Document Structure
When reading documents from Cosmos DB via CosmosDbPartitionedStorage, **ALWAYS** unwrap the nested "document" property:

```csharp
if (docElement.TryGetProperty("document", out var nestedDoc))
{
    docElement = nestedDoc;
}
```

All documents have this structure: `{ "id": "...", "realId": "...", "document": { /* actual data */ }, "partitionKey": "..." }`

### 5. Message Serialization
When serializing ChatMessage objects for storage, **ALWAYS** use these JsonSerializerOptions:

```csharp
private static readonly JsonSerializerOptions s_jsonOptions = new()
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var messageElement = JsonSerializer.SerializeToElement(message, s_jsonOptions);
```

This preserves the Contents array structure required by the framework.

## Component-Specific Instructions

### Working with Agents/ Folder

**ChatAgentFactory.cs** - When modifying agent creation:
- System instructions are loaded from `prompts/system-instructions.txt` - **NEVER** hardcode them
- Deployment name defaults to "gpt-4o" from configuration
- **ALWAYS** add MCP tools via `ChatOptions.Tools`, **NEVER** directly on `ChatClientAgentOptions.Tools`
- The agent creation pipeline: AzureOpenAI client → ReasoningChatClient wrapper → AIAgent conversion
- Authentication uses `AzureCliCredential` - user must run `az login` before startup

When adding new tool integrations:
```csharp
var tools = await mcpServerManager.GetToolsAsync();
chatOptions.Tools = tools; // Tools go here, not on ChatClientAgentOptions
```

### Working with Display/ Folder

**ReasoningChatClient.cs** - Middleware for displaying agent reasoning:
- Inherits from `DelegatingChatClient` (Microsoft.Extensions.AI v10.2.0 pattern)
- **ALWAYS** log before calling `AnsiConsole.Write()` to prevent console corruption
- Use `LogDebug` level, not `LogInformation`, to avoid interfering with UI
- Overrides both `GetResponseAsync` and `GetStreamingResponseAsync` for complete coverage
- Display pattern: Show user's question → Display tool invocation table → Return response unchanged

When customizing the reasoning display:
- Modify `DisplayToolCalls()` method for different table formatting
- Use Spectre.Console components: `Table`, `Panel`, `Markup`, etc.
- Truncate long argument values in `FormatArguments()` to keep table readable

**ConsoleUI.cs** - Rich terminal interface:
- **ALWAYS** use Spectre.Console components for consistency: `FigletText`, `SelectionPrompt`, `Panel`, `Spinner`
- Color scheme: Cyan for user input, Blue for agent responses, Green for success, Red for errors
- Image generation: Display file path in panel and launch default viewer - **DO NOT** attempt console rendering
- Thread selection returns special value "NEW_CONVERSATION" for new threads

### Working with Storage/ Folder

**CosmosDbAgentThreadStore.cs** - Thread metadata management:
- Implements custom `AgentThreadStore` using IStorage
- Thread document IDs: `{userId}:{threadId}` format for partition isolation
- Index document IDs: `thread-index:{userId}` format
- `LastChatHistoryKey` property extracts the chat history key from `threadData.storeState`
- **ALWAYS** call `SetCurrentUserId()` before any load/save operations

Key methods:
- `GetUserThreadsAsync()` - Returns `Dictionary<threadId, title>` for UI display
- `AddThreadToUserIndexAsync()` - Stores thread with title from first message
- `GetThreadAsync()` - Loads thread and populates `LastChatHistoryKey` property

**CosmosDbChatMessageStore.cs** - Conversation message persistence:
- Generates unique chat history key: `chat-history-{guid}` on first use
- `InvokingAsync` retrieves existing messages at conversation start
- `InvokedAsync` appends new messages (RequestMessages + AIContextProviderMessages + ResponseMessages)
- Public `GetMessagesAsync()` method for displaying conversation history
- **MUST** use proper serialization options (see rule #5 above)

### Working with Integration/ Folder

**MCPServerManager.cs** - Model Context Protocol integration:
- Supports two transport types: SSE (HTTP) and Stdio (local process)
- **SSE Transport**: Connects to HTTP endpoints like `https://learn.microsoft.com/api/mcp`
- **Stdio Transport**: Launches local processes like `npx -y @azure/mcp@latest server start`
- Uses `SseClientTransport` for SSE servers and `StdioClientTransport` for Stdio servers
- Returns tools from all configured and enabled MCP servers
- Tools are automatically passed to agent via ChatOptions

When adding new MCP servers:
1. Add configuration to `appsettings.json` under `MCPServers.Servers`
2. Set `TransportType` to `Sse` or `Stdio`
3. For SSE: Set `Endpoint` to the server URL
4. For Stdio: Set `Command`, `Arguments`, and optionally `EnvironmentVariables` and `WorkingDirectory`
5. Tools automatically integrate via ChatOptions.Tools - no code changes needed

**ImageGenerationService.cs** - DALL-E 3 integration:
- Saves generated images to `images/` folder with timestamp filenames
- Automatically launches images in default viewer using `Process.Start()`
- Supports size and quality parameters from Azure OpenAI DALL-E 3
- Returns file path for display purposes

**FileAttachmentService.cs** - File attachment processing:
- Processes comma-separated file paths from user input
- Supports text files: .txt, .md, .json, .xml, .csv, .log, .cs, .js, .ts, .py, .java, .cpp, .html, .css, .yaml, .yml, .toml, .ini, .config
- Supports image files: .jpg, .jpeg, .png, .gif, .bmp, .webp
- Enforces 10MB file size limit for all attachments
- Text files are wrapped in markdown code blocks with filename header
- Image files are converted to `DataContent` with proper media type for vision model
- Returns `List<AIContent>` containing TextContent and/or DataContent objects

**MultimodalMessageHelper.cs** - ChatMessage construction:
- Creates properly structured `ChatMessage` objects with multimodal content
- `CreateMultimodalMessage()` combines user text + attachments into single ChatMessage
- Content array structure: [TextContent (user message), ...AIContent (attachments)]
- **CRITICAL**: This enables proper image support via `agent.RunAsync(ChatMessage, thread)`
- `HasImageAttachments()` checks for DataContent to identify vision model usage

When handling file attachments:
```csharp
// Process attachments
var attachmentContents = await fileAttachmentService.ProcessFileAttachmentsAsync(filePaths);

// Create multimodal ChatMessage (not string concatenation!)
var chatMessage = multimodalHelper.CreateMultimodalMessage(userText, attachmentContents);

// Use ChatMessage overload for proper multimodal support
await agent.RunAsync(chatMessage, thread);  // NOT RunAsync(string, thread)
```

## Cosmos DB Storage Patterns

When working with Cosmos DB storage, understand these document types:

**Thread Documents** - Individual conversation threads
- **ALWAYS** use ID format: `{userId}:{threadId}`
- Contains `threadData` with `storeState` pointing to chat history key
- Partitioned by document ID for isolation

**Thread Index Documents** - User's thread list
- **ALWAYS** use ID format: `thread-index:{userId}`
- Contains array of `ThreadMetadata` objects: `{ threads: [{ threadId, title, createdAt }] }`
- Maintain backward compatibility with old format (simple threadIds array)

**Chat History Documents** - Message contents
- **ALWAYS** use ID format: `chat-history-{guid}` (generated automatically)
- Contains array of `ChatMessage` objects with Role, Text, and Contents
- Each message must have properly serialized Contents array (see rule #5)

**Data Isolation Rules**:
- **ALWAYS** prefix thread keys with userId: `{userId}:{threadId}`
- **ALWAYS** prefix index keys with userId: `thread-index:{userId}`
- Include userId field in chat history for analytics
- Partition key is document ID, so userId in key provides automatic isolation

## Required Coding Patterns

### Loading Existing Thread - Follow This Pattern
When the user selects an existing thread to continue:

```csharp
// 1. Set user context FIRST
threadStore.SetCurrentUserId(username);

// 2. Load thread (this populates LastChatHistoryKey)
var thread = await threadStore.GetThreadAsync(agent, threadId);

// 3. Display conversation history if available
var chatHistoryKey = threadStore.LastChatHistoryKey;
if (!string.IsNullOrEmpty(chatHistoryKey))
{
    var messages = await messageStore.GetMessagesAsync(chatHistoryKey);
    consoleUI.DisplayConversationHistory(messages, username);
}
```

### Reading Cosmos DB Documents - ALWAYS Unwrap
CosmosDbPartitionedStorage wraps all documents in a nested structure. **ALWAYS** check for and unwrap the "document" property:

```csharp
if (docElement.TryGetProperty("document", out var nestedDoc))
{
    docElement = nestedDoc; // Work with nestedDoc, not original
}
```

Document structure from storage:
```json
{
  "id": "actual-id",
  "realId": "actual-id",
  "document": { /* YOUR ACTUAL DATA HERE */ },
  "partitionKey": "actual-id"
}
```

## Common Errors to Prevent

**NEVER** make these mistakes:

1. **Thread Save Timing** - Saving thread before first message will fail (chat history key doesn't exist yet)
2. **Missing UserId** - Forgetting `SetCurrentUserId()` will cause wrong user's data to load
3. **Unwrapping Documents** - Forgetting to unwrap "document" property causes deserialization failures
4. **Wrong Serialization** - Not using proper JsonSerializerOptions corrupts message Contents arrays
5. **Tool Location** - Adding tools to ChatClientAgentOptions instead of ChatOptions breaks tool integration
6. **Case Sensitivity** - ThreadMetadata requires `PropertyNameCaseInsensitive = true` for deserialization
7. **Logging Timing** - Logging with `LogInformation` during UI rendering corrupts Spectre.Console output (use LogDebug instead)
8. **UI Terminology** - "Quit" at thread menu logs out; `/quit` in chat returns to thread menu
9. **Empty Messages** - Chat history display must skip empty messages and tool calls to avoid display issues
10. **Wrong RunAsync Overload** - Using `RunAsync(string)` doesn't support images; use `RunAsync(ChatMessage)` with content array for multimodal support

## When User Requests Changes

### Adding New Features
**ALWAYS** follow this checklist:
1. Determine which folder the new code belongs in (Agents/, Display/, Storage/, Integration/, Models/)
2. Create new class with matching namespace (e.g., `MicrosoftAgentSDKDemo.Display` for Display/ folder)
3. Update dependency injection in Program.cs if the class needs to be injected
4. Test with multiple usernames to verify data isolation works correctly

### Debugging Storage Issues
When user reports storage problems:
1. Check logs for "Thread state ValueKind" to understand serialization format
2. Verify code calls `threadStore.SetCurrentUserId(username)` before operations
3. Check if code unwraps "document" property from CosmosDbPartitionedStorage
4. Verify chat history key exists in threadData.storeState before attempting to load

### Adding New MCP Tools
When user wants new tool integrations:
1. Add server configuration to `appsettings.json` under `MCPServers.Servers`
2. For SSE servers: Set `TransportType: "Sse"` and `Endpoint` URL
3. For Stdio servers: Set `TransportType: "Stdio"`, `Command`, and `Arguments`
4. Tools automatically integrate via ChatOptions.Tools - no code changes needed
5. ReasoningChatClient will automatically display new tool invocations in table

Example Stdio configuration for Azure MCP:
```json
{
  "Name": "Azure MCP",
  "TransportType": "Stdio",
  "Command": "npx",
  "Arguments": ["-y", "@azure/mcp@latest", "server", "start"],
  "Enabled": true,
  "TimeoutSeconds": 60,
  "EnvironmentVariables": {},
  "WorkingDirectory": null
}
```

### Customizing UI Display
When user wants to change how information is displayed:
1. Edit ReasoningChatClient in Display/ folder for reasoning/tool display changes
2. Modify DisplayToolCalls() method to change table formatting
3. Adjust FormatArguments() to change how tool arguments are truncated/displayed
4. Edit ConsoleUI.cs for other UI changes (menus, prompts, panels, colors)
5. Use Spectre.Console components for consistency

### Modifying Thread Storage
When user needs changes to thread metadata:
1. Update ThreadMetadata class in Storage/CosmosDbAgentThreadStore.cs
2. Update GetUserThreadsAsync deserialization to handle new properties
3. Update AddThreadToUserIndexAsync serialization to save new properties
4. **ALWAYS** consider backward compatibility with existing Cosmos DB documents

## After Making Changes - ALWAYS Verify

When you make code changes, **ALWAYS** verify:

1. ✅ **Builds Successfully** - Run `dotnet build` to ensure no compilation errors
2. ✅ **Correct Folder** - New files are in appropriate folder with matching namespace
3. ✅ **Thread Lifecycle** - Thread creation follows the 4-step sequence (create → index → message → save)
4. ✅ **Data Isolation** - Code calls `SetCurrentUserId()` before thread operations
5. ✅ **Document Unwrapping** - Code unwraps "document" property when reading from Cosmos DB
6. ✅ **Message Serialization** - ChatMessage serialization uses proper JsonSerializerOptions
7. ✅ **Tool Configuration** - MCP tools added to ChatOptions.Tools, not ChatClientAgentOptions
8. ✅ **Logging Placement** - Logs happen BEFORE Spectre.Console rendering, use LogDebug not LogInformation
9. ✅ **UI Consistency** - Display code uses Spectre.Console components and established color scheme

## Framework and SDK Information

**Do NOT** suggest upgrades or changes to these core dependencies:
- Microsoft.Agents.AI v1.0.0-preview.260121.1 (Core agent framework)
- Microsoft.Agents.AI.Hosting v1.0.0-preview.260121.1 (AIHostAgent wrapper)
- Microsoft.Agents.Storage.CosmosDb v1.3.176 (IStorage implementation)
- Azure.AI.OpenAI v2.1.0 (OpenAI client)
- ModelContextProtocol.Core v0.2.0-preview.3 (MCP SDK)
- Spectre.Console v0.54.0 (Terminal UI)

**Authentication**: Uses AzureCliCredential - user must run `az login` before starting application
**Azure Resources Required**: Azure OpenAI Service (GPT-4o + DALL-E 3), Cosmos DB with "conversations" container
**Partition Key**: Fixed to `/id` by framework, cannot be changed

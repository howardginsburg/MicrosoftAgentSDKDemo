# Microsoft Learn MCP Integration

## Overview

The Microsoft Agent Framework SDK Demo has been enhanced with Microsoft Learn documentation tools integration through an MCP (Model Context Protocol) server manager pattern. This allows the agent to access Microsoft Learn documentation tools for searching and retrieving documentation.

## Architecture

### New Components

#### 1. **MCPServerManager.cs** (`src/Services/MCPServerManager.cs`)
- **Purpose**: Provides Microsoft Learn documentation tools for agent use
- **Interface**: `IMCPServerManager`
- **Responsibility**: Creates and manages tool definitions for agent access

#### 2. **ToolDefinition Class**
- **Purpose**: Represents a tool that an agent can use
- **Properties**:
  - `Name`: The tool name (e.g., "SearchMicrosoftLearn")
  - `Description`: Human-readable description of what the tool does
  - `Function`: The delegate/function to execute when the tool is called

### Available Tools

The MCPServerManager exposes three Microsoft Learn documentation tools:

1. **SearchMicrosoftLearn(query: string) → string**
   - Searches Microsoft Learn documentation for articles related to a topic
   - Input: Search query or topic
   - Output: Search results or placeholder message

2. **GetDocumentation(topic: string, filter?: string) → string**
   - Retrieves detailed documentation from Microsoft Learn
   - Input: Documentation topic/URL path, optional filter
   - Output: Documentation content or placeholder message

3. **SearchAzureDocumentation(service: string, category?: string) → string**
   - Searches Azure-specific documentation on Microsoft Learn
   - Input: Azure service name, optional category (tutorial, reference, how-to)
   - Output: Azure documentation results or placeholder message

## Integration Points

### ChatAgentFactory
- Accepts `IMCPServerManager` via dependency injection
- Calls `GetMicrosoftLearnToolsAsync()` during agent creation
- Passes tool definitions to the AzureOpenAIAgent
- Implements graceful degradation if tool loading fails

### AzureOpenAIAgent
- Stores available tools in `_learnTools` field
- Logs available tools when processing messages
- Maintains separation of concerns (tools defined separately)

## Data Flow

```
ChatAgentFactory.CreateAgentAsync()
    ↓
MCPServerManager.GetMicrosoftLearnToolsAsync()
    ↓
Returns List<ToolDefinition>
    ↓
AzureOpenAIAgent stores tools
    ↓
ProcessMessageAsync logs available tools
```

## Current Implementation Status

### ✅ Completed
- MCPServerManager service created with 3 documentation tools
- Tool definitions properly structured with names and descriptions
- Integration with ChatAgentFactory
- Dependency injection setup in Program.cs
- Logging for tool availability
- Build compiles successfully

### 📋 Future Enhancements

1. **Real API Integration**: Replace placeholder implementations with actual calls to learn.microsoft.com APIs
2. **Tool Invocation**: Implement actual tool calling in the Azure OpenAI agent when the model requests it
3. **Response Handling**: Process tool responses and feed them back to the model for context
4. **Caching**: Implement caching for frequently requested documentation
5. **Error Handling**: Enhanced error handling for API failures
6. **Rate Limiting**: Implement rate limiting for API calls

## Configuration

The MCPServerManager is registered as a singleton in the DI container:

```csharp
services.AddSingleton<IMCPServerManager, MCPServerManager>();
```

## Usage Example

The tools are available to agents during message processing:

```csharp
var agent = await agentFactory.CreateAgentAsync(userId);
// Agent now has access to Microsoft Learn tools
var response = await agent.ProcessMessageAsync(threadKey, userMessage);
```

## Implementation Notes

- Tool definitions use `System.ComponentModel.Description` attributes for documentation
- Tools are currently placeholder implementations (safe for testing)
- The framework is extensible for adding more documentation sources
- Graceful error handling allows agent operation even if tools fail to load

## Technical Stack

- **Framework**: Microsoft Agent Framework 1.0.0-preview
- **Service Pattern**: Dependency injection with singleton lifetime
- **Language**: C# 12.0 (.NET 8.0)
- **Tool Format**: ToolDefinition with delegates

## Next Steps

To enable actual tool invocation in the agent:

1. Implement tool calling logic in `AzureOpenAIAgent.ProcessMessageAsync()`
2. Add tool response handling to incorporate results into the conversation
3. Connect to actual learn.microsoft.com API endpoints
4. Test end-to-end tool usage in multi-turn conversations

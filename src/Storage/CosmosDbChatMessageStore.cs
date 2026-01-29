using Microsoft.Agents.AI;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicrosoftAgentSDKDemo.Storage;

/// <summary>
/// Chat message store implementation that persists messages to Cosmos DB using IStorage.
/// </summary>
internal sealed class CosmosDbChatMessageStore : ChatMessageStore
{
    private readonly IStorage _storage;
    private readonly ILogger<CosmosDbChatMessageStore> _logger;
    private readonly string _userId;
    
    // JsonSerializerOptions configured for proper ChatMessage serialization
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The unique key used to store/retrieve messages for this thread in Cosmos DB.
    /// </summary>
    public string? ThreadDbKey { get; private set; }

    public CosmosDbChatMessageStore(
        IStorage storage,
        string userId,
        JsonElement serializedStoreState,
        ILogger<CosmosDbChatMessageStore> logger,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _userId = userId ?? throw new ArgumentNullException(nameof(userId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Deserialize the thread DB key if we're restoring from a saved state
        if (serializedStoreState.ValueKind is JsonValueKind.String)
        {
            ThreadDbKey = serializedStoreState.Deserialize<string>(jsonSerializerOptions);
            _logger.LogDebug("Restored ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
        }
    }

    /// <summary>
    /// Called at the start of agent invocation to retrieve messages from storage.
    /// Returns messages in ascending chronological order (oldest first).
    /// </summary>
    public override async ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (ThreadDbKey is null)
        {
            // No thread key yet, so no messages to retrieve
            _logger.LogDebug("No ThreadDbKey yet, returning empty message list");
            return [];
        }

        try
        {
            _logger.LogDebug("Retrieving messages | ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
            
            // Read the chat history document from Cosmos DB
            var chatHistoryDocument = await _storage.ReadAsync([ThreadDbKey], cancellationToken);

            if (chatHistoryDocument.TryGetValue(ThreadDbKey, out var doc))
            {
                _logger.LogDebug("Found chat history document | DocType: {DocType}", doc?.GetType().Name ?? "null");
                
                var docElement = CosmosDbDocumentHelper.ToUnwrappedJsonElement(doc);
                if (docElement == null)
                {
                    _logger.LogWarning("Chat history document is unexpected type: {Type}", doc?.GetType().FullName ?? "null");
                    return [];
                }
                
                if (docElement.Value.ValueKind == JsonValueKind.Object && docElement.Value.TryGetProperty("messages", out var messagesElement))
                {
                    var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesElement, s_jsonOptions);
                    _logger.LogDebug("Retrieved {Count} messages from chat history | ThreadDbKey: {ThreadDbKey}", messages?.Count ?? 0, ThreadDbKey);
                    return messages ?? [];
                }
                else
                {
                    _logger.LogWarning("Chat history document missing 'messages' property | ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
                }
            }
            else
            {
                _logger.LogDebug("No chat history document found | ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
            }

            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
            return [];
        }
    }

    /// <summary>
    /// Called at the end of agent invocation to add new messages to storage.
    /// </summary>
    public override async ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        // Don't store messages if the request failed
        if (context.InvokeException is not null)
        {
            _logger.LogWarning("Invocation failed, not storing messages");
            return;
        }

        // Generate a thread key on first use
        if (ThreadDbKey is null)
        {
            ThreadDbKey = $"chat-history-{Guid.NewGuid():N}";
            _logger.LogDebug("Generated new ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
        }

        try
        {
            _logger.LogDebug("Storing messages | ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
            
            // Retrieve existing messages
            var existingMessages = new List<ChatMessage>();
            var chatHistoryDocument = await _storage.ReadAsync([ThreadDbKey], cancellationToken);

            if (chatHistoryDocument.TryGetValue(ThreadDbKey, out var doc))
            {
                _logger.LogDebug("Found existing chat history document");
                
                var docElement = CosmosDbDocumentHelper.ToUnwrappedJsonElement(doc);
                if (docElement == null)
                {
                    _logger.LogWarning("Existing chat history document is unexpected type: {Type}", doc?.GetType().FullName ?? "null");
                }
                else if (docElement.Value.ValueKind == JsonValueKind.Object && docElement.Value.TryGetProperty("messages", out var messagesElement))
                {
                    existingMessages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesElement, s_jsonOptions) ?? new List<ChatMessage>();
                    _logger.LogDebug("Loaded {Count} existing messages", existingMessages.Count);
                }
            }
            else
            {
                _logger.LogDebug("No existing chat history found, creating new document");
            }

            // Add new messages from this invocation
            var allNewMessages = context.RequestMessages
                .Concat(context.AIContextProviderMessages ?? [])
                .Concat(context.ResponseMessages ?? []);

            var newMessageCount = allNewMessages.Count();
            
            // Log details about the messages being stored
            _logger.LogDebug("Messages to store: RequestMessages={RequestCount}, AIContextProviderMessages={ContextCount}, ResponseMessages={ResponseCount}",
                context.RequestMessages.Count(), 
                context.AIContextProviderMessages?.Count() ?? 0,
                context.ResponseMessages?.Count() ?? 0);
            
            foreach (var msg in allNewMessages)
            {
                var contentCount = msg.Contents?.Count ?? 0;
                var textContent = msg.Text ?? "(no text)";
                _logger.LogDebug("Message: Role={Role}, Contents={ContentCount}, Text={Text}", 
                    msg.Role, contentCount, textContent.Length > 50 ? textContent.Substring(0, 50) + "..." : textContent);
            }
            
            existingMessages.AddRange(allNewMessages);

            // Store back to Cosmos DB with proper JSON serialization
            var documentToStore = new Dictionary<string, object>
            {
                ["id"] = ThreadDbKey,
                ["userId"] = _userId,
                ["messages"] = JsonSerializer.SerializeToElement(existingMessages, s_jsonOptions),
                ["lastUpdated"] = DateTimeOffset.UtcNow
            };

            var batch = new Dictionary<string, object>
            {
                [ThreadDbKey] = documentToStore
            };

            await _storage.WriteAsync(batch, cancellationToken);
            _logger.LogDebug("Stored chat history | ThreadDbKey: {ThreadDbKey} | NewMessages: {NewMessages} | TotalMessages: {TotalMessages}", 
                ThreadDbKey, newMessageCount, existingMessages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing messages for ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
        }
    }

    /// <summary>
    /// Serialize the store state (the thread DB key) so it can be persisted.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        _logger.LogDebug("Serializing ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
        return JsonSerializer.SerializeToElement(ThreadDbKey, jsonSerializerOptions);
    }

    /// <summary>
    /// Public method to retrieve messages for display purposes.
    /// </summary>
    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string threadDbKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var chatHistoryDocument = await _storage.ReadAsync([threadDbKey], cancellationToken);

            if (chatHistoryDocument.TryGetValue(threadDbKey, out var doc))
            {
                var docElement = CosmosDbDocumentHelper.ToUnwrappedJsonElement(doc);
                if (docElement == null)
                {
                    return [];
                }

                if (docElement.Value.TryGetProperty("messages", out var messagesElement))
                {
                    var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesElement.GetRawText(), s_jsonOptions) ?? new List<ChatMessage>();
                    return messages;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for ThreadDbKey: {ThreadDbKey}", threadDbKey);
        }

        return [];
    }
}

using Microsoft.Agents.AI;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.CosmosDb;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Chat message store implementation that persists messages to Cosmos DB using IStorage.
/// </summary>
internal sealed class CosmosDbChatMessageStore : ChatMessageStore
{
    private readonly IStorage _storage;
    private readonly ILogger<CosmosDbChatMessageStore> _logger;

    /// <summary>
    /// The unique key used to store/retrieve messages for this thread in Cosmos DB.
    /// </summary>
    public string? ThreadDbKey { get; private set; }

    public CosmosDbChatMessageStore(
        IStorage storage,
        JsonElement serializedStoreState,
        ILogger<CosmosDbChatMessageStore> logger,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
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
            // Read the chat history document from Cosmos DB
            var chatHistoryDocument = await _storage.ReadAsync([ThreadDbKey], cancellationToken);

            if (chatHistoryDocument.TryGetValue(ThreadDbKey, out var doc) && doc is Dictionary<string, object> docDict)
            {
                if (docDict.TryGetValue("messages", out var messagesObj) && messagesObj is JsonElement messagesElement)
                {
                    var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesElement);
                    _logger.LogDebug("Retrieved {Count} messages for ThreadDbKey: {ThreadDbKey}", messages?.Count ?? 0, ThreadDbKey);
                    return messages ?? [];
                }
            }

            _logger.LogDebug("No messages found for ThreadDbKey: {ThreadDbKey}", ThreadDbKey);
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
        ThreadDbKey ??= $"chat-history-{Guid.NewGuid():N}";

        try
        {
            // Retrieve existing messages
            var existingMessages = new List<ChatMessage>();
            var chatHistoryDocument = await _storage.ReadAsync([ThreadDbKey], cancellationToken);

            if (chatHistoryDocument.TryGetValue(ThreadDbKey, out var doc) && doc is Dictionary<string, object> docDict)
            {
                if (docDict.TryGetValue("messages", out var messagesObj) && messagesObj is JsonElement messagesElement)
                {
                    existingMessages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesElement) ?? new List<ChatMessage>();
                }
            }

            // Add new messages from this invocation
            var allNewMessages = context.RequestMessages
                .Concat(context.AIContextProviderMessages ?? [])
                .Concat(context.ResponseMessages ?? []);

            existingMessages.AddRange(allNewMessages);

            // Store back to Cosmos DB
            var documentToStore = new Dictionary<string, object>
            {
                ["id"] = ThreadDbKey,
                ["messages"] = existingMessages,
                ["lastUpdated"] = DateTimeOffset.UtcNow
            };

            var batch = new Dictionary<string, object>
            {
                [ThreadDbKey] = documentToStore
            };

            await _storage.WriteAsync(batch, cancellationToken);
            _logger.LogDebug("Stored {Count} total messages for ThreadDbKey: {ThreadDbKey}", existingMessages.Count, ThreadDbKey);
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
}

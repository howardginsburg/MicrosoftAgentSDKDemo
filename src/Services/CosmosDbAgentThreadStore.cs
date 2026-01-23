using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Services;

public class ThreadMetadata
{
    public string ThreadId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CosmosDbAgentThreadStore : AgentThreadStore
{
    private readonly IStorage _storage;
    private readonly ILogger<CosmosDbAgentThreadStore> _logger;
    private string? _currentUserId;

    public CosmosDbAgentThreadStore(
        IStorage storage,
        ILogger<CosmosDbAgentThreadStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public void SetCurrentUserId(string userId) => _currentUserId = userId;

    public override async ValueTask SaveThreadAsync(
        AIAgent agent,
        string threadId,
        AgentThread thread,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentUserId))
                throw new InvalidOperationException("UserId must be set before saving thread. Call SetCurrentUserId first.");
                
            var json = thread.Serialize();
            var key = GetThreadKey(threadId);
            
            // Create a wrapper object with id and userId for Cosmos DB
            var document = new Dictionary<string, object>
            {
                { "id", key },
                { "userId", _currentUserId },
                { "threadData", json }
            };
            
            await _storage.WriteAsync(new Dictionary<string, object>
            {
                { key, document }
            }, cancellationToken);

            _logger.LogDebug("Thread saved | ThreadId: {ThreadId}", threadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save thread | ThreadId: {ThreadId}", threadId);
            throw;
        }
    }

    public override async ValueTask<AgentThread> GetThreadAsync(
        AIAgent agent,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetThreadKey(threadId);
            var items = await _storage.ReadAsync(new[] { key }, cancellationToken);

            if (items != null && items.TryGetValue(key, out var value))
            {
                // Try to handle both JsonElement and Dictionary<string, object> formats
                JsonElement docElement;
                
                if (value is JsonElement jsonElem)
                {
                    docElement = jsonElem;
                }
                else if (value is Dictionary<string, object> dict)
                {
                    // Convert Dictionary to JsonElement
                    docElement = JsonSerializer.SerializeToElement(dict);
                }
                else
                {
                    _logger.LogError("Thread document is unexpected type: {Type} | ThreadId: {ThreadId}", 
                        value?.GetType().FullName ?? "null", threadId);
                    throw new InvalidOperationException($"Thread {threadId} has invalid format (unexpected type)");
                }
                
                // Extract threadData from the wrapper document
                if (docElement.ValueKind == JsonValueKind.Object)
                {
                    if (docElement.TryGetProperty("threadData", out var threadDataElement))
                    {
                        var thread = await agent.DeserializeThreadAsync(threadDataElement, cancellationToken: cancellationToken);
                        _logger.LogDebug("Thread loaded | ThreadId: {ThreadId}", threadId);
                        return thread;
                    }
                }
                
                _logger.LogError("Thread document missing 'threadData' property or not an object | ThreadId: {ThreadId} | Kind: {Kind}", 
                    threadId, docElement.ValueKind);
                throw new InvalidOperationException($"Thread {threadId} has invalid format");
            }

            _logger.LogDebug("Thread not found | ThreadId: {ThreadId}", threadId);
            throw new InvalidOperationException($"Thread {threadId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load thread | ThreadId: {ThreadId}", threadId);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> GetUserThreadsAsync(string userId, int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            SetCurrentUserId(userId);
            var indexKey = GetThreadIndexKey();
            _logger.LogDebug("Retrieving thread index | UserId: {UserId} | IndexKey: {IndexKey}", userId, indexKey);
            
            var items = await _storage.ReadAsync(new[] { indexKey }, cancellationToken);
            _logger.LogDebug("Storage returned {Count} items", items?.Count ?? 0);

            if (items != null && items.TryGetValue(indexKey, out var value))
            {
                _logger.LogDebug("Found index document | ValueType: {ValueType}", value?.GetType().Name ?? "null");
                
                // Try to handle both JsonElement and Dictionary<string, object> formats
                JsonElement docElement;
                
                if (value is JsonElement jsonElem)
                {
                    docElement = jsonElem;
                }
                else if (value is Dictionary<string, object> dict)
                {
                    // Convert Dictionary to JsonElement
                    docElement = JsonSerializer.SerializeToElement(dict);
                }
                else
                {
                    _logger.LogWarning("Index document is unexpected type: {Type} | UserId: {UserId}", value?.GetType().FullName ?? "null", userId);
                    return new Dictionary<string, string>();
                }
                
                // Check if this is the wrapped format from CosmosDbPartitionedStorage
                if (docElement.TryGetProperty("document", out var nestedDoc))
                {
                    _logger.LogDebug("Unwrapping nested 'document' property");
                    docElement = nestedDoc;
                }
                
                if (docElement.ValueKind == JsonValueKind.Object)
                {
                    // Try new format with metadata first
                    if (docElement.TryGetProperty("threads", out var threadsElement))
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var threads = JsonSerializer.Deserialize<List<ThreadMetadata>>(threadsElement.GetRawText(), options) ?? new List<ThreadMetadata>();
                        _logger.LogInformation("Retrieved {Count} threads with metadata for user {UserId}", threads.Count, userId);
                        return threads.Take(limit).ToDictionary(t => t.ThreadId, t => t.Title);
                    }
                    // Fallback to old format with just IDs
                    else if (docElement.TryGetProperty("threadIds", out var threadIdsElement))
                    {
                        var threadIds = JsonSerializer.Deserialize<List<string>>(threadIdsElement.GetRawText()) ?? new List<string>();
                        _logger.LogInformation("Retrieved {Count} thread IDs (old format) for user {UserId}", threadIds.Count, userId);
                        return threadIds.Take(limit).ToDictionary(id => id, id => id);
                    }
                    else
                    {
                        _logger.LogWarning("Index document missing 'threads' or 'threadIds' property | UserId: {UserId}", userId);
                    }
                }
                else
                {
                    _logger.LogWarning("Index document JsonElement is not an object, it's {Kind} | UserId: {UserId}", docElement.ValueKind, userId);
                }
            }
            else
            {
                _logger.LogInformation("No thread index found for user {UserId} - returning empty dictionary", userId);
            }

            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user threads | UserId: {UserId}", userId);
            return new Dictionary<string, string>();
        }
    }

    public async Task AddThreadToUserIndexAsync(string userId, string threadId, string title, CancellationToken cancellationToken = default)
    {
        try
        {
            SetCurrentUserId(userId);
            var indexKey = GetThreadIndexKey();
            var items = await _storage.ReadAsync(new[] { indexKey }, cancellationToken);

            List<ThreadMetadata> threads;
            if (items != null && items.TryGetValue(indexKey, out var value))
            {
                // Try to handle both JsonElement and Dictionary<string, object> formats
                JsonElement docElement;
                
                if (value is JsonElement jsonElem)
                {
                    docElement = jsonElem;
                }
                else if (value is Dictionary<string, object> dict)
                {
                    // Convert Dictionary to JsonElement
                    docElement = JsonSerializer.SerializeToElement(dict);
                }
                else
                {
                    _logger.LogWarning("Unexpected index document type when adding thread: {Type}", value?.GetType().FullName ?? "null");
                    threads = new List<ThreadMetadata>();
                    docElement = default;
                }
                
                // Check if this is the wrapped format from CosmosDbPartitionedStorage
                if (docElement.ValueKind == JsonValueKind.Object && docElement.TryGetProperty("document", out var nestedDoc))
                {
                    _logger.LogDebug("Unwrapping nested 'document' property when adding thread");
                    docElement = nestedDoc;
                }
                
                if (docElement.ValueKind == JsonValueKind.Object)
                {
                    // Try new format first
                    if (docElement.TryGetProperty("threads", out var threadsElement))
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        threads = JsonSerializer.Deserialize<List<ThreadMetadata>>(threadsElement.GetRawText(), options) ?? new List<ThreadMetadata>();
                    }
                    // Migrate from old format
                    else if (docElement.TryGetProperty("threadIds", out var threadIdsElement))
                    {
                        var oldThreadIds = JsonSerializer.Deserialize<List<string>>(threadIdsElement.GetRawText()) ?? new List<string>();
                        threads = oldThreadIds.Select(id => new ThreadMetadata 
                        { 
                            ThreadId = id, 
                            Title = id,
                            CreatedAt = DateTimeOffset.UtcNow 
                        }).ToList();
                    }
                    else
                    {
                        threads = new List<ThreadMetadata>();
                    }
                }
                else
                {
                    threads = new List<ThreadMetadata>();
                }
            }
            else
            {
                threads = new List<ThreadMetadata>();
            }

            if (!threads.Any(t => t.ThreadId == threadId))
            {
                threads.Insert(0, new ThreadMetadata
                {
                    ThreadId = threadId,
                    Title = title,
                    CreatedAt = DateTimeOffset.UtcNow
                }); // Most recent first
                
                // Create wrapper document with id and userId
                var document = new Dictionary<string, object>
                {
                    { "id", indexKey },
                    { "userId", userId },
                    { "threads", threads }
                };
                
                await _storage.WriteAsync(new Dictionary<string, object>
                {
                    { indexKey, document }
                }, cancellationToken);
                
                _logger.LogInformation("Added thread to user index | UserId: {UserId} | ThreadId: {ThreadId} | Title: {Title} | TotalThreads: {TotalThreads}", 
                    userId, threadId, title, threads.Count);
            }
            else
            {
                _logger.LogDebug("Thread already in user index | UserId: {UserId} | ThreadId: {ThreadId}", userId, threadId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add thread to user index | UserId: {UserId} | ThreadId: {ThreadId}", userId, threadId);
        }
    }

    private string GetThreadKey(string threadId) => $"{_currentUserId}:{threadId}";
    private string GetThreadIndexKey() => $"thread-index:{_currentUserId}";
}

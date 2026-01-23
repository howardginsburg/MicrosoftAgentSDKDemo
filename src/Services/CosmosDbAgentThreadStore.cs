using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Services;

public class CosmosDbAgentThreadStore : AgentThreadStore
{
    private readonly IStorage _storage;
    private readonly ILogger<CosmosDbAgentThreadStore> _logger;

    public CosmosDbAgentThreadStore(
        IStorage storage,
        ILogger<CosmosDbAgentThreadStore> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public override async ValueTask SaveThreadAsync(
        AIAgent agent,
        string threadId,
        AgentThread thread,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = thread.Serialize();
            var key = GetThreadKey(threadId);
            
            // Create a wrapper object with id for Cosmos DB
            var document = new Dictionary<string, object>
            {
                { "id", key },
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
                // Extract threadData from the wrapper document
                if (value is JsonElement docElement && docElement.ValueKind == JsonValueKind.Object)
                {
                    if (docElement.TryGetProperty("threadData", out var threadDataElement))
                    {
                        var thread = await agent.DeserializeThreadAsync(threadDataElement, cancellationToken: cancellationToken);
                        _logger.LogDebug("Thread loaded | ThreadId: {ThreadId}", threadId);
                        return thread;
                    }
                }
                
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

    public async Task<List<string>> GetUserThreadIdsAsync(string userId, int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = GetThreadIndexKey(userId);
            var items = await _storage.ReadAsync(new[] { indexKey }, cancellationToken);

            if (items != null && items.TryGetValue(indexKey, out var value))
            {
                if (value is JsonElement docElement && docElement.ValueKind == JsonValueKind.Object)
                {
                    if (docElement.TryGetProperty("threadIds", out var threadIdsElement))
                    {
                        var threadIds = JsonSerializer.Deserialize<List<string>>(threadIdsElement.GetRawText()) ?? new List<string>();
                        return threadIds.Take(limit).ToList();
                    }
                }
            }

            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user thread IDs | UserId: {UserId}", userId);
            return new List<string>();
        }
    }

    public async Task AddThreadToUserIndexAsync(string userId, string threadId, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = GetThreadIndexKey(userId);
            var items = await _storage.ReadAsync(new[] { indexKey }, cancellationToken);

            List<string> threadIds;
            if (items != null && items.TryGetValue(indexKey, out var value))
            {
                if (value is JsonElement docElement && docElement.ValueKind == JsonValueKind.Object)
                {
                    if (docElement.TryGetProperty("threadIds", out var threadIdsElement))
                    {
                        threadIds = JsonSerializer.Deserialize<List<string>>(threadIdsElement.GetRawText()) ?? new List<string>();
                    }
                    else
                    {
                        threadIds = new List<string>();
                    }
                }
                else
                {
                    threadIds = new List<string>();
                }
            }
            else
            {
                threadIds = new List<string>();
            }

            if (!threadIds.Contains(threadId))
            {
                threadIds.Insert(0, threadId); // Most recent first
                
                // Create wrapper document with id
                var document = new Dictionary<string, object>
                {
                    { "id", indexKey },
                    { "threadIds", threadIds }
                };
                
                await _storage.WriteAsync(new Dictionary<string, object>
                {
                    { indexKey, document }
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add thread to user index | UserId: {UserId} | ThreadId: {ThreadId}", userId, threadId);
        }
    }

    private static string GetThreadKey(string threadId) => $"thread:{threadId}";
    private static string GetThreadIndexKey(string userId) => $"thread-index:{userId}";
}

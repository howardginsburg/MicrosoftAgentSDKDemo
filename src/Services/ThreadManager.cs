using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Models;

namespace MicrosoftAgentSDKDemo.Services;

public interface IThreadManager
{
    Task<string> CreateThreadAsync(string userId, string initialMessage);
    Task<ThreadDocument?> GetThreadAsync(string userId, string threadId);
    Task<List<ThreadDocument>> GetUserThreadsAsync(string userId, int limit = 10);
    Task SaveMessageAsync(string userId, string threadId, string role, string content);
    Task UpdateThreadNameAsync(string userId, string threadId, string threadName);
}

public class ThreadManager : IThreadManager
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;
    private readonly ILogger<ThreadManager> _logger;

    public ThreadManager(
        IConfiguration configuration,
        ILogger<ThreadManager> logger)
    {
        _logger = logger;

        var cosmosConfig = configuration.GetSection("CosmosDB");
        var endpoint = cosmosConfig["Endpoint"] ?? throw new InvalidOperationException("CosmosDB Endpoint not configured");
        var accountKey = cosmosConfig["AccountKey"] ?? throw new InvalidOperationException("CosmosDB AccountKey not configured");
        var databaseName = cosmosConfig["DatabaseName"] ?? "agent-database";
        var containerId = cosmosConfig["ContainerId"] ?? "conversations";

        _cosmosClient = new CosmosClient(endpoint, accountKey);
        _container = _cosmosClient.GetDatabase(databaseName).GetContainer(containerId);

        _logger.LogDebug("ThreadManager initialized with Cosmos DB endpoint: {Endpoint}", endpoint);
    }

    public async Task<string> CreateThreadAsync(string userId, string initialMessage)
    {
        var threadId = Guid.NewGuid().ToString();
        var threadName = ExtractThreadName(initialMessage);
        var now = DateTimeOffset.UtcNow;

        var threadDoc = new ThreadDocument
        {
            Id = threadId,
            UserId = userId,
            ThreadName = threadName,
            CreatedDate = now,
            LastActivity = now,
            MessageCount = 0,
            Messages = []
        };

        try
        {
            await _container.CreateItemAsync(threadDoc, new PartitionKey(userId));
            _logger.LogDebug("Thread created in Cosmos DB | ThreadId: {ThreadId} | UserId: {UserId}", threadId, userId);
            return threadId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create thread | ThreadId: {ThreadId} | UserId: {UserId}", threadId, userId);
            throw;
        }
    }

    public async Task<ThreadDocument?> GetThreadAsync(string userId, string threadId)
    {
        try
        {
            _logger.LogDebug("Retrieving thread | ThreadId: {ThreadId} | UserId: {UserId}", threadId, userId);
            var response = await _container.ReadItemAsync<ThreadDocument>(threadId, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Thread not found | ThreadId: {ThreadId} | UserId: {UserId}", threadId, userId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve thread | ThreadId: {ThreadId} | UserId: {UserId}", threadId, userId);
            throw;
        }
    }

    public async Task<List<ThreadDocument>> GetUserThreadsAsync(string userId, int limit = 10)
    {
        try
        {
            _logger.LogDebug("Retrieving user threads | UserId: {UserId} | Limit: {Limit}", userId, limit);

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.userId = @userId ORDER BY c.lastActivity DESC OFFSET 0 LIMIT @limit")
                .WithParameter("@userId", userId)
                .WithParameter("@limit", limit);

            var iterator = _container.GetItemQueryIterator<ThreadDocument>(query,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

            var threads = new List<ThreadDocument>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                threads.AddRange(response);
            }

            _logger.LogDebug("Retrieved {ThreadCount} threads for user | UserId: {UserId}", threads.Count, userId);
            return threads;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user threads | UserId: {UserId}", userId);
            throw;
        }
    }



    public async Task SaveMessageAsync(string userId, string threadId, string role, string content)
    {
        try
        {
            var thread = await GetThreadAsync(userId, threadId);
            if (thread != null)
            {
                var message = new Message
                {
                    Id = Guid.NewGuid().ToString(),
                    Role = role,
                    Content = content,
                    Timestamp = DateTimeOffset.UtcNow
                };

                thread.Messages.Add(message);
                thread.LastActivity = DateTimeOffset.UtcNow;
                thread.MessageCount++;

                await _container.UpsertItemAsync(thread, new PartitionKey(userId));
                _logger.LogDebug("Message saved | MessageId: {MessageId} | ThreadId: {ThreadId} | Role: {Role}",
                    message.Id, threadId, role);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save message | ThreadId: {ThreadId}", threadId);
            throw;
        }
    }

    public async Task UpdateThreadNameAsync(string userId, string threadId, string threadName)
    {
        try
        {
            var thread = await GetThreadAsync(userId, threadId);
            if (thread != null)
            {
                thread.ThreadName = threadName;
                await _container.UpsertItemAsync(thread, new PartitionKey(userId));
                _logger.LogDebug("Thread name updated | ThreadId: {ThreadId} | ThreadName: {ThreadName}",
                    threadId, threadName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thread name | ThreadId: {ThreadId}", threadId);
            throw;
        }
    }

    private static string ExtractThreadName(string message)
    {
        const int maxLength = 60;
        if (message.Length <= maxLength)
            return message;
        return message[..maxLength] + "...";
    }
}

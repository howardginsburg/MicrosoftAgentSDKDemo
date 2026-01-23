using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Models;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Provides AIFunction tools for thread persistence operations.
/// These tools can be used by agents to autonomously manage conversations.
/// Follows the Agent Framework pattern for tool integration.
/// </summary>
public class ThreadTools
{
    private readonly IThreadManager _threadManager;
    private readonly ILogger<ThreadTools> _logger;

    public ThreadTools(IThreadManager threadManager, ILogger<ThreadTools> logger)
    {
        _threadManager = threadManager;
        _logger = logger;
    }

    [Description("Create a new conversation thread with an initial message")]
    public async Task<string> CreateThreadAsync(
        [Description("The user ID")] string userId,
        [Description("The initial message to start the conversation")] string initialMessage)
    {
        try
        {
            var threadId = await _threadManager.CreateThreadAsync(userId, initialMessage);
            _logger.LogInformation("Thread created via tool | UserId: {UserId} | ThreadId: {ThreadId}", userId, threadId);
            return threadId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create thread via tool");
            throw;
        }
    }

    [Description("Save a message to a conversation thread")]
    public async Task SaveMessageAsync(
        [Description("The user ID")] string userId,
        [Description("The thread ID")] string threadId,
        [Description("The message role (user or assistant)")] string role,
        [Description("The message content")] string content)
    {
        try
        {
            await _threadManager.SaveMessageAsync(userId, threadId, role, content);
            _logger.LogDebug("Message saved via tool | ThreadId: {ThreadId} | Role: {Role}", threadId, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save message via tool");
            throw;
        }
    }

    [Description("Get a specific conversation thread")]
    public async Task<ThreadDTO?> GetThreadAsync(
        [Description("The user ID")] string userId,
        [Description("The thread ID")] string threadId)
    {
        try
        {
            var thread = await _threadManager.GetThreadAsync(userId, threadId);
            if (thread == null)
                return null;

            return new ThreadDTO
            {
                Id = thread.Id,
                ThreadName = thread.ThreadName,
                MessageCount = thread.MessageCount,
                LastActivity = thread.LastActivity
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get thread via tool");
            throw;
        }
    }

    [Description("List all conversation threads for a user")]
    public async Task<List<ThreadDTO>> GetUserThreadsAsync(
        [Description("The user ID")] string userId,
        [Description("Maximum number of threads to return")] int limit = 10)
    {
        try
        {
            var threads = await _threadManager.GetUserThreadsAsync(userId, limit);
            return threads.Select(t => new ThreadDTO
            {
                Id = t.Id,
                ThreadName = t.ThreadName,
                MessageCount = t.MessageCount,
                LastActivity = t.LastActivity
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user threads via tool");
            throw;
        }
    }

    [Description("Update the name of a conversation thread")]
    public async Task UpdateThreadNameAsync(
        [Description("The user ID")] string userId,
        [Description("The thread ID")] string threadId,
        [Description("The new thread name")] string threadName)
    {
        try
        {
            await _threadManager.UpdateThreadNameAsync(userId, threadId, threadName);
            _logger.LogDebug("Thread name updated via tool | ThreadId: {ThreadId} | ThreadName: {ThreadName}", threadId, threadName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thread name via tool");
            throw;
        }
    }
}

/// <summary>
/// DTO for thread information returned by tools
/// </summary>
public record ThreadDTO
{
    public string Id { get; set; } = string.Empty;
    public string ThreadName { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTimeOffset LastActivity { get; set; }
}

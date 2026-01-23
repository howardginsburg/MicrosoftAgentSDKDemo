using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Services;

public interface ITelemetryService
{
    void LogThreadCreated(string userId, string threadId, string threadName);
    void LogMessageSent(string userId, string threadId, string content);
    void LogAgentResponse(string userId, string threadId, string content, int tokensUsed, double latencyMs);
    void LogApiCall(string endpoint, string model, int promptTokens, int completionTokens, double latencyMs);
    void LogThreadSwitch(string userId, string threadId, string threadName);
    void LogSessionStart(string userId);
    void LogSessionEnd(string userId, TimeSpan duration);
    void LogError(string userId, string threadId, string errorMessage, Exception? exception = null);
}

public class TelemetryService : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly TelemetryClient _telemetryClient;

    public TelemetryService(ILogger<TelemetryService> logger, TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    public void LogThreadCreated(string userId, string threadId, string threadName)
    {
        _logger.LogInformation("Thread created | UserId: {UserId} | ThreadId: {ThreadId} | ThreadName: {ThreadName}",
            userId, threadId, threadName);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["ThreadId"] = threadId,
            ["ThreadName"] = threadName
        };

        _telemetryClient.TrackEvent("ThreadCreated", properties);
    }

    public void LogMessageSent(string userId, string threadId, string content)
    {
        _logger.LogInformation("User message sent | UserId: {UserId} | ThreadId: {ThreadId} | Content: {Content}",
            userId, threadId, content);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["ThreadId"] = threadId,
            ["Content"] = content
        };

        _telemetryClient.TrackEvent("MessageSent", properties);
    }

    public void LogAgentResponse(string userId, string threadId, string content, int tokensUsed, double latencyMs)
    {
        _logger.LogInformation("Agent response | UserId: {UserId} | ThreadId: {ThreadId} | Tokens: {Tokens} | Latency: {LatencyMs}ms | Content: {Content}",
            userId, threadId, tokensUsed, latencyMs, content);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["ThreadId"] = threadId,
            ["Content"] = content,
            ["TokensUsed"] = tokensUsed.ToString()
        };

        var metrics = new Dictionary<string, double>
        {
            ["LatencyMs"] = latencyMs,
            ["TokensUsed"] = tokensUsed
        };

        _telemetryClient.TrackEvent("AgentResponse", properties, metrics);
    }

    public void LogApiCall(string endpoint, string model, int promptTokens, int completionTokens, double latencyMs)
    {
        _logger.LogDebug("API call | Endpoint: {Endpoint} | Model: {Model} | PromptTokens: {PromptTokens} | CompletionTokens: {CompletionTokens} | Latency: {LatencyMs}ms",
            endpoint, model, promptTokens, completionTokens, latencyMs);

        var properties = new Dictionary<string, string>
        {
            ["Endpoint"] = endpoint,
            ["Model"] = model
        };

        var metrics = new Dictionary<string, double>
        {
            ["PromptTokens"] = promptTokens,
            ["CompletionTokens"] = completionTokens,
            ["LatencyMs"] = latencyMs
        };

        _telemetryClient.TrackEvent("ApiCall", properties, metrics);
    }

    public void LogThreadSwitch(string userId, string threadId, string threadName)
    {
        _logger.LogInformation("Thread switched | UserId: {UserId} | ThreadId: {ThreadId} | ThreadName: {ThreadName}",
            userId, threadId, threadName);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["ThreadId"] = threadId,
            ["ThreadName"] = threadName
        };

        _telemetryClient.TrackEvent("ThreadSwitched", properties);
    }

    public void LogSessionStart(string userId)
    {
        _logger.LogInformation("Session started | UserId: {UserId}", userId);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId
        };

        _telemetryClient.TrackEvent("SessionStart", properties);
    }

    public void LogSessionEnd(string userId, TimeSpan duration)
    {
        _logger.LogInformation("Session ended | UserId: {UserId} | Duration: {Duration}",
            userId, duration.TotalSeconds);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId
        };

        var metrics = new Dictionary<string, double>
        {
            ["DurationSeconds"] = duration.TotalSeconds
        };

        _telemetryClient.TrackEvent("SessionEnd", properties, metrics);
    }

    public void LogError(string userId, string threadId, string errorMessage, Exception? exception = null)
    {
        _logger.LogError(exception, "Error occurred | UserId: {UserId} | ThreadId: {ThreadId} | Error: {ErrorMessage}",
            userId, threadId, errorMessage);

        var properties = new Dictionary<string, string>
        {
            ["UserId"] = userId,
            ["ThreadId"] = threadId,
            ["ErrorMessage"] = errorMessage
        };

        _telemetryClient.TrackEvent("Error", properties);
    }
}

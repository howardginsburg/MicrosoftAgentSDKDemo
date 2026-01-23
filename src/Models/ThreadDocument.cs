using Newtonsoft.Json;

namespace MicrosoftAgentSDKDemo.Models;

public class Message
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty; // "user" or "assistant"

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

public class ThreadDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty("threadName")]
    public string ThreadName { get; set; } = string.Empty;

    [JsonProperty("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [JsonProperty("lastActivity")]
    public DateTimeOffset LastActivity { get; set; }

    [JsonProperty("messageCount")]
    public int MessageCount { get; set; }

    [JsonProperty("messages")]
    public List<Message> Messages { get; set; } = [];
}

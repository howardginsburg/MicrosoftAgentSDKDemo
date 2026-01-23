using Newtonsoft.Json;

namespace MicrosoftAgentSDKDemo.Models;

public class MessageDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty; // "user" or "assistant"

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

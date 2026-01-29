using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Extension methods for creating multimodal ChatMessages with file attachments.
/// Supports both text files and images through proper ChatMessage content array.
/// </summary>
public static class MultimodalMessageExtensions
{
    /// <summary>
    /// Creates a ChatMessage with text and attachments (text files and images).
    /// Uses the ChatMessage content array for proper multimodal support.
    /// </summary>
    public static ChatMessage CreateMultimodalMessage(string userMessage, List<AIContent> attachments)
    {
        var contents = new List<AIContent>();
        
        // Add user's text message first
        contents.Add(new TextContent(userMessage));
        
        // Add all attachments (both text and images)
        contents.AddRange(attachments);
        
        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// Checks if any attachments contain images (DataContent).
    /// </summary>
    public static bool HasImageAttachments(this IEnumerable<AIContent> attachments)
    {
        return attachments.OfType<DataContent>().Any();
    }
}

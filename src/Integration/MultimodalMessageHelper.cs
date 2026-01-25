using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Helper service for creating multimodal ChatMessages with file attachments.
/// Supports both text files and images through proper ChatMessage content array.
/// </summary>
public class MultimodalMessageHelper
{
    private readonly ILogger<MultimodalMessageHelper> _logger;

    public MultimodalMessageHelper(ILogger<MultimodalMessageHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a ChatMessage with text and attachments (text files and images).
    /// Uses the ChatMessage content array for proper multimodal support.
    /// </summary>
    public ChatMessage CreateMultimodalMessage(string userMessage, List<AIContent> attachments)
    {
        var contents = new List<AIContent>();
        
        // Add user's text message first
        contents.Add(new TextContent(userMessage));
        
        // Add all attachments (both text and images)
        contents.AddRange(attachments);
        
        _logger.LogDebug("Created multimodal message with {Count} content items", contents.Count);
        
        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// Checks if any attachments contain images (DataContent).
    /// </summary>
    public bool HasImageAttachments(List<AIContent> attachments)
    {
        return attachments.OfType<DataContent>().Any();
    }
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Service for handling file attachments and converting them to AI content.
/// Supports text files and images.
/// </summary>
public interface IFileAttachmentService
{
    Task<List<AIContent>> ProcessFileAttachmentsAsync(string filePaths);
}

public class FileAttachmentService : IFileAttachmentService
{
    private readonly ILogger<FileAttachmentService> _logger;

    // Supported file extensions
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", 
        ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".html", ".css",
        ".yaml", ".yml", ".toml", ".ini", ".config"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB limit

    public FileAttachmentService(ILogger<FileAttachmentService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes file paths (comma-separated) and converts them to AIContent objects.
    /// </summary>
    public async Task<List<AIContent>> ProcessFileAttachmentsAsync(string filePaths)
    {
        var contents = new List<AIContent>();

        if (string.IsNullOrWhiteSpace(filePaths))
            return contents;

        var paths = filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    _logger.LogWarning("File not found: {Path}", path);
                    contents.Add(new TextContent($"[File not found: {path}]"));
                    continue;
                }

                var fileInfo = new FileInfo(path);
                
                // Check file size
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    _logger.LogWarning("File too large (max 10MB): {Path} ({Size} bytes)", path, fileInfo.Length);
                    contents.Add(new TextContent($"[File too large: {Path.GetFileName(path)} ({FormatFileSize(fileInfo.Length)})]"));
                    continue;
                }

                var extension = fileInfo.Extension.ToLowerInvariant();

                if (TextExtensions.Contains(extension))
                {
                    var content = await ProcessTextFileAsync(path);
                    if (content != null)
                        contents.Add(content);
                }
                else if (ImageExtensions.Contains(extension))
                {
                    var content = await ProcessImageFileAsync(path);
                    if (content != null)
                        contents.Add(content);
                }
                else
                {
                    _logger.LogWarning("Unsupported file type: {Path} ({Extension})", path, extension);
                    contents.Add(new TextContent($"[Unsupported file type: {Path.GetFileName(path)}]"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {Path}", path);
                contents.Add(new TextContent($"[Error reading file: {Path.GetFileName(path)}]"));
            }
        }

        return contents;
    }

    private async Task<AIContent?> ProcessTextFileAsync(string path)
    {
        try
        {
            var content = await File.ReadAllTextAsync(path);
            var fileName = Path.GetFileName(path);
            
            _logger.LogDebug("Processed text file: {FileName} ({Length} characters)", fileName, content.Length);
            
            // Format as a code block or quoted content for better readability
            var formattedContent = $"📄 **File: {fileName}**\n```\n{content}\n```";
            
            return new TextContent(formattedContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read text file: {Path}", path);
            return null;
        }
    }

    private async Task<AIContent?> ProcessImageFileAsync(string path)
    {
        try
        {
            var imageBytes = await File.ReadAllBytesAsync(path);
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            
            // Map file extension to media type
            var mediaType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/jpeg" // default
            };
            
            _logger.LogInformation("Processed image file: {FileName} ({Size} bytes, {MediaType})", 
                fileName, imageBytes.Length, mediaType);
            
            return new DataContent(imageBytes, mediaType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read image file: {Path}", path);
            return null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

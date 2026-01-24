using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Images;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Service for generating images using Azure OpenAI DALL-E
/// </summary>
public interface IImageGenerationService
{
    Task<ImageResult> GenerateImageAsync(string prompt, string size = "1024x1024", string quality = "standard");
}

public record ImageResult(string Url, string LocalPath);

public class AzureOpenAIImageService : IImageGenerationService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAIImageService> _logger;

    public AzureOpenAIImageService(
        IConfiguration configuration,
        ILogger<AzureOpenAIImageService> logger)
    {
        var openAIConfig = configuration.GetSection("AzureOpenAI");
        var dallEEndpoint = openAIConfig["DallEEndpoint"] ?? openAIConfig["Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI DallEEndpoint not configured");
        _deploymentName = openAIConfig["DallEDeploymentName"] ?? "dall-e-3";
        
        var credential = new AzureCliCredential();
        _client = new AzureOpenAIClient(new Uri(dallEEndpoint), credential);
        _logger = logger;
    }

    public async Task<ImageResult> GenerateImageAsync(string prompt, string size = "1024x1024", string quality = "standard")
    {
        try
        {
            _logger.LogInformation("Generating image | Prompt: {Prompt} | Size: {Size} | Quality: {Quality}", 
                prompt.Length > 50 ? prompt.Substring(0, 50) + "..." : prompt, size, quality);

            var imageClient = _client.GetImageClient(_deploymentName);
            
            var imageSize = size switch
            {
                "1792x1024" => GeneratedImageSize.W1792xH1024,
                "1024x1792" => GeneratedImageSize.W1024xH1792,
                _ => GeneratedImageSize.W1024xH1024
            };

            var imageQuality = quality.Equals("hd", StringComparison.OrdinalIgnoreCase) 
                ? GeneratedImageQuality.High 
                : GeneratedImageQuality.Standard;

            var options = new ImageGenerationOptions
            {
                Size = imageSize,
                Quality = imageQuality,
                ResponseFormat = GeneratedImageFormat.Bytes // Get base64 instead of URL
            };

            var response = await imageClient.GenerateImageAsync(prompt, options);
            var imageBytes = response.Value.ImageBytes.ToArray();

            _logger.LogInformation("Image generated successfully | Size: {Size} bytes", imageBytes.Length);
            
            // Save the image locally
            var localPath = await SaveImageAsync(imageBytes);
            _logger.LogInformation("Image saved | Path: {Path}", localPath);

            return new ImageResult("(base64 data)", localPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate image | Prompt: {Prompt}", prompt);
            throw;
        }
    }

    private async Task<string> SaveImageAsync(byte[] imageBytes)
    {
        // Create images directory if it doesn't exist
        var imagesDir = Path.Combine(AppContext.BaseDirectory, "images");
        Directory.CreateDirectory(imagesDir);
        
        // Generate filename with timestamp
        var fileName = $"dalle_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(imagesDir, fileName);
        
        await File.WriteAllBytesAsync(filePath, imageBytes);
        return filePath;
    }
}

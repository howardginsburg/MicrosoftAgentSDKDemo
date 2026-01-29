using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Service for exporting chat conversations to PDF format.
/// Preserves the visual styling from the console UI.
/// </summary>
public interface IChatExportService
{
    /// <summary>
    /// Exports a conversation to PDF format.
    /// </summary>
    Task<string> ExportToPdfAsync(
        IEnumerable<ChatMessage> messages, 
        string username, 
        string threadId,
        CancellationToken cancellationToken = default);
}

public class ChatExportService : IChatExportService
{
    private readonly ILogger<ChatExportService> _logger;
    private readonly string _agentName;
    private readonly string _displayName;

    // Colors matching Spectre.Console theme
    private static readonly string CyanColor = "#00D7FF";      // Cyan1 for user
    private static readonly string BlueColor = "#0077FF";      // Blue for agent panels
    private static readonly string DarkBackground = "#1E1E1E"; // Dark theme background
    private static readonly string PanelBackground = "#2D2D2D"; // Slightly lighter for panels
    private static readonly string TextColor = "#FFFFFF";       // White text
    private static readonly string DimTextColor = "#808080";    // Gray for timestamps

    static ChatExportService()
    {
        // Configure QuestPDF license (Community license for open source)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ChatExportService(
        ILogger<ChatExportService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _agentName = configuration["Application:AgentName"] ?? "Agent";
        _displayName = configuration["Application:DisplayName"] ?? "Agent SDK Demo";
    }

    public async Task<string> ExportToPdfAsync(
        IEnumerable<ChatMessage> messages,
        string username,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        
        // Create exports directory
        var exportsDir = Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(exportsDir);
        
        var fileName = $"chat_{username}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(exportsDir, fileName);

        _logger.LogInformation("Exporting {MessageCount} messages to PDF | Path: {Path}", 
            messageList.Count, filePath);

        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(TextColor));
                    page.PageColor(DarkBackground);

                    // Header
                    page.Header().Element(ComposeHeader);

                    // Content - conversation messages
                    page.Content().Element(content => ComposeContent(content, messageList, username));

                    // Footer with page numbers
                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf(filePath);
        }, cancellationToken);

        _logger.LogInformation("PDF export completed | Path: {Path}", filePath);
        
        return filePath;
    }

    private void ComposeHeader(IContainer container)
    {
        container
            .PaddingBottom(15)
            .BorderBottom(2)
            .BorderColor(CyanColor)
            .Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item()
                        .Text(_displayName)
                        .FontSize(24)
                        .Bold()
                        .FontColor(CyanColor);
                    
                    col.Item()
                        .Text($"Conversation Export - {DateTime.Now:MMMM dd, yyyy}")
                        .FontSize(10)
                        .FontColor(DimTextColor);
                });
            });
    }

    private void ComposeContent(IContainer container, List<ChatMessage> messages, string username)
    {
        container.PaddingVertical(10).Column(column =>
        {
            column.Spacing(15);

            foreach (var message in messages)
            {
                var text = message.Text ?? string.Empty;
                
                // Skip empty messages or tool calls
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Check for image content
                var hasImage = message.Contents?.OfType<DataContent>().Any() ?? false;

                if (message.Role.Value == "user")
                {
                    column.Item().Element(c => ComposeUserMessage(c, username, text, hasImage));
                }
                else if (message.Role.Value == "assistant")
                {
                    column.Item().Element(c => ComposeAgentMessage(c, text));
                }
            }
        });
    }

    private void ComposeUserMessage(IContainer container, string username, string text, bool hasImage)
    {
        container.Row(row =>
        {
            row.AutoItem()
                .Width(30)
                .Height(30)
                .AlignMiddle()
                .Text("👤")
                .FontSize(18);

            row.RelativeItem()
                .PaddingLeft(10)
                .Column(col =>
                {
                    col.Item()
                        .Text(txt =>
                        {
                            txt.Span(username)
                                .Bold()
                                .FontColor(CyanColor);
                        });
                    
                    col.Item()
                        .PaddingTop(5)
                        .Text(text)
                        .FontColor(TextColor);
                    
                    if (hasImage)
                    {
                        col.Item()
                            .PaddingTop(5)
                            .Text("📎 [Image attachment]")
                            .FontSize(9)
                            .FontColor(DimTextColor);
                    }
                });
        });
    }

    private void ComposeAgentMessage(IContainer container, string text)
    {
        container
            .Background(PanelBackground)
            .Border(1)
            .BorderColor(BlueColor)
            .Padding(12)
            .Column(col =>
            {
                // Panel header
                col.Item()
                    .PaddingBottom(8)
                    .BorderBottom(1)
                    .BorderColor(BlueColor)
                    .Row(headerRow =>
                    {
                        headerRow.AutoItem()
                            .Text("🤖")
                            .FontSize(14);
                        
                        headerRow.RelativeItem()
                            .PaddingLeft(8)
                            .Text(_agentName)
                            .Bold()
                            .FontColor(BlueColor);
                    });

                // Panel content
                col.Item()
                    .PaddingTop(8)
                    .Text(text)
                    .FontColor(TextColor);
            });
    }

    private void ComposeFooter(IContainer container)
    {
        container
            .PaddingTop(10)
            .BorderTop(1)
            .BorderColor(DimTextColor)
            .Row(row =>
            {
                row.RelativeItem()
                    .Text(txt =>
                    {
                        txt.Span("Generated by ")
                            .FontSize(8)
                            .FontColor(DimTextColor);
                        txt.Span(_displayName)
                            .FontSize(8)
                            .FontColor(CyanColor);
                    });

                row.RelativeItem()
                    .AlignRight()
                    .Text(txt =>
                    {
                        txt.Span("Page ")
                            .FontSize(8)
                            .FontColor(DimTextColor);
                        txt.CurrentPageNumber()
                            .FontSize(8)
                            .FontColor(TextColor);
                        txt.Span(" of ")
                            .FontSize(8)
                            .FontColor(DimTextColor);
                        txt.TotalPages()
                            .FontSize(8)
                            .FontColor(TextColor);
                    });
            });
    }
}

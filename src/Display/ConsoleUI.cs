using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace MicrosoftAgentSDKDemo.Display;

/// <summary>
/// Handles console-based user interface for thread selection and chat interactions.
/// </summary>
public interface IConsoleUI
{
    Task<string> GetUsernameAsync();
    Task<ThreadSelection> GetThreadSelectionAsync(Dictionary<string, string> threads, string username);
    Task<(string Message, string? FilePaths)> GetFirstMessageWithAttachmentsAsync();
    Task<string> GetChatInputAsync(string username);
    void DisplayThreadCreated(string threadId);
    void DisplayThreadLoaded(string threadId);
    void DisplayConversationHistory(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, string username);
    void DisplayAgentResponse(string response);
    void DisplayImageGenerated(string imageUrl);
    void DisplayError(string message);
    void DisplayGoodbye();
    void DisplayAttachmentsProcessed(int count);
}

public record ThreadSelection(ThreadSelectionType Type, string? ThreadId = null, string? FirstMessage = null, string? FilePaths = null);

public enum ThreadSelectionType
{
    New,
    Existing,
    Exit
}

public class ConsoleUI : IConsoleUI
{
    private readonly ILogger<ConsoleUI> _logger;

    public ConsoleUI(ILogger<ConsoleUI> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetUsernameAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("Agent SDK Demo")
                .Centered()
                .Color(Color.Cyan1));
        
        AnsiConsole.WriteLine();
        
        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan1]Enter your username:[/]")
                .PromptStyle("green")
                .ValidationErrorMessage("[red]Username cannot be empty[/]")
                .Validate(name => !string.IsNullOrWhiteSpace(name)));
        
        return await Task.FromResult(username);
    }

    public async Task<ThreadSelection> GetThreadSelectionAsync(Dictionary<string, string> threads, string username)
    {
        AnsiConsole.Clear();
        
        var rule = new Rule($"[cyan1]{username}'s Conversation Threads[/]");
        rule.Style = Style.Parse("cyan1");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
        
        // Build selection choices
        var choices = new List<string> { "[green]📝 Start a new conversation[/]" };
        
        var threadList = new List<string>();
        foreach (var thread in threads)
        {
            var displayTitle = thread.Value.Length > 70 ? thread.Value.Substring(0, 67) + "..." : thread.Value;
            var choice = $"[yellow]💬[/] {displayTitle}";
            choices.Add(choice);
            threadList.Add(thread.Key); // Track actual thread IDs
        }
        
        choices.Add("[red]🚪 Logout[/]");
        
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan1]Select an option:[/]")
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to see more threads)[/]")
                .AddChoices(choices));
        
        if (selection.Contains("Start a new conversation"))
        {
            var (firstMessage, filePaths) = await GetFirstMessageWithAttachmentsAsync();
            return new ThreadSelection(ThreadSelectionType.New, FirstMessage: firstMessage, FilePaths: filePaths);
        }
        else if (selection.Contains("Logout"))
        {
            return new ThreadSelection(ThreadSelectionType.Exit);
        }
        else
        {
            // Find the thread ID based on selection index
            var selectedIndex = choices.IndexOf(selection) - 1; // -1 because first choice is "new"
            var selectedThreadId = threadList[selectedIndex];
            return new ThreadSelection(ThreadSelectionType.Existing, ThreadId: selectedThreadId);
        }
    }

    public async Task<(string Message, string? FilePaths)> GetFirstMessageWithAttachmentsAsync()
    {
        AnsiConsole.WriteLine();
        var message = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan1]What would you like to talk about?[/]")
                .PromptStyle("green")
                .AllowEmpty());
        
        if (string.IsNullOrWhiteSpace(message))
            return await Task.FromResult((string.Empty, (string?)null));
        
        // Prompt for file attachments when starting a new conversation
        AnsiConsole.Markup("[dim]Attach files? (Enter file paths separated by commas, or press Enter to skip)[/]\n");
        AnsiConsole.Markup("[dim]Note: File attachments are only available when starting a new conversation[/]\n");
        AnsiConsole.Markup("[grey]Files >[/] ");
        var filePaths = Console.ReadLine();
        
        return await Task.FromResult((message, string.IsNullOrWhiteSpace(filePaths) ? null : filePaths));
    }

    public async Task<string> GetChatInputAsync(string username)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup($"[bold cyan1]{username}[/] [grey]>[/] ");
        var input = Console.ReadLine() ?? string.Empty;
        
        return await Task.FromResult(input);
    }

    public void DisplayThreadCreated(string threadId)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓[/] Created new conversation");
        AnsiConsole.WriteLine();
    }

    public void DisplayThreadLoaded(string threadId)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓[/] Conversation loaded");
        AnsiConsole.WriteLine();
    }

    public void DisplayConversationHistory(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, string username)
    {
        AnsiConsole.Clear();
        
        var rule = new Rule("[cyan1]📜 Conversation History[/]");
        rule.Style = Style.Parse("cyan1");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        foreach (var message in messages)
        {
            var text = message.Text ?? string.Empty;
            
            // Skip empty messages or tool calls
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (message.Role.Value == "user")
            {
                AnsiConsole.MarkupLine($"[bold cyan1]👤 {username.EscapeMarkup()}:[/] {text.EscapeMarkup()}");
                AnsiConsole.WriteLine();
            }
            else if (message.Role.Value == "assistant")
            {
                var panel = new Panel(new Markup(text.EscapeMarkup()))
                {
                    Header = new PanelHeader("[bold blue]🤖 Agent[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Blue),
                    Padding = new Padding(1, 0)
                };
                
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }
        }
        
        var separator = new Rule();
        separator.Style = Style.Parse("grey");
        AnsiConsole.Write(separator);
        AnsiConsole.WriteLine();
    }

    public void DisplayAgentResponse(string response)
    {
        AnsiConsole.WriteLine();
        
        // Check if image was generated by looking for the marker OR an image file path
        var imageMatch = System.Text.RegularExpressions.Regex.Match(response, @"\[IMAGE_GENERATED\]Saved to: (.+?)\[/IMAGE_GENERATED\]");
        string? imagePath = null;
        
        if (imageMatch.Success)
        {
            imagePath = imageMatch.Groups[1].Value.Trim();
            // Remove the marker from the response before displaying
            response = System.Text.RegularExpressions.Regex.Replace(response, @"\[IMAGE_GENERATED\].*?\[/IMAGE_GENERATED\]", "").Trim();
        }
        else
        {
            // Look for image paths in the response (e.g., C:\...\images\dalle_*.png)
            var pathMatch = System.Text.RegularExpressions.Regex.Match(response, @"([A-Z]:\\[^<>:""|?*\n]+\\images\\dalle_\d+_\d+\.png)", RegexOptions.IgnoreCase);
            if (pathMatch.Success)
            {
                imagePath = pathMatch.Groups[1].Value.Trim();
            }
        }
        
        if (imagePath != null && File.Exists(imagePath))
        {
            DisplayImageGenerated(imagePath);
        }
        
        // Display agent's text response if there's any content left
        if (!string.IsNullOrWhiteSpace(response))
        {
            var panel = new Panel(new Markup(response.EscapeMarkup()))
            {
                Header = new PanelHeader("[bold blue]🤖 Agent Response[/]", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Padding = new Padding(2, 1)
            };
            
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
    }

    public void DisplayImageGenerated(string localPath)
    {
        AnsiConsole.WriteLine();
        
        var panel = new Panel(new Markup($"[green]🎨 Image Generated and Saved![/]\n[dim]{localPath.EscapeMarkup()}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green),
            Padding = new Padding(2, 1)
        };
        
        AnsiConsole.Write(panel);
        
        // Open in default viewer
        if (File.Exists(localPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = localPath,
                    UseShellExecute = true
                });
                AnsiConsole.MarkupLine("[dim]Opening image in default viewer...[/]");
            }
            catch
            {
                AnsiConsole.MarkupLine("[dim]Could not open image automatically. Please open the file manually.[/]");
            }
        }
        
        AnsiConsole.WriteLine();
    }

    public void DisplayError(string message)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold red]❌ Error:[/] {message.EscapeMarkup()}");
        AnsiConsole.WriteLine();
    }

    public void DisplayGoodbye()
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[cyan1]Goodbye! 👋[/]");
        rule.Style = Style.Parse("cyan1");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
    }

    public void DisplayAttachmentsProcessed(int count)
    {
        if (count > 0)
        {
            AnsiConsole.MarkupLine($"[dim]📎 Attached {count} file(s)[/]");
        }
    }
}

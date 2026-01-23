using Microsoft.Extensions.Logging;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Handles console-based user interface for thread selection and chat interactions.
/// </summary>
public interface IConsoleUI
{
    Task<string> GetUsernameAsync();
    Task<ThreadSelection> GetThreadSelectionAsync(Dictionary<string, string> threads, string username);
    Task<string?> GetFirstMessageAsync();
    Task<string?> GetChatInputAsync(string username);
    void DisplayThreadCreated(string threadId);
    void DisplayThreadLoaded(string threadId);
    void DisplayAgentResponse(string response);
    void DisplayError(string message);
    void DisplayGoodbye();
}

public record ThreadSelection(ThreadSelectionType Type, string? ThreadId = null, string? FirstMessage = null);

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
        Console.Write("Enter your username: ");
        return await Task.FromResult(Console.ReadLine() ?? "User");
    }

    public async Task<ThreadSelection> GetThreadSelectionAsync(Dictionary<string, string> threads, string username)
    {
        Console.WriteLine("\nSelect a thread:");
        Console.WriteLine("  1. [NEW] - Start a new conversation");
        
        int index = 2;
        foreach (var thread in threads)
        {
            var displayTitle = thread.Value.Length > 60 ? thread.Value.Substring(0, 57) + "..." : thread.Value;
            Console.WriteLine($"  {index}. {displayTitle}");
            index++;
        }
        
        Console.WriteLine($"  {index}. [QUIT] - Exit the application");
        
        Console.Write("\nEnter thread number: ");
        var selection = Console.ReadLine() ?? string.Empty;
        
        if (!int.TryParse(selection, out var choice))
        {
            return await Task.FromResult(new ThreadSelection(ThreadSelectionType.Exit));
        }

        if (choice == 1)
        {
            // New thread
            var firstMessage = await GetFirstMessageAsync();
            return new ThreadSelection(ThreadSelectionType.New, FirstMessage: firstMessage);
        }
        else if (choice >= 2 && choice < index)
        {
            // Existing thread
            var selectedThreadId = threads.Keys.ElementAt(choice - 2);
            return new ThreadSelection(ThreadSelectionType.Existing, ThreadId: selectedThreadId);
        }
        else if (choice == index)
        {
            // Exit
            return new ThreadSelection(ThreadSelectionType.Exit);
        }

        return await Task.FromResult(new ThreadSelection(ThreadSelectionType.Exit));
    }

    public async Task<string?> GetFirstMessageAsync()
    {
        Console.Write("First message: ");
        return await Task.FromResult(Console.ReadLine());
    }

    public async Task<string?> GetChatInputAsync(string username)
    {
        Console.WriteLine();
        Console.Write($"{username}> ");
        return await Task.FromResult(Console.ReadLine());
    }

    public void DisplayThreadCreated(string threadId)
    {
        Console.WriteLine($"\nCreated new thread (ID: {threadId})\n");
    }

    public void DisplayThreadLoaded(string threadId)
    {
        Console.WriteLine($"\nLoaded thread (ID: {threadId})\n");
    }

    public void DisplayAgentResponse(string response)
    {
        Console.WriteLine($"\nAgent: {response}\n");
    }

    public void DisplayError(string message)
    {
        Console.WriteLine($"Error: {message}");
    }

    public void DisplayGoodbye()
    {
        Console.WriteLine("Goodbye!");
    }
}

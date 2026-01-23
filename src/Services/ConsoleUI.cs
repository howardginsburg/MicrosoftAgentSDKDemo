using Microsoft.Extensions.Logging;
using MicrosoftAgentSDKDemo.Models;

namespace MicrosoftAgentSDKDemo.Services;

/// <summary>
/// Handles console-based user interface for thread selection and chat interactions.
/// Separated from business logic to maintain clean architecture.
/// </summary>
public interface IConsoleUI
{
    Task<string> GetUsernameAsync();
    Task<ThreadSelection> GetThreadSelectionAsync(List<ThreadDocument> userThreads, string username);
    Task<string?> GetFirstMessageAsync();
    Task<string?> GetChatInputAsync(string username, string threadName);
    void DisplayThreadCreated(string threadName, string threadId);
    void DisplayThreadLoaded(string threadName);
    void DisplayConversationHistory(string username, List<Message> messages);
    void DisplayAgentResponse(string response);
    void DisplayError(string message);
    void DisplayGoodbye();
}

public record ThreadSelection(ThreadSelectionType Type, ThreadDocument? Thread = null, string? FirstMessage = null);

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

    public async Task<ThreadSelection> GetThreadSelectionAsync(List<ThreadDocument> userThreads, string username)
    {
        Console.WriteLine("\nSelect a thread:");
        Console.WriteLine("  1. [NEW] - Start a new conversation");
        
        for (int i = 0; i < userThreads.Count; i++)
        {
            Console.WriteLine($"  {i + 2}. {userThreads[i].ThreadName}");
        }
        
        Console.WriteLine($"  {userThreads.Count + 2}. [QUIT] - Exit the application");
        
        Console.Write("\nEnter thread number: ");
        var selection = Console.ReadLine() ?? string.Empty;
        
        if (!int.TryParse(selection, out var index))
        {
            return await Task.FromResult(new ThreadSelection(ThreadSelectionType.Exit));
        }

        if (index == 1)
        {
            // New thread
            var firstMessage = await GetFirstMessageAsync();
            return new ThreadSelection(ThreadSelectionType.New, FirstMessage: firstMessage);
        }
        else if (index > 1 && index < userThreads.Count + 2)
        {
            // Existing thread
            var selectedThread = userThreads[index - 2];
            return new ThreadSelection(ThreadSelectionType.Existing, Thread: selectedThread);
        }
        else if (index == userThreads.Count + 2)
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

    public async Task<string?> GetChatInputAsync(string username, string threadName)
    {
        Console.WriteLine();
        var prompt = $"{username} [{threadName}]> ";
        Console.Write(prompt);
        return await Task.FromResult(Console.ReadLine());
    }

    public void DisplayThreadCreated(string threadName, string threadId)
    {
        Console.WriteLine($"\nCreated new thread: {threadName} (ID: {threadId})\n");
    }

    public void DisplayThreadLoaded(string threadName)
    {
        Console.WriteLine($"\nLoaded thread: {threadName}\n");
    }

    public void DisplayConversationHistory(string username, List<Message> messages)
    {
        if (messages.Count == 0)
            return;

        Console.WriteLine("--- Conversation History ---");
        foreach (var msg in messages)
        {
            if (msg.Role == "user")
            {
                Console.WriteLine($"{username}: {msg.Content}");
            }
            else if (msg.Role == "assistant")
            {
                Console.WriteLine($"Agent: {msg.Content}");
            }
        }
        Console.WriteLine("--- End of History ---\n");
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

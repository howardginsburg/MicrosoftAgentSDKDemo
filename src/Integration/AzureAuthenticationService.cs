using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace MicrosoftAgentSDKDemo.Integration;

/// <summary>
/// Handles Azure CLI authentication verification and user information extraction.
/// </summary>
public interface IAzureAuthenticationService
{
    Task<bool> VerifyAndDisplayAuthenticationAsync();
}

public class AzureAuthenticationService : IAzureAuthenticationService
{
    private readonly ILogger<AzureAuthenticationService> _logger;

    public AzureAuthenticationService(ILogger<AzureAuthenticationService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> VerifyAndDisplayAuthenticationAsync()
    {
        try
        {
            var credential = new AzureCliCredential();
            
            // Get the authenticated account username from token
            var userName = await GetAuthenticatedUserNameAsync(credential);
            
            // Use Azure Resource Manager to validate authentication and get tenant info
            var armClient = new ArmClient(credential);
            var subscriptions = armClient.GetSubscriptions();
            
            // Attempt to get first subscription to validate authentication
            Azure.ResourceManager.Resources.SubscriptionResource? subscription = null;
            await foreach (var sub in subscriptions)
            {
                subscription = sub;
                break;
            }
            
            // Display authentication status
            DisplayAuthenticationStatus(userName, subscription);
            
            _logger.LogDebug("Azure CLI authenticated | User: {User} | Subscription: {Subscription}", 
                userName ?? "unknown", subscription?.Data.DisplayName ?? "unknown");
            
            return true;
        }
        catch (Azure.Identity.CredentialUnavailableException)
        {
            DisplayAuthenticationError();
            _logger.LogError("Azure CLI not authenticated. Application requires 'az login'.");
            return false;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
        {
            DisplayAuthenticationError();
            _logger.LogError("Azure CLI not authenticated. Application requires 'az login'.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify Azure CLI authentication status");
            return true; // Continue anyway
        }
    }

    private async Task<string?> GetAuthenticatedUserNameAsync(AzureCliCredential credential)
    {
        try
        {
            var tokenRequestContext = new Azure.Core.TokenRequestContext(new[] { "https://management.azure.com/.default" });
            var token = await credential.GetTokenAsync(tokenRequestContext, default);
            
            return ParseUserNameFromToken(token.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get token for user information");
            return null;
        }
    }

    private string? ParseUserNameFromToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;
            
            var payload = parts[1];
            
            // Fix base64 padding
            var remainder = payload.Length % 4;
            if (remainder > 0)
            {
                payload += new string('=', 4 - remainder);
            }
            
            var jsonBytes = Convert.FromBase64String(payload);
            var tokenJson = System.Text.Json.JsonDocument.Parse(jsonBytes);
            
            // Try different user claim properties
            if (tokenJson.RootElement.TryGetProperty("upn", out var upn))
            {
                return upn.GetString();
            }
            else if (tokenJson.RootElement.TryGetProperty("unique_name", out var uniqueName))
            {
                return uniqueName.GetString();
            }
            else if (tokenJson.RootElement.TryGetProperty("email", out var email))
            {
                return email.GetString();
            }
            
            _logger.LogDebug("Token claims parsed but user not found in token");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse JWT token for user information");
            return null;
        }
    }

    private void DisplayAuthenticationStatus(string? userName, Azure.ResourceManager.Resources.SubscriptionResource? subscription)
    {
        if (!string.IsNullOrEmpty(userName))
        {
            AnsiConsole.MarkupLine($"[dim]✓ Authenticated as [cyan]{userName.EscapeMarkup()}[/][/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]✓ Azure CLI authenticated[/]");
        }
        
        if (subscription != null)
        {
            AnsiConsole.MarkupLine($"[dim]  Subscription: [cyan]{subscription.Data.DisplayName}[/][/]");
            
            // Get tenant info if available
            var credential = new AzureCliCredential();
            var armClient = new ArmClient(credential);
            var tenants = armClient.GetTenants();
            
            Azure.ResourceManager.Resources.TenantResource? tenant = null;
            foreach (var t in tenants)
            {
                tenant = t;
                break;
            }
            
            if (tenant != null)
            {
                AnsiConsole.MarkupLine($"[dim]  Tenant: [cyan]{tenant.Data.TenantId}[/][/]");
            }
        }
        
        AnsiConsole.WriteLine();
    }

    private void DisplayAuthenticationError()
    {
        AnsiConsole.MarkupLine("[red]✗ Azure CLI authentication failed[/]");
        AnsiConsole.MarkupLine("[yellow]Please run 'az login' to authenticate with Azure before starting the application.[/]");
        AnsiConsole.WriteLine();
    }
}

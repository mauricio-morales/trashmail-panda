using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Models;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Shared.Security;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// Interactive configuration wizard for first-time setup and provider reconfiguration
/// Uses Spectre.Console for step-by-step guided configuration experience
/// </summary>
public class ConfigurationWizard
{
    private readonly ISecureStorageManager _secureStorage;
    private readonly IGoogleOAuthHandler _oauthHandler;
    private readonly ConsoleStatusDisplay _statusDisplay;
    private readonly ILogger<ConfigurationWizard> _logger;
    private readonly ConfigurationWizardState _state;

    public ConfigurationWizard(
        ISecureStorageManager secureStorage,
        IGoogleOAuthHandler oauthHandler,
        ConsoleStatusDisplay statusDisplay,
        ILogger<ConfigurationWizard> logger)
    {
        _secureStorage = secureStorage;
        _oauthHandler = oauthHandler;
        _statusDisplay = statusDisplay;
        _logger = logger;
        _state = new ConfigurationWizardState();
    }

    /// <summary>
    /// Run the complete configuration wizard flow
    /// </summary>
    /// <returns>True if configuration completed successfully, false if user cancelled</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting configuration wizard");

        try
        {
            // Storage is always auto-configured — no user input required
            AutoConfigureStorage();

            // Check which providers are already configured
            var gmailConfigured = await IsGmailConfiguredAsync();

            // Step 1: Welcome screen with status
            await DisplayWelcomeAsync(gmailConfigured);
            _state.CurrentStep = WizardStep.Welcome;

            // Step 2: Gmail OAuth setup (conditional)
            _state.CurrentStep = WizardStep.GmailSetup;
            if (gmailConfigured)
            {
                _logger.LogInformation("Gmail already configured and healthy, skipping setup");
                _state.GmailConfigured = true;
            }
            else
            {
                if (!await ConfigureGmailAsync(cancellationToken))
                {
                    _logger.LogWarning("User cancelled configuration at Gmail setup");
                    return false;
                }
            }

            // Step 4: Confirmation screen
            _state.CurrentStep = WizardStep.Confirmation;
            await DisplayConfirmationAsync();

            // Step 5: Persist configurations
            await SaveConfigurationsAsync();

            // Step 6: Mark as complete
            _state.CurrentStep = WizardStep.Complete;
            _logger.LogInformation("Configuration wizard completed successfully");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration wizard failed with exception");
            _state.Errors.Add($"Unexpected error: {ex.Message}");
            AnsiConsole.MarkupLine("[red]✗ Configuration failed. Please try again.[/]");
            return false;
        }
    }

    /// <summary>
    /// Display welcome screen with setup overview
    /// </summary>
    private async Task DisplayWelcomeAsync(bool gmailConfigured)
    {
        AnsiConsole.Clear();

        // Welcome banner
        var rule = new Rule("[bold cyan]🦝 Welcome to TrashMail Panda Setup[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Setup overview
        AnsiConsole.MarkupLine("[bold]First-time setup wizard[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("This wizard will guide you through configuring:");

        // Gmail status
        if (gmailConfigured)
        {
            AnsiConsole.MarkupLine("  [green]✓[/] [cyan]1.[/] Gmail integration [dim](already configured)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("  [cyan]1.[/] Gmail integration (OAuth authentication)");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Storage is configured automatically — no setup required.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press Enter to continue...[/]");

        System.Console.ReadLine();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Auto-configures storage using the OS-standard path with mandatory encryption.
    /// No user input is required — the database location is determined by the OS.
    /// </summary>
    private void AutoConfigureStorage()
    {
        var dbPath = StorageProviderConfig.GetOsDefaultPath();
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _state.StorageConfigured = true;
        _logger.LogInformation("Storage auto-configured at OS-standard path: {Path}", dbPath);
    }

    /// <summary>
    /// Configure Gmail OAuth credentials and perform authentication
    /// </summary>
    private async Task<bool> ConfigureGmailAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold cyan]Step 1: Gmail OAuth Setup[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Check if OAuth client credentials already exist
        var existingClientId = await _secureStorage.RetrieveCredentialAsync("gmail_client_id");
        var existingClientSecret = await _secureStorage.RetrieveCredentialAsync("gmail_client_secret");

        var hasExistingCredentials = existingClientId.IsSuccess && !string.IsNullOrEmpty(existingClientId.Value) &&
                                     existingClientSecret.IsSuccess && !string.IsNullOrEmpty(existingClientSecret.Value);

        string clientId;
        string clientSecret;

        if (hasExistingCredentials)
        {
            // Credentials exist - use them automatically for OAuth flow
            clientId = existingClientId.Value!;
            clientSecret = existingClientSecret.Value!;

            AnsiConsole.MarkupLine("[green]✓[/] Using existing OAuth client credentials");
            AnsiConsole.MarkupLine($"[dim]Client ID:[/] {clientId.Substring(0, Math.Min(20, clientId.Length))}...");
            AnsiConsole.WriteLine();

            _logger.LogInformation("Using existing OAuth credentials for authentication");
        }
        else
        {
            // No existing credentials - show setup instructions and prompt
            AnsiConsole.MarkupLine("[bold]To configure Gmail access, you need OAuth 2.0 credentials:[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]1. Go to[/] [link=https://console.cloud.google.com/]Google Cloud Console[/]");
            AnsiConsole.MarkupLine("[dim]2. Create a new project or select existing one[/]");
            AnsiConsole.MarkupLine("[dim]3. Enable the Gmail API[/]");
            AnsiConsole.MarkupLine("[dim]4. Create OAuth 2.0 credentials (Desktop application type)[/]");
            AnsiConsole.MarkupLine("[dim]5. Download the client ID and client secret[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]For detailed setup instructions, see:[/]");
            AnsiConsole.MarkupLine("[link]docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md[/]");
            AnsiConsole.WriteLine();

            (clientId, clientSecret) = await PromptForOAuthCredentialsAsync();

            // Store OAuth credentials in secure storage
            await _secureStorage.StoreCredentialAsync("gmail_client_id", clientId);
            await _secureStorage.StoreCredentialAsync("gmail_client_secret", clientSecret);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✓ OAuth credentials stored securely[/]");
            AnsiConsole.WriteLine();
        }

        // Perform OAuth flow to get access tokens
        AnsiConsole.MarkupLine("[bold cyan]Initiating OAuth authentication flow...[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]A browser window will open for you to authorize TrashMail Panda.[/]");
        AnsiConsole.MarkupLine("[dim]After authorization, you'll be redirected back to this application.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Press Enter to open your browser...[/]");
        System.Console.ReadLine();

        // Build OAuth configuration
        var oauthConfig = new OAuthConfiguration
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = "http://localhost:8080/oauth/callback",
            Scopes = new[] { "https://www.googleapis.com/auth/gmail.modify" }
        };

        // Run OAuth flow (GoogleOAuthHandler manages its own console output)
        var authResult = await _oauthHandler.AuthenticateAsync(oauthConfig, cancellationToken);

        if (!authResult.IsSuccess)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]✗ OAuth authentication failed: {authResult.Error.Message}[/]");
            _logger.LogError("OAuth authentication failed: {Error}", authResult.Error.Message);
            _state.Errors.Add($"Gmail OAuth: {authResult.Error.Message}");

            var retry = AnsiConsole.Confirm("[yellow]Would you like to retry?[/]", defaultValue: true);
            if (retry)
            {
                return await ConfigureGmailAsync(cancellationToken);
            }
            return false;
        }

        // Display authenticated account
        var userEmail = authResult.Value.UserEmail ?? "Unknown";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓ Successfully authenticated as:[/] [bold]{userEmail}[/]");

        _state.GmailConfigured = true;
        _logger.LogInformation("Gmail configured successfully for user: {Email}", userEmail);

        await Task.Delay(1500, cancellationToken);
        return true;
    }

    /// <summary>
    /// Display confirmation screen showing all configured providers
    /// </summary>
    private async Task DisplayConfirmationAsync()
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold green]✓ Configuration Complete[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]The following providers have been configured:[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("  [green]✓[/] Storage Provider [dim](auto-configured)[/]");

        // Gmail status
        if (_state.GmailConfigured)
        {
            AnsiConsole.MarkupLine("  [green]✓[/] Gmail Provider");
        }
        else
        {
            AnsiConsole.MarkupLine("  [red]✗[/] Gmail Provider");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]Starting provider initialization...[/]");
        AnsiConsole.WriteLine();

        await Task.Delay(1500);
    }

    /// <summary>
    /// Save all configurations to secure storage and appsettings.json
    /// </summary>
    private async Task SaveConfigurationsAsync()
    {
        _logger.LogInformation("Saving configurations to secure storage");

        // Mark first-time setup as complete
        await _secureStorage.StoreCredentialAsync("setup_completed", DateTime.UtcNow.ToString("O"));

        _logger.LogInformation("Configurations saved successfully");
    }

    /// <summary>
    /// Check if Gmail provider is fully configured (credentials AND access tokens)
    /// </summary>
    private async Task<bool> IsGmailConfiguredAsync()
    {
        try
        {
            // Check OAuth client credentials (from Google Cloud Console)
            var clientId = await _secureStorage.RetrieveCredentialAsync("gmail_client_id");
            var clientSecret = await _secureStorage.RetrieveCredentialAsync("gmail_client_secret");

            var hasCredentials = clientId.IsSuccess && !string.IsNullOrEmpty(clientId.Value) &&
                                clientSecret.IsSuccess && !string.IsNullOrEmpty(clientSecret.Value);

            if (!hasCredentials)
            {
                return false;
            }

            // Check OAuth access tokens (from authentication flow)
            // Refresh token is the critical one - it's used to get new access tokens
            var refreshToken = await _secureStorage.RetrieveCredentialAsync("gmail_refresh_token");
            var hasTokens = refreshToken.IsSuccess && !string.IsNullOrEmpty(refreshToken.Value);

            // Gmail is only fully configured if we have BOTH credentials AND access tokens
            return hasTokens;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Prompt user to reconfigure or skip an already-configured provider
    /// </summary>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>True if user wants to reconfigure, false to skip.</returns>
    private async Task<bool> PromptReconfigureOrSkipAsync(string providerName)
    {
        AnsiConsole.Clear();

        var rule = new Rule($"[bold green]{providerName} Already Configured[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[green]✓[/] {providerName} provider is already configured.");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[cyan]What would you like to do?[/]")
                .AddChoices(
                    "Skip (keep current configuration)",
                    "Reconfigure (replace existing configuration)"));

        await Task.CompletedTask;
        return choice.StartsWith("Reconfigure");
    }

    /// <summary>
    /// Prompt user for OAuth client credentials (Client ID and Client Secret)
    /// </summary>
    /// <returns>Tuple of (clientId, clientSecret)</returns>
    private async Task<(string clientId, string clientSecret)> PromptForOAuthCredentialsAsync()
    {
        // Prompt for Client ID
        var clientId = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Gmail OAuth Client ID:[/]")
                .Validate(id =>
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return ValidationResult.Error("Client ID cannot be empty");
                    }
                    if (!id.EndsWith(".apps.googleusercontent.com"))
                    {
                        return ValidationResult.Error("Client ID should end with .apps.googleusercontent.com");
                    }
                    return ValidationResult.Success();
                }));

        // Prompt for Client Secret
        var clientSecret = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Gmail OAuth Client Secret:[/]")
                .Secret()
                .Validate(secret =>
                {
                    if (string.IsNullOrWhiteSpace(secret))
                    {
                        return ValidationResult.Error("Client Secret cannot be empty");
                    }
                    return ValidationResult.Success();
                }));

        await Task.CompletedTask;
        return (clientId, clientSecret);
    }
}

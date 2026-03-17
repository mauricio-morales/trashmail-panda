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
            // Step 1: Welcome screen
            await DisplayWelcomeAsync();
            _state.CurrentStep = WizardStep.Welcome;

            // Step 2: Storage configuration
            _state.CurrentStep = WizardStep.StorageSetup;
            if (!await ConfigureStorageAsync(cancellationToken))
            {
                _logger.LogWarning("User cancelled configuration at storage setup");
                return false;
            }

            // Step 3: Gmail OAuth setup
            _state.CurrentStep = WizardStep.GmailSetup;
            if (!await ConfigureGmailAsync(cancellationToken))
            {
                _logger.LogWarning("User cancelled configuration at Gmail setup");
                return false;
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
    private async Task DisplayWelcomeAsync()
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
        AnsiConsole.MarkupLine("  [cyan]1.[/] Storage settings (database location and encryption)");
        AnsiConsole.MarkupLine("  [cyan]2.[/] Gmail integration (OAuth authentication)");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press Enter to continue...[/]");

        System.Console.ReadLine();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Configure storage provider settings
    /// </summary>
    private async Task<bool> ConfigureStorageAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold cyan]Step 1: Storage Configuration[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Prompt for database path
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrashMailPanda",
            "app.db");

        var databasePath = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Database location:[/]")
                .DefaultValue(defaultPath)
                .ShowDefaultValue(true)
                .Validate(path =>
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(path);
                        if (string.IsNullOrEmpty(directory))
                        {
                            return ValidationResult.Error("Invalid path");
                        }
                        return ValidationResult.Success();
                    }
                    catch
                    {
                        return ValidationResult.Error("Invalid path format");
                    }
                }));

        // Create directory if it doesn't exist
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created database directory: {Directory}", directory);
        }

        // Prompt for encryption option
        var useEncryption = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Enable database encryption?[/]")
                .AddChoices("Yes (Recommended)", "No"));

        var enableEncryption = useEncryption == "Yes (Recommended)";

        // Store configuration in secure storage
        await _secureStorage.StoreCredentialAsync("storage_database_path", databasePath);
        await _secureStorage.StoreCredentialAsync("storage_encryption_enabled", enableEncryption.ToString());

        _state.StorageConfigured = true;
        _logger.LogInformation("Storage configured: Path={Path}, Encryption={Encryption}", databasePath, enableEncryption);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Storage configured successfully[/]");
        await Task.Delay(1000, cancellationToken);

        return true;
    }

    /// <summary>
    /// Configure Gmail OAuth credentials and perform authentication
    /// </summary>
    private async Task<bool> ConfigureGmailAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold cyan]Step 2: Gmail OAuth Setup[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Display OAuth instructions
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

        // Store OAuth credentials in secure storage
        await _secureStorage.StoreCredentialAsync("gmail_client_id", clientId);
        await _secureStorage.StoreCredentialAsync("gmail_client_secret", clientSecret);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ OAuth credentials stored securely[/]");
        AnsiConsole.WriteLine();

        // Perform OAuth flow
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

        // Show spinner during OAuth flow
        var authResult = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[cyan]Waiting for authorization...[/]", async ctx =>
            {
                return await _oauthHandler.AuthenticateAsync(oauthConfig, cancellationToken);
            });

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

        // Storage status
        if (_state.StorageConfigured)
        {
            AnsiConsole.MarkupLine("  [green]✓[/] Storage Provider");
        }
        else
        {
            AnsiConsole.MarkupLine("  [red]✗[/] Storage Provider");
        }

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
}

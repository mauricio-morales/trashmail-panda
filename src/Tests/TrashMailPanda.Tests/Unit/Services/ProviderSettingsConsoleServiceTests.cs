using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spectre.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services.Console;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class ProviderSettingsConsoleServiceTests
{
    private readonly Mock<IEmailArchiveService> _archiveService = new();

    private static StorageQuota MakeQuota(long currentBytes = 1_073_741_824L, long limitBytes = 10_737_418_240L,
        long archiveCount = 5, long featureCount = 10, long userCorrectedCount = 2) =>
        new StorageQuota
        {
            LimitBytes = limitBytes,
            CurrentBytes = currentBytes,
            FeatureBytes = currentBytes / 2,
            ArchiveBytes = currentBytes / 2,
            FeatureCount = featureCount,
            ArchiveCount = archiveCount,
            UserCorrectedCount = userCorrectedCount,
        };

    private (ProviderSettingsConsoleService Service, StringWriter Writer) CreateService(
        Queue<ConsoleKeyInfo> keyQueue,
        Func<CancellationToken, Task<bool>>? runWizard = null)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        // Provide a default no-op wizard so non-reauth tests don't need a real wizard
        runWizard ??= _ => Task.FromResult(false);

        // ConfigurationWizard is not used when runWizard is injected
        ConfigurationWizard? wizard = null!;

        var service = new ProviderSettingsConsoleService(
            wizard!,
            _archiveService.Object,
            NullLogger<ProviderSettingsConsoleService>.Instance,
            console,
            readKey: () => keyQueue.Count > 0
                ? keyQueue.Dequeue()
                : new ConsoleKeyInfo((char)0, ConsoleKey.Q, false, false, false),
            runWizard: runWizard);

        return (service, writer);
    }

    // ── Menu exits on Q ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ExitsImmediately_WhenQPressed()
    {
        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys);

        var result = await service.RunAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task RunAsync_ExitsImmediately_WhenEscapePressed()
    {
        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo((char)0, ConsoleKey.Escape, false, false, false));

        var (service, writer) = CreateService(keys);

        var result = await service.RunAsync();

        Assert.True(result.IsSuccess);
    }

    // ── Storage stats display (T052) ─────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Option2_RendersStorageStats()
    {
        _archiveService.Setup(x => x.GetStorageUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StorageQuota>.Success(MakeQuota(
                currentBytes: 1_073_741_824L,   // 1 GB
                limitBytes: 10_737_418_240L,     // 10 GB
                archiveCount: 42,
                featureCount: 100,
                userCorrectedCount: 5)));

        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys);
        await service.RunAsync();

        var output = writer.ToString();
        Assert.Contains("42", output);   // Archive count
        Assert.Contains("100", output);  // Feature vector count
        Assert.Contains("5", output);    // User-corrected count
        Assert.Contains("GB", output);   // GB labels
    }

    [Fact]
    public async Task RunAsync_Option2_ShowsError_WhenStorageServiceFails()
    {
        _archiveService.Setup(x => x.GetStorageUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StorageQuota>.Failure(new StorageError("DB unavailable")));

        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys);
        await service.RunAsync();

        var output = writer.ToString();
        Assert.Contains("DB unavailable", output);
    }

    // ── Gmail re-auth delegates to wizard (T051) ──────────────────────────────

    [Fact]
    public async Task RunAsync_Option1_DelegatesGmailReauthToWizard()
    {
        var wizardCalled = false;
        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys, runWizard: ct =>
        {
            wizardCalled = true;
            return Task.FromResult(true);
        });

        await service.RunAsync();

        Assert.True(wizardCalled, "ConfigurationWizard.RunAsync should have been called");
    }

    [Fact]
    public async Task RunAsync_Option1_ShowsSuccess_WhenWizardReturnsTrue()
    {
        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys, runWizard: _ => Task.FromResult(true));

        await service.RunAsync();

        var output = writer.ToString();
        Assert.Contains("successfully", output);
    }

    [Fact]
    public async Task RunAsync_Option1_ShowsFailure_WhenWizardReturnsFalse()
    {
        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys, runWizard: _ => Task.FromResult(false));

        await service.RunAsync();

        var output = writer.ToString();
        Assert.Contains("cancelled", output);
    }

    // ── Storage limit update (T053) ───────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Option3_UpdatesStorageLimit_OnValidInput()
    {
        long? capturedBytes = null;
        _archiveService
            .Setup(x => x.UpdateStorageLimitAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<long, CancellationToken>((b, _) => capturedBytes = b)
            .ReturnsAsync(Result<bool>.Success(true));

        // Simulate user entering "20\n" via Console.ReadLine
        // The service reads from Console.ReadLine() so we use a custom TextReader
        var originalIn = System.Console.In;
        System.Console.SetIn(new System.IO.StringReader("20\n"));

        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys);

        try
        {
            await service.RunAsync();
        }
        finally
        {
            System.Console.SetIn(originalIn);
        }

        _archiveService.Verify(x => x.UpdateStorageLimitAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedBytes);
        // 20 GB = 20 * 1_073_741_824 bytes
        Assert.Equal(20L * 1_073_741_824L, capturedBytes!.Value);
    }

    [Fact]
    public async Task RunAsync_Option3_ShowsError_WhenUpdateFails()
    {
        _archiveService
            .Setup(x => x.UpdateStorageLimitAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new StorageError("Write failed")));

        var originalIn = System.Console.In;
        System.Console.SetIn(new System.IO.StringReader("10\n"));

        var keys = new Queue<ConsoleKeyInfo>();
        keys.Enqueue(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        keys.Enqueue(new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false));

        var (service, writer) = CreateService(keys);

        try
        {
            await service.RunAsync();
        }
        finally
        {
            System.Console.SetIn(originalIn);
        }

        var output = writer.ToString();
        Assert.Contains("Write failed", output);
    }
}

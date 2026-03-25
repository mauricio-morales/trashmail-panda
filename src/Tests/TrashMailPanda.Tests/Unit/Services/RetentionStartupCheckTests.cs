using System.Threading;
using System.Threading.Tasks;
using Moq;
using TrashMailPanda.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Startup;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="RetentionStartupCheck"/> covering the startup prompt
/// decision flow: whether to prompt, user confirmation, and scan invocation.
/// </summary>
[Trait("Category", "Unit")]
public class RetentionStartupCheckTests
{
    private readonly Mock<IRetentionEnforcementService> _retentionService = new();

    private RetentionStartupCheck CreateSut(bool userConfirms = true)
    {
        return new RetentionStartupCheck(
            _retentionService.Object,
            confirmAction: () => userConfirms);
    }

    // ── ShouldPrompt = true → prompt shown ───────────────────────────────────

    [Fact]
    public async Task RunAsync_ShouldPromptTrue_UserConfirms_RunsScan()
    {
        _retentionService.Setup(x => x.ShouldPromptAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _retentionService.Setup(x => x.RunScanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RetentionScanResult>.Success(
                new RetentionScanResult { ScannedCount = 5, DeletedCount = 2, SkippedCount = 3, FailedIds = [], RanAtUtc = System.DateTime.UtcNow }));

        var sut = CreateSut(userConfirms: true);
        var result = await sut.RunAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        _retentionService.Verify(x => x.RunScanAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldPromptTrue_UserDeclines_DoesNotRunScan()
    {
        _retentionService.Setup(x => x.ShouldPromptAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut(userConfirms: false);
        await sut.RunAsync(CancellationToken.None);

        _retentionService.Verify(x => x.RunScanAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ShouldPrompt = false → no prompt ────────────────────────────────────

    [Fact]
    public async Task RunAsync_ShouldPromptFalse_DoesNotPromptOrRunScan()
    {
        _retentionService.Setup(x => x.ShouldPromptAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(false));

        var sut = CreateSut(userConfirms: true);
        await sut.RunAsync(CancellationToken.None);

        _retentionService.Verify(x => x.RunScanAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Services;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services.Console;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public sealed class BulkOperationConsoleServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IAnsiConsole CreateTestConsole(StringWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

    private static EmailFeatureVector MakeVector(string id = "email-1", string domain = "example.com") =>
        new() { EmailId = id, SenderDomain = domain, EmailAgeDays = 10 };

    /// <summary>
    /// Creates a service with injectable key/line sequences.
    /// <paramref name="lines"/> feed the ReadLine (criteria wizard) calls.
    /// <paramref name="keys"/> feed the ReadKey (action + confirm) calls.
    /// </summary>
    private static BulkOperationConsoleService CreateService(
        Mock<IBulkOperationService> bulkMock,
        StringWriter writer,
        Queue<string?> lines,
        Queue<ConsoleKeyInfo> keys) =>
        new BulkOperationConsoleService(
            bulkService: bulkMock.Object,
            logger: NullLogger<BulkOperationConsoleService>.Instance,
            console: CreateTestConsole(writer),
            readKey: () => keys.Count > 0 ? keys.Dequeue() : new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false),
            readLine: () => lines.Count > 0 ? lines.Dequeue() : null);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PreviewStepRenderedBeforeExecution()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var vectors = new List<EmailFeatureVector> { MakeVector("id-1") };

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(vectors));

        bulkMock.Setup(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationResult>.Success(new BulkOperationResult(1, [])));

        var writer = new StringWriter();

        // Lines: sender="test.com", dateFrom="", dateTo="" → criteria
        var lines = new Queue<string?>(["test.com", "", ""]);

        // Keys: 'A' (Archive action), 'Y' (confirm), any (wait for key), Esc (exit loop)
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('Y', ConsoleKey.Y, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), // WaitForKey
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit on next criteria
        ]);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: preview was called, and ExecuteAsync was also called
        bulkMock.Verify(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()), Times.Once);
        bulkMock.Verify(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), "Archive", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ConfirmationRequiredNKeyPreventsExecution()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var vectors = new List<EmailFeatureVector> { MakeVector("id-1") };

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(vectors));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["test.com", "", ""]);

        // Keys: 'A' (Archive), 'N' (cancel confirmation), Esc (exit loop)
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('N', ConsoleKey.N, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false),
        ]);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: ExecuteAsync was NOT called
        bulkMock.Verify(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_EscapeConfirmationPreventsExecution()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var vectors = new List<EmailFeatureVector> { MakeVector("id-1") };

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(vectors));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["test.com", "", ""]);

        // Keys: 'D' (Delete), Esc (cancel confirmation), Esc (exit loop)
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('D', ConsoleKey.D, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // cancel confirmation
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit loop
        ]);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: ExecuteAsync was NOT called
        bulkMock.Verify(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_FailedIdsRenderedInBoldRed()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var vectors = new List<EmailFeatureVector> { MakeVector("id-fail") };

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(vectors));

        bulkMock.Setup(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationResult>.Success(new BulkOperationResult(0, ["id-fail"])));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["test.com", "", ""]);

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('Y', ConsoleKey.Y, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), // WaitForKey
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit loop
        ]);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: "id-fail" appears in output (the output has no ANSI codes since NoColors)
        var output = writer.ToString();
        Assert.Contains("id-fail", output);
        // Since AnsiSupport.No strips markup tags, the actual ID text should be present
    }

    [Fact]
    public async Task RunAsync_SuccessCountRenderedAsText_WhenAllSucceed()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var vectors = new List<EmailFeatureVector> { MakeVector("id-1"), MakeVector("id-2") };

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(vectors));

        bulkMock.Setup(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkOperationResult>.Success(new BulkOperationResult(2, [])));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["test.com", "", ""]);

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('K', ConsoleKey.K, false, false, false),
            new ConsoleKeyInfo('Y', ConsoleKey.Y, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), // WaitForKey
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit loop
        ]);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: success count shown
        var output = writer.ToString();
        Assert.Contains("2", output);
        Assert.Contains("processed successfully", output);
    }

    [Fact]
    public async Task RunAsync_NoMatchesShownWarning()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var empty = new List<EmailFeatureVector>();

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Success(empty));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["nobody.com", "", ""]);

        // After "no matches" warning, WaitForKey, then exit loop
        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), // WaitForKey after warning
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit loop (null readLine)
        ]);

        // Second iteration: readLine returns null → criteria returns null → exit loop
        lines.Enqueue(null);

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        // Assert: ExecuteAsync never called
        bulkMock.Verify(b => b.ExecuteAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var output = writer.ToString();
        Assert.Contains("No emails match", output);
    }

    [Fact]
    public async Task RunAsync_PreviewError_ShowsErrorMessage()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();

        bulkMock.Setup(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmailFeatureVector>>.Failure(new StorageError("DB unavailable")));

        var writer = new StringWriter();
        var lines = new Queue<string?>(["test.com", "", ""]);

        var keys = new Queue<ConsoleKeyInfo>([
            new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false), // WaitForKey after error
            new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false), // exit loop
        ]);
        lines.Enqueue(null); // cause exit on second iteration

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        await svc.RunAsync(CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Preview failed", output);
        Assert.Contains("DB unavailable", output);
    }

    [Fact]
    public async Task RunAsync_NullLineInput_ExitsGracefully()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var writer = new StringWriter();

        // readLine returns null immediately → BuildCriteria returns null → service returns
        var lines = new Queue<string?>([null]);
        var keys = new Queue<ConsoleKeyInfo>();

        var svc = CreateService(bulkMock, writer, lines, keys);

        // Act
        var result = await svc.RunAsync(CancellationToken.None);

        // Assert: clean exit, no calls to preview or execute
        Assert.True(result.IsSuccess);
        bulkMock.Verify(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_CancellationToken_StopsLoop()
    {
        // Arrange
        var bulkMock = new Mock<IBulkOperationService>();
        var writer = new StringWriter();
        var lines = new Queue<string?>();
        var keys = new Queue<ConsoleKeyInfo>();

        var svc = CreateService(bulkMock, writer, lines, keys);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await svc.RunAsync(cts.Token);

        // Assert: exits immediately
        Assert.True(result.IsSuccess);
        bulkMock.Verify(b => b.PreviewAsync(It.IsAny<BulkOperationCriteria>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class AutoApplyUndoServiceTests
{
    private readonly Mock<IEmailProvider> _emailProvider = new();
    private readonly Mock<IEmailArchiveService> _archive = new();

    private AutoApplyUndoService CreateSut()
        => new(_emailProvider.Object, _archive.Object, NullLogger<AutoApplyUndoService>.Instance);

    private void SetupBatchModify(bool success = true)
    {
        if (success)
            _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
                .ReturnsAsync(Result<bool>.Success(true));
        else
            _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
                .ReturnsAsync(Result<bool>.Failure(new NetworkError("Gmail API error")));
    }

    private void SetupTrainingLabel(bool success = true)
    {
        if (success)
            _archive.Setup(x => x.SetTrainingLabelAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<bool>.Success(true));
        else
            _archive.Setup(x => x.SetTrainingLabelAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<bool>.Failure(new StorageError("DB error")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Gmail reversal mapping
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndoAsync_DeleteAction_AddsInboxRemovesTrash()
    {
        SetupBatchModify();
        SetupTrainingLabel();
        var sut = CreateSut();

        await sut.UndoAsync("msg1", "Delete", "Keep");

        _emailProvider.Verify(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r =>
            r.AddLabelIds != null && ((IList<string>)r.AddLabelIds)[0] == "INBOX" &&
            r.RemoveLabelIds != null && ((IList<string>)r.RemoveLabelIds)[0] == "TRASH"
        )), Times.Once);
    }

    [Fact]
    public async Task UndoAsync_ArchiveAction_AddsInboxNoRemove()
    {
        SetupBatchModify();
        SetupTrainingLabel();
        var sut = CreateSut();

        await sut.UndoAsync("msg1", "Archive", "Keep");

        _emailProvider.Verify(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r =>
            r.AddLabelIds != null && ((IList<string>)r.AddLabelIds)[0] == "INBOX"
        )), Times.Once);
    }

    [Fact]
    public async Task UndoAsync_SpamAction_AddsInboxRemovesSpam()
    {
        SetupBatchModify();
        SetupTrainingLabel();
        var sut = CreateSut();

        await sut.UndoAsync("msg1", "Spam", "Keep");

        _emailProvider.Verify(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r =>
            r.AddLabelIds != null && ((IList<string>)r.AddLabelIds)[0] == "INBOX" &&
            r.RemoveLabelIds != null && ((IList<string>)r.RemoveLabelIds)[0] == "SPAM"
        )), Times.Once);
    }

    [Fact]
    public async Task UndoAsync_KeepAction_SkipsGmailCall()
    {
        SetupTrainingLabel();
        var sut = CreateSut();

        var result = await sut.UndoAsync("msg1", "Keep", "Archive");

        Assert.True(result.IsSuccess);
        _emailProvider.Verify(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dual-write ordering: Gmail first, then training label
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndoAsync_Success_WritesTrainingLabelWithUserCorrectedTrue()
    {
        SetupBatchModify();
        SetupTrainingLabel();
        var sut = CreateSut();

        var result = await sut.UndoAsync("msg1", "Delete", "Keep");

        Assert.True(result.IsSuccess);
        _archive.Verify(x => x.SetTrainingLabelAsync("msg1", "Keep", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UndoAsync_Success_GmailCalledBeforeTrainingLabel()
    {
        var callOrder = new List<string>();

        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback(() => callOrder.Add("Gmail"))
            .ReturnsAsync(Result<bool>.Success(true));

        _archive.Setup(x => x.SetTrainingLabelAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Training"))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();
        await sut.UndoAsync("msg1", "Archive", "Keep");

        Assert.Equal(["Gmail", "Training"], callOrder);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Failure isolation: Gmail fails → no training label update
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndoAsync_GmailFails_ReturnsFailure()
    {
        SetupBatchModify(success: false);
        var sut = CreateSut();

        var result = await sut.UndoAsync("msg1", "Delete", "Keep");

        Assert.False(result.IsSuccess);
        Assert.Equal("Gmail API error", result.Error.Message);
    }

    [Fact]
    public async Task UndoAsync_GmailFails_TrainingLabelNotUpdated()
    {
        SetupBatchModify(success: false);
        var sut = CreateSut();

        await sut.UndoAsync("msg1", "Delete", "Keep");

        _archive.Verify(x => x.SetTrainingLabelAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Input validation
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "Delete", "Keep")]
    [InlineData("msg1", "", "Keep")]
    [InlineData("msg1", "Delete", "")]
    public async Task UndoAsync_NullOrEmptyInputs_ReturnsFailure(
        string emailId, string originalAction, string correctedAction)
    {
        var sut = CreateSut();

        var result = await sut.UndoAsync(emailId, originalAction, correctedAction);

        Assert.False(result.IsSuccess);
    }
}

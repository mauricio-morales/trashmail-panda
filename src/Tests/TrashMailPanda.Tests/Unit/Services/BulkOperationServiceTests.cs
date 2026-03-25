using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Services;
using TrashMailPanda.Shared;
using TrashMailPanda.Shared.Base;
using Xunit;

namespace TrashMailPanda.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class BulkOperationServiceTests
{
    private readonly Mock<IEmailArchiveService> _archiveService = new();
    private readonly Mock<IEmailProvider> _emailProvider = new();

    private BulkOperationService CreateSut() =>
        new(_archiveService.Object, _emailProvider.Object,
            NullLogger<BulkOperationService>.Instance);

    private static EmailFeatureVector MakeVector(
        string emailId,
        string senderDomain = "example.com",
        int emailAgeDays = 10,
        float emailSizeLog = 8.0f) =>
        new()
        {
            EmailId = emailId,
            SenderDomain = senderDomain,
            EmailAgeDays = emailAgeDays,
            EmailSizeLog = emailSizeLog,
            ExtractedAt = DateTime.UtcNow,
            SpfResult = "pass",
            DkimResult = "pass",
            DmarcResult = "pass",
            SenderFrequency = 1,
            ThreadMessageCount = 1,
            FeatureSchemaVersion = 1,
        };

    // ── PreviewAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_ReturnsAllFeatures_WhenNoCriteriaSet()
    {
        var vectors = new List<EmailFeatureVector>
        {
            MakeVector("id1"),
            MakeVector("id2"),
        };
        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));

        var sut = CreateSut();
        var result = await sut.PreviewAsync(new BulkOperationCriteria());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task PreviewAsync_FiltersBySenderDomain()
    {
        var vectors = new List<EmailFeatureVector>
        {
            MakeVector("id1", senderDomain: "newsletter.com"),
            MakeVector("id2", senderDomain: "friend.com"),
        };
        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));

        var sut = CreateSut();
        var result = await sut.PreviewAsync(new BulkOperationCriteria { Sender = "newsletter" });

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("id1", result.Value[0].EmailId);
    }

    [Fact]
    public async Task PreviewAsync_FiltersByDateFrom()
    {
        // id1 received 5 days ago, id2 received 20 days ago
        var now = DateTime.UtcNow;
        var vectors = new List<EmailFeatureVector>
        {
            new() { EmailId = "id1", SenderDomain = "a.com", EmailAgeDays = 5, ExtractedAt = now, EmailSizeLog = 8f, SpfResult = "pass", DkimResult = "pass", DmarcResult = "pass", SenderFrequency = 1, ThreadMessageCount = 1, FeatureSchemaVersion = 1 },
            new() { EmailId = "id2", SenderDomain = "b.com", EmailAgeDays = 20, ExtractedAt = now, EmailSizeLog = 8f, SpfResult = "pass", DkimResult = "pass", DmarcResult = "pass", SenderFrequency = 1, ThreadMessageCount = 1, FeatureSchemaVersion = 1 },
        };
        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Success(vectors));

        var sut = CreateSut();
        // Only emails received within the last 10 days
        var tenDaysAgo = now - TimeSpan.FromDays(10);
        var result = await sut.PreviewAsync(new BulkOperationCriteria { DateFrom = tenDaysAgo });

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("id1", result.Value[0].EmailId);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsFailure_WhenArchiveServiceFails()
    {
        _archiveService.Setup(x => x.GetAllFeaturesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<EmailFeatureVector>>.Failure(new StorageError("DB error")));

        var sut = CreateSut();
        var result = await sut.PreviewAsync(new BulkOperationCriteria());

        Assert.False(result.IsSuccess);
    }

    // ── ExecuteAsync — Gmail action before label ───────────────────────────────

    [Theory]
    [InlineData("Archive")]
    [InlineData("Keep")]
    [InlineData("Delete")]
    public async Task ExecuteAsync_CallsBatchModify_ThenLabel(string action)
    {
        var order = new List<string>();
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .Callback<BatchModifyRequest>(_ => order.Add("gmail"))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync("id1", action, false, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, _, _, _) => order.Add("label"))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(["id1"], action);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(["gmail", "label"], order);
    }

    [Fact]
    public async Task ExecuteAsync_Spam_CallsReportSpam_ThenLabel()
    {
        var order = new List<string>();
        _emailProvider.Setup(x => x.ReportSpamAsync("id1"))
            .Callback<string>(_ => order.Add("gmail"))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync("id1", "Spam", false, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, _, _, _) => order.Add("label"))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(["id1"], "Spam");

        Assert.True(result.IsSuccess);
        Assert.Equal(["gmail", "label"], order);
    }

    [Fact]
    public async Task ExecuteAsync_GmailFailure_AddedToFailedIds_LabelNotCalled()
    {
        _emailProvider.Setup(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()))
            .ReturnsAsync(Result<bool>.Failure(new NetworkError("Gmail down")));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(["id1"], "Archive");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SuccessCount);
        Assert.Contains("id1", result.Value.FailedIds);
        _archiveService.Verify(x => x.SetTrainingLabelAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PartialFailure_ContinuesBatch()
    {
        _emailProvider.Setup(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r => r.EmailIds.Contains("id1"))))
            .ReturnsAsync(Result<bool>.Failure(new NetworkError("fail")));
        _emailProvider.Setup(x => x.BatchModifyAsync(It.Is<BatchModifyRequest>(r => r.EmailIds.Contains("id2"))))
            .ReturnsAsync(Result<bool>.Success(true));
        _archiveService.Setup(x => x.SetTrainingLabelAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(["id1", "id2"], "Archive");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Single(result.Value.FailedIds);
        Assert.Contains("id1", result.Value.FailedIds);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyList_ReturnsZeroSuccessWithNoGmailCall()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(Array.Empty<string>(), "Archive");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SuccessCount);
        _emailProvider.Verify(x => x.BatchModifyAsync(It.IsAny<BatchModifyRequest>()), Times.Never);
    }
}

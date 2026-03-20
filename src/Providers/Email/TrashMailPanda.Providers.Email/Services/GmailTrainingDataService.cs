using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using TrashMailPanda.Providers.Email.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;
using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Providers.Email.Services;

/// <summary>
/// Scans Gmail folders to build ML training datasets.
/// Processes Spam → Trash → Sent → Archive → Inbox in signal-value order.
/// Supports resumable scans, back-correction for engagement flags,
/// label taxonomy import, scan progress tracking, and incremental History API updates.
/// </summary>
public sealed class GmailTrainingDataService : IGmailTrainingDataService
{
    // Folder scan order: highest signal value first.
    // ARCHIVE uses a search query (not a label ID) because Gmail has no "ARCHIVE" system label —
    // archived emails are simply those with no INBOX/SENT/TRASH/SPAM label.
    private static readonly (string LabelId, string FolderName)[] ScanFolderOrder =
    [
        ("SPAM",      "SPAM"),
        ("TRASH",     "TRASH"),
        ("SENT",      "SENT"),
        ("ARCHIVE",   "ARCHIVE"),
        ("INBOX",     "INBOX"),
    ];

    // Gmail query used to fetch archived emails (everything that isn't inbox/sent/trash/spam).
    private const string ArchiveQuery = "-in:inbox -in:trash -in:spam -in:sent";

    private const int PageSize = 100;
    private const int InterPageDelayMs = 50;

    private readonly GmailEmailProvider _emailProvider;
    private readonly ITrainingSignalAssigner _signalAssigner;
    private readonly ITrainingEmailRepository _trainingEmailRepo;
    private readonly ILabelTaxonomyRepository _labelTaxonomyRepo;
    private readonly ILabelAssociationRepository _labelAssociationRepo;
    private readonly IScanProgressRepository _scanProgressRepo;
    private readonly IEmailArchiveService _archiveService;
    private readonly IGmailRateLimitHandler _rateLimitHandler;
    private readonly ILogger<GmailTrainingDataService> _logger;

    public GmailTrainingDataService(
        GmailEmailProvider emailProvider,
        ITrainingSignalAssigner signalAssigner,
        ITrainingEmailRepository trainingEmailRepo,
        ILabelTaxonomyRepository labelTaxonomyRepo,
        ILabelAssociationRepository labelAssociationRepo,
        IScanProgressRepository scanProgressRepo,
        IEmailArchiveService archiveService,
        IGmailRateLimitHandler rateLimitHandler,
        ILogger<GmailTrainingDataService> logger)
    {
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _signalAssigner = signalAssigner ?? throw new ArgumentNullException(nameof(signalAssigner));
        _trainingEmailRepo = trainingEmailRepo ?? throw new ArgumentNullException(nameof(trainingEmailRepo));
        _labelTaxonomyRepo = labelTaxonomyRepo ?? throw new ArgumentNullException(nameof(labelTaxonomyRepo));
        _labelAssociationRepo = labelAssociationRepo ?? throw new ArgumentNullException(nameof(labelAssociationRepo));
        _scanProgressRepo = scanProgressRepo ?? throw new ArgumentNullException(nameof(scanProgressRepo));
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
        _rateLimitHandler = rateLimitHandler ?? throw new ArgumentNullException(nameof(rateLimitHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<ScanSummary>> RunInitialScanAsync(
        string accountId,
        CancellationToken cancellationToken,
        IProgress<ScanProgressUpdate>? scanProgress = null)
    {
        var gmailService = _emailProvider.GetGmailService();
        if (gmailService is null)
            return Result<ScanSummary>.Failure(new InvalidOperationError("Gmail service is not initialized. Call ConnectAsync first."));

        var startedAt = DateTime.UtcNow;
        int totalProcessed = 0, autoDeleteCount = 0, autoArchiveCount = 0;
        int lowConfidenceCount = 0, excludedCount = 0, labelsImported = 0;
        // Declared outside try so the cancellation catch can mark it interrupted
        ScanProgressEntity? progress = null;

        try
        {
            // Import label taxonomy before folder traversal (T036)
            var labelResult = await ImportLabelTaxonomyAsync(gmailService, accountId, cancellationToken);
            if (labelResult.IsSuccess)
                labelsImported = labelResult.Value;

            // Load or create scan progress (T041, T043)
            var progressResult = await _scanProgressRepo.GetActiveAsync(accountId);
            Dictionary<string, FolderProgress> folderProgress;

            if (progressResult.IsSuccess && progressResult.Value is not null &&
                (progressResult.Value.Status == ScanStatus.InProgress ||
                 progressResult.Value.Status == ScanStatus.PausedStorageFull ||
                 progressResult.Value.Status == ScanStatus.Interrupted))
            {
                progress = progressResult.Value;
                // Mark back to InProgress so the checkpoint is live again
                progress.Status = ScanStatus.InProgress;
                progress.UpdatedAt = DateTime.UtcNow;
                await _scanProgressRepo.UpdateFolderProgressAsync(progress.Id, progress.FolderProgressJson);
                folderProgress = DeserializeFolderProgress(progress.FolderProgressJson);
                _logger.LogInformation("Resuming scan for {AccountId} from saved checkpoint.", accountId);
            }
            else
            {
                var folderState = ScanFolderOrder.ToDictionary(
                    f => f.LabelId,
                    _ => new FolderProgress { Status = ScanStatus.NotStarted });
                folderProgress = folderState;

                var createResult = await _scanProgressRepo.CreateAsync(new ScanProgressEntity
                {
                    AccountId = accountId,
                    ScanType = ScanType.Initial,
                    Status = ScanStatus.InProgress,
                    FolderProgressJson = SerializeFolderProgress(folderProgress),
                    ProcessedCount = 0,
                    StartedAt = startedAt,
                    UpdatedAt = startedAt
                });
                progress = createResult.IsSuccess ? createResult.Value : new ScanProgressEntity
                {
                    AccountId = accountId,
                    ScanType = ScanType.Initial,
                    Status = ScanStatus.InProgress
                };
            }

            // Scan each folder in order
            foreach (var (labelId, folderName) in ScanFolderOrder)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fp = folderProgress.GetValueOrDefault(labelId) ?? new FolderProgress();

                // Skip already-completed folders (T043)
                if (fp.Status == ScanStatus.Completed)
                {
                    _logger.LogDebug("Skipping completed folder {Folder}.", folderName);
                    // Notify the UI so it can mark the row as done rather than spinning forever
                    scanProgress?.Report(new ScanProgressUpdate(
                        folderName,
                        fp.ProcessedCount,
                        FolderCompleted: true,
                        totalProcessed));
                    continue;
                }

                fp.Status = ScanStatus.InProgress;
                folderProgress[labelId] = fp;

                var folderResult = await ScanFolderAsync(
                    gmailService, accountId, labelId, folderName, fp,
                    folderProgress, progress, scanProgress, cancellationToken);

                if (folderResult.IsSuccess)
                {
                    var stats = folderResult.Value;
                    totalProcessed += stats.TotalProcessed;
                    autoDeleteCount += stats.AutoDeleteCount;
                    autoArchiveCount += stats.AutoArchiveCount;
                    lowConfidenceCount += stats.LowConfidenceCount;
                    excludedCount += stats.ExcludedCount;

                    fp.Status = ScanStatus.Completed;
                    folderProgress[labelId] = fp;

                    // Persist the Completed status immediately so a cancel/restart skips this folder
                    await _scanProgressRepo.UpdateFolderProgressAsync(
                        progress.Id, SerializeFolderProgress(folderProgress));

                    // Notify folder complete
                    scanProgress?.Report(new ScanProgressUpdate(
                        folderName,
                        stats.TotalProcessed,
                        FolderCompleted: true,
                        totalProcessed));
                }
                else if (folderResult.Error is StorageQuotaError)
                {
                    // Storage quota reached — pause scan (T045)
                    progress.Status = ScanStatus.PausedStorageFull;
                    progress.FolderProgressJson = SerializeFolderProgress(folderProgress);
                    await _scanProgressRepo.UpdateFolderProgressAsync(progress.Id, progress.FolderProgressJson);
                    return Result<ScanSummary>.Failure(folderResult.Error);
                }
                else
                {
                    _logger.LogWarning("Folder {Folder} scan ended with error: {Error}", folderName, folderResult.Error.Message);
                }
            }

            // Run back-correction for engagement flags (T031)
            await _trainingEmailRepo.RunBackCorrectionAsync(accountId, cancellationToken);
            await _trainingEmailRepo.ReDeriveSignalsForThreadsAsync(accountId, _signalAssigner, cancellationToken);

            // Update label usage counts after all associations are written (T051)
            await _labelTaxonomyRepo.UpdateUsageCountsAsync(accountId, cancellationToken);

            // Save historyId for incremental scans (T047)
            var profileResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
                await gmailService.Users.GetProfile(GmailApiConstants.USER_ID_ME).ExecuteAsync(cancellationToken));

            if (profileResult.IsSuccess && profileResult.Value?.HistoryId is not null)
                await _scanProgressRepo.SaveHistoryIdAsync(progress.Id, profileResult.Value.HistoryId.Value);

            await _scanProgressRepo.MarkCompletedAsync(progress.Id);

            var duration = DateTime.UtcNow - startedAt;
            return Result<ScanSummary>.Success(new ScanSummary(
                totalProcessed, autoDeleteCount, autoArchiveCount,
                lowConfidenceCount, excludedCount, labelsImported, duration));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Initial scan cancelled by user for account {AccountId}.", accountId);
            // Best-effort: mark as interrupted so the next launch prompts to resume.
            try { await _scanProgressRepo.MarkInterruptedAsync(progress?.Id ?? 0); } catch { /* ignore */ }
            return Result<ScanSummary>.Failure(new OperationCancelledError("Scan cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial scan failed for account {AccountId}.", accountId);
            return Result<ScanSummary>.Failure(ex.ToProviderError("Initial scan failed"));
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScanSummary>> RunIncrementalScanAsync(string accountId, CancellationToken cancellationToken)
    {
        var gmailService = _emailProvider.GetGmailService();
        if (gmailService is null)
            return Result<ScanSummary>.Failure(new InvalidOperationError("Gmail service is not initialized."));

        var startedAt = DateTime.UtcNow;
        int totalProcessed = 0, autoDeleteCount = 0, autoArchiveCount = 0;
        int lowConfidenceCount = 0, excludedCount = 0, labelsImported = 0;

        try
        {
            // Get the last completed scan's historyId (T046)
            var progressResult = await _scanProgressRepo.GetActiveAsync(accountId);
            if (!progressResult.IsSuccess || progressResult.Value?.HistoryId is null)
                return Result<ScanSummary>.Failure(new ValidationError("No completed initial scan found. Run an initial scan first."));

            var lastProgress = progressResult.Value;
            ulong startHistoryId = lastProgress.HistoryId!.Value;

            // Import fresh label taxonomy
            var labelResult = await ImportLabelTaxonomyAsync(gmailService, accountId, cancellationToken);
            if (labelResult.IsSuccess) labelsImported = labelResult.Value;

            string? pageToken = null;
            var emails = new List<TrainingEmailEntity>();
            int fetchAttempts = 0, notFoundCount = 0;

            do
            {
                var historyResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
                {
                    var req = gmailService.Users.History.List(GmailApiConstants.USER_ID_ME);
                    req.StartHistoryId = startHistoryId;
                    req.MaxResults = PageSize;
                    if (pageToken is not null) req.PageToken = pageToken;
                    return await req.ExecuteAsync(cancellationToken);
                });

                if (!historyResult.IsSuccess)
                {
                    // historyId expired (404) — fall back to targeted re-check (T046)
                    _logger.LogWarning("History API returned error for account {AccountId}. HistoryId may have expired. Error: {Error}",
                        accountId, historyResult.Error.Message);
                    break;
                }

                var historyResponse = historyResult.Value;
                if (historyResponse?.History is null) break;

                // Collect message IDs from history changes
                var messageIds = historyResponse.History
                    .SelectMany(h =>
                        (h.MessagesAdded?.Select(m => m.Message.Id) ?? [])
                        .Concat(h.LabelsAdded?.Select(m => m.Message.Id) ?? [])
                        .Concat(h.LabelsRemoved?.Select(m => m.Message.Id) ?? []))
                    .Distinct()
                    .ToList();

                foreach (var msgId in messageIds)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    fetchAttempts++;
                    var msgResult = await FetchMessageAsync(gmailService, msgId, cancellationToken);
                    if (!msgResult.IsSuccess)
                    {
                        if (msgResult.Error is NotFoundError) notFoundCount++;
                        continue;
                    }

                    var msg = msgResult.Value;
                    var entity = BuildTrainingEmail(accountId, msg);
                    if (entity is not null) emails.Add(entity);
                }

                if (emails.Count >= PageSize)
                {
                    await FlushBatchAsync(accountId, emails, cancellationToken);
                    var counts = CountSignals(emails);
                    totalProcessed += emails.Count;
                    autoDeleteCount += counts.autoDelete;
                    autoArchiveCount += counts.autoArchive;
                    lowConfidenceCount += counts.lowConfidence;
                    excludedCount += counts.excluded;
                    emails.Clear();
                }

                pageToken = historyResponse.NextPageToken;
                if (pageToken is not null)
                    await Task.Delay(InterPageDelayMs, cancellationToken);

            } while (pageToken is not null && !cancellationToken.IsCancellationRequested);

            // Flush remaining
            if (emails.Count > 0)
            {
                await FlushBatchAsync(accountId, emails, cancellationToken);
                var counts = CountSignals(emails);
                totalProcessed += emails.Count;
                autoDeleteCount += counts.autoDelete;
                autoArchiveCount += counts.autoArchive;
                lowConfidenceCount += counts.lowConfidence;
                excludedCount += counts.excluded;
            }

            // Log 404 summary: only warn when >50% of fetches failed (indicates a real problem,
            // not the normal case of a handful of post-history-event deletions).
            if (fetchAttempts > 0 && notFoundCount > fetchAttempts / 2)
                _logger.LogWarning(
                    "Incremental scan for account {AccountId}: {NotFound}/{Total} message fetches returned 404. " +
                    "The saved historyId may be stale — consider running a full scan.",
                    accountId, notFoundCount, fetchAttempts);
            else if (notFoundCount > 0)
                _logger.LogDebug(
                    "Incremental scan for account {AccountId}: {NotFound}/{Total} messages were already deleted (normal).",
                    accountId, notFoundCount, fetchAttempts);

            // Back-correction and signal re-derivation
            await _trainingEmailRepo.RunBackCorrectionAsync(accountId, cancellationToken);
            await _trainingEmailRepo.ReDeriveSignalsForThreadsAsync(accountId, _signalAssigner, cancellationToken);
            await _labelTaxonomyRepo.UpdateUsageCountsAsync(accountId, cancellationToken);

            // Save updated historyId
            var profileResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
                await gmailService.Users.GetProfile(GmailApiConstants.USER_ID_ME).ExecuteAsync(cancellationToken));

            if (profileResult.IsSuccess && profileResult.Value?.HistoryId is not null)
                await _scanProgressRepo.SaveHistoryIdAsync(lastProgress.Id, profileResult.Value.HistoryId.Value);

            var duration = DateTime.UtcNow - startedAt;
            return Result<ScanSummary>.Success(new ScanSummary(
                totalProcessed, autoDeleteCount, autoArchiveCount,
                lowConfidenceCount, excludedCount, labelsImported, duration));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Incremental scan cancelled by user for account {AccountId}.", accountId);
            return Result<ScanSummary>.Failure(new OperationCancelledError("Scan cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental scan failed for account {AccountId}.", accountId);
            return Result<ScanSummary>.Failure(ex.ToProviderError("Incremental scan failed"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Result<FolderScanStats>> ScanFolderAsync(
        GmailService gmailService,
        string accountId,
        string labelId,
        string folderName,
        FolderProgress fp,
        Dictionary<string, FolderProgress> folderProgress,
        ScanProgressEntity progress,
        IProgress<ScanProgressUpdate>? progressCallback,
        CancellationToken cancellationToken)
    {
        // Seed from previously saved count so the display counter continues from where it left off
        int totalProcessed = fp.ProcessedCount, autoDeleteCount = 0, autoArchiveCount = 0;
        int lowConfidenceCount = 0, excludedCount = 0;
        string? pageToken = fp.PageToken; // Resume from saved cursor (T043)

        var emails = new List<TrainingEmailEntity>();

        do
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Check storage quota before each batch write (T045)
            var quotaResult = await _archiveService.ShouldTriggerCleanupAsync();
            if (quotaResult.IsSuccess && quotaResult.Value)
            {
                _logger.LogWarning("Storage quota reached for account {AccountId}. Pausing scan.", accountId);
                // Save progress and surface a StorageQuotaError to the caller
                fp.PageToken = pageToken;
                fp.Status = ScanStatus.InProgress;
                folderProgress[labelId] = fp;
                progress.FolderProgressJson = SerializeFolderProgress(folderProgress);
                await _scanProgressRepo.UpdateFolderProgressAsync(progress.Id, progress.FolderProgressJson);
                return Result<FolderScanStats>.Failure(new StorageQuotaError("Storage quota reached"));
            }

            // Fetch page of message IDs.
            // Archive has no system label — use a search query instead of a label filter.
            var listResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            {
                var req = gmailService.Users.Messages.List(GmailApiConstants.USER_ID_ME);
                if (labelId == "ARCHIVE")
                    req.Q = ArchiveQuery;
                else
                    req.LabelIds = labelId;
                req.MaxResults = PageSize;
                if (pageToken is not null) req.PageToken = pageToken;
                return await req.ExecuteAsync(cancellationToken);
            });

            if (!listResult.IsSuccess)
            {
                // pageToken expired — recover by restarting folder from beginning (T044)
                if (pageToken is not null)
                {
                    _logger.LogWarning("pageToken expired for folder {Folder}. Restarting folder scan.", folderName);
                    fp.Status = ScanStatus.Recovering;
                    fp.PageToken = null;
                    pageToken = null;
                    continue;
                }
                break;
            }

            var page = listResult.Value;

            // Empty folder — mark completed immediately (T052)
            if (page.Messages is null || page.Messages.Count == 0)
            {
                _logger.LogDebug("Folder {Folder} is empty, marking completed.", folderName);
                break;
            }

            // Fetch full message details per ID
            foreach (var stub in page.Messages)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var msgResult = await FetchMessageAsync(gmailService, stub.Id, cancellationToken);
                if (!msgResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to fetch message {MessageId}: {Error}", stub.Id, msgResult.Error.Message);
                    continue;
                }

                var entity = BuildTrainingEmail(accountId, msgResult.Value, folderName);
                if (entity is not null)
                    emails.Add(entity);
            }

            // Atomic batch upsert + association reconcile (T037)
            if (emails.Count > 0)
            {
                await _trainingEmailRepo.UpsertBatchAsync(emails, cancellationToken);

                foreach (var email in emails)
                {
                    var labelIds = ParseLabelIds(email.RawLabelIds);
                    await _labelAssociationRepo.ReconcileAssociationsAsync(email.EmailId, labelIds, cancellationToken);
                }

                var counts = CountSignals(emails);
                totalProcessed += emails.Count;
                autoDeleteCount += counts.autoDelete;
                autoArchiveCount += counts.autoArchive;
                lowConfidenceCount += counts.lowConfidence;
                excludedCount += counts.excluded;
                emails.Clear();

                // Report per-batch progress (folder not yet complete)
                progressCallback?.Report(new ScanProgressUpdate(
                    folderName,
                    EmailsProcessedInFolder: totalProcessed,
                    FolderCompleted: false,
                    TotalEmailsProcessed: totalProcessed));
            }

            // Save checkpoint after each page (T041)
            pageToken = page.NextPageToken;
            fp.PageToken = pageToken;
            fp.ProcessedCount += page.Messages?.Count ?? 0;
            folderProgress[labelId] = fp;

            progress.ProcessedCount = progress.ProcessedCount + (page.Messages?.Count ?? 0);
            progress.UpdatedAt = DateTime.UtcNow;
            if (page.Messages?.Count > 0)
                progress.LastProcessedEmailId = page.Messages[^1].Id;

            await _scanProgressRepo.UpdateFolderProgressAsync(
                progress.Id,
                SerializeFolderProgress(folderProgress));

            if (pageToken is not null)
                await Task.Delay(InterPageDelayMs, cancellationToken);

        } while (pageToken is not null && !cancellationToken.IsCancellationRequested);

        return Result<FolderScanStats>.Success(new FolderScanStats(
            totalProcessed, autoDeleteCount, autoArchiveCount,
            lowConfidenceCount, excludedCount));
    }

    private async Task<Result<int>> ImportLabelTaxonomyAsync(
        GmailService gmailService,
        string accountId,
        CancellationToken cancellationToken)
    {
        var labelsResult = await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
            await gmailService.Users.Labels.List(GmailApiConstants.USER_ID_ME).ExecuteAsync(cancellationToken));

        if (!labelsResult.IsSuccess) return Result<int>.Failure(labelsResult.Error);

        var labels = labelsResult.Value?.Labels;
        if (labels is null || labels.Count == 0) return Result<int>.Success(0);

        var entities = labels.Select(l => new LabelTaxonomyEntity
        {
            LabelId = l.Id,
            AccountId = accountId,
            Name = l.Name ?? l.Id,
            Color = l.Color?.TextColor,
            LabelType = IsSystemLabel(l.Id) ? "System" : "User",
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        await _labelTaxonomyRepo.UpsertBatchAsync(entities, cancellationToken);
        return Result<int>.Success(entities.Count);
    }

    private async Task<Result<Message>> FetchMessageAsync(
        GmailService gmailService,
        string messageId,
        CancellationToken cancellationToken)
    {
        return await _rateLimitHandler.ExecuteWithRetryAsync(async () =>
        {
            var req = gmailService.Users.Messages.Get(GmailApiConstants.USER_ID_ME, messageId);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            req.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(new[] { "Subject", "From", "To", "Date" });
            return await req.ExecuteAsync(cancellationToken);
        });
    }

    private TrainingEmailEntity? BuildTrainingEmail(
        string accountId,
        Message msg,
        string? folderOverride = null)
    {
        try
        {
            if (msg?.Id is null) return null;

            var labelIds = msg.LabelIds?.ToList() ?? [];
            var folder = folderOverride ?? DeriveFolder(labelIds);
            var isRead = !labelIds.Contains("UNREAD");
            var isSent = folder == "SENT";

            var subject = msg.Payload?.Headers
                ?.FirstOrDefault(h => h.Name?.Equals("Subject", StringComparison.OrdinalIgnoreCase) == true)
                ?.Value ?? string.Empty;

            string? subjectPrefix = null;
            if (isSent && subject.Length > 0)
                subjectPrefix = subject.Length > 10 ? subject[..10] : subject;

            var signal = _signalAssigner.AssignSignal(folder, isRead, new EngagementFlags(false, false));
            var now = DateTime.UtcNow;

            return new TrainingEmailEntity
            {
                EmailId = msg.Id,
                AccountId = accountId,
                ThreadId = msg.ThreadId ?? msg.Id,
                FolderOrigin = folder,
                IsRead = isRead,
                IsReplied = false,       // populated later by back-correction
                IsForwarded = false,     // populated later by back-correction
                SubjectPrefix = subjectPrefix,
                ClassificationSignal = signal.Signal.ToString(),
                SignalConfidence = signal.Confidence,
                IsValid = signal.Signal != ClassificationSignal.Excluded,
                RawLabelIds = labelIds.Count > 0 ? JsonSerializer.Serialize(labelIds) : null,
                LastSeenAt = now,
                ImportedAt = now,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            // Graceful partial-data handling (T053): log and skip, never fail the batch
            _logger.LogWarning(ex, "Failed to build training email from message {MessageId}. Skipping.", msg?.Id);
            return null;
        }
    }

    private static string DeriveFolder(IList<string> labelIds)
    {
        if (labelIds.Contains("SPAM")) return "SPAM";
        if (labelIds.Contains("TRASH")) return "TRASH";
        if (labelIds.Contains("SENT")) return "SENT";
        if (!labelIds.Contains("INBOX")) return "ARCHIVE";
        return "INBOX";
    }

    private static readonly HashSet<string> SystemLabelIds =
    [
        "INBOX", "SENT", "TRASH", "SPAM", "STARRED", "IMPORTANT",
        "UNREAD", "DRAFT", "CATEGORY_PERSONAL", "CATEGORY_SOCIAL",
        "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS"
    ];

    private static bool IsSystemLabel(string labelId) =>
        SystemLabelIds.Contains(labelId) || labelId.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ParseLabelIds(string? rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(rawJson) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, FolderProgress> DeserializeFolderProgress(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, FolderProgress>>(json) ?? []; }
        catch { return []; }
    }

    private static string SerializeFolderProgress(Dictionary<string, FolderProgress> progress) =>
        JsonSerializer.Serialize(progress);

    private async Task FlushBatchAsync(string accountId, List<TrainingEmailEntity> emails, CancellationToken cancellationToken)
    {
        await _trainingEmailRepo.UpsertBatchAsync(emails, cancellationToken);
        foreach (var email in emails)
        {
            var labelIds = ParseLabelIds(email.RawLabelIds);
            await _labelAssociationRepo.ReconcileAssociationsAsync(email.EmailId, labelIds, cancellationToken);
        }
    }

    private static (int autoDelete, int autoArchive, int lowConfidence, int excluded) CountSignals(
        IReadOnlyList<TrainingEmailEntity> emails)
    {
        int ad = 0, aa = 0, lc = 0, ex = 0;
        foreach (var e in emails)
        {
            switch (e.ClassificationSignal)
            {
                case nameof(ClassificationSignal.AutoDelete): ad++; break;
                case nameof(ClassificationSignal.AutoArchive): aa++; break;
                case nameof(ClassificationSignal.LowConfidence): lc++; break;
                case nameof(ClassificationSignal.Excluded): ex++; break;
            }
        }
        return (ad, aa, lc, ex);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private data types
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FolderProgress
    {
        public string Status { get; set; } = ScanStatus.NotStarted;
        public string? PageToken { get; set; }
        public int ProcessedCount { get; set; }
    }

    private sealed record FolderScanStats(
        int TotalProcessed,
        int AutoDeleteCount,
        int AutoArchiveCount,
        int LowConfidenceCount,
        int ExcludedCount);
}

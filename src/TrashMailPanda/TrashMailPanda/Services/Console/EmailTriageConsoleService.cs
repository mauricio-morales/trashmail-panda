using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using TrashMailPanda.Models.Console;
using TrashMailPanda.Providers.ML.Models;
using TrashMailPanda.Providers.Storage;
using TrashMailPanda.Providers.Storage.Models;
using TrashMailPanda.Shared.Base;

namespace TrashMailPanda.Services.Console;

/// <summary>
/// TUI presenter for the email triage workflow.
/// Drives the cold-start labeling loop (Phase 3) and AI-assisted triage loop (Phase 4).
/// Delegates business logic to <see cref="IEmailTriageService"/>;
/// this class is responsible only for rendering and key input.
/// Injects <see cref="IAnsiConsole"/> for testability — never references <c>AnsiConsole</c> statically.
/// </summary>
public sealed class EmailTriageConsoleService : IEmailTriageConsoleService
{
    private const int PageSize = 5;
    private const int RollingWindowSize = 100;

    private readonly IEmailTriageService _triageService;
    private readonly IConsoleHelpPanel _helpPanel;
    private readonly ILogger<EmailTriageConsoleService> _logger;
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly IAutoApplyService? _autoApplyService;
    private readonly IEmailArchiveService? _archiveService;
    private readonly IModelQualityMonitor? _qualityMonitor;
    private readonly IAutoApplyUndoService? _undoService;

    public EmailTriageConsoleService(
        IEmailTriageService triageService,
        IConsoleHelpPanel helpPanel,
        ILogger<EmailTriageConsoleService> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null,
        IAutoApplyService? autoApplyService = null,
        IEmailArchiveService? archiveService = null,
        IModelQualityMonitor? qualityMonitor = null,
        IAutoApplyUndoService? undoService = null)
    {
        _triageService = triageService ?? throw new ArgumentNullException(nameof(triageService));
        _helpPanel = helpPanel ?? throw new ArgumentNullException(nameof(helpPanel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
        _autoApplyService = autoApplyService;
        _archiveService = archiveService;
        _qualityMonitor = qualityMonitor;
        _undoService = undoService;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T032 — RunAsync (main triage loop)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<Result<TriageSessionSummary>> RunAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var infoResult = await _triageService.GetSessionInfoAsync(cancellationToken);
        if (!infoResult.IsSuccess)
            return Result<TriageSessionSummary>.Failure(infoResult.Error);

        var info = infoResult.Value;
        var session = new EmailTriageSession
        {
            AccountId = accountId,
            Mode = info.Mode,
            LabeledCount = info.LabeledCount,
            LabelingThreshold = info.LabelingThreshold,
            // Suppress the threshold prompt if the user already crossed it before this session
            ThresholdPromptShownThisSession = info.ThresholdAlreadyReached,
        };

        RenderSessionHeader(session);

        // Load auto-apply config once per session (null = feature not wired in DI)
        AutoApplyConfig? autoApplyConfig = null;
        if (_autoApplyService is not null)
        {
            var configResult = await _autoApplyService.GetConfigAsync(cancellationToken);
            if (configResult.IsSuccess)
            {
                autoApplyConfig = configResult.Value;
                _autoApplyService.ResetSession();
            }
        }

        _qualityMonitor?.ResetSession();

        var exitRequested = false;
        var manuallyReviewedCount = 0;

        while (!cancellationToken.IsCancellationRequested && !exitRequested)
        {
            // ── Fetch the next page ──────────────────────────────────────────
            // In re-triage phase: interleaved old-untriaged + recently-archived relabels.
            // In normal phase: untriaged emails ordered Inbox→Archive→most recent first.
            IReadOnlyList<EmailFeatureVector> batch;

            if (session.IsRetriagedPhase)
            {
                _logger.LogDebug("Triage loop: fetching re-triage batch (isRetriagedPhase=true)");
                var retriageResult = await _triageService.GetRetriageQueueAsync(PageSize, cancellationToken);
                if (!retriageResult.IsSuccess)
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Error}✗{ConsoleColors.Close} " +
                        $"{ConsoleColors.ErrorText}Failed to load re-triage emails: " +
                        $"{Markup.Escape(retriageResult.Error.Message)}{ConsoleColors.Close}");
                    break;
                }
                batch = retriageResult.Value;
            }
            else
            {
                // Always fetch from offset=0 because labeled emails are excluded by the query
                // (WHERE training_label IS NULL), so the pool naturally shrinks.
                _logger.LogDebug("Triage loop: fetching normal batch (isRetriagedPhase=false)");
                var batchResult = await _triageService.GetNextBatchAsync(PageSize, 0, cancellationToken);
                if (!batchResult.IsSuccess)
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Error}✗{ConsoleColors.Close} " +
                        $"{ConsoleColors.ErrorText}Failed to load emails: " +
                        $"{Markup.Escape(batchResult.Error.Message)}{ConsoleColors.Close}");
                    break;
                }
                batch = batchResult.Value;

                // Check if the oldest available untriaged email has crossed the 5-year threshold.
                // When batch[0] (the most recent untriaged) is 5+ years old, no newer emails remain.
                // Trigger the re-triage transition: mix old unlabeled with archived re-evaluations.
                // Use ReceivedDateUtc for accurate check; fall back to EmailAgeDays for legacy rows.
                var oldestAgeDays = batch.Count > 0
                    ? (batch[0].ReceivedDateUtc.HasValue
                        ? (int)(DateTime.UtcNow - batch[0].ReceivedDateUtc.Value).TotalDays
                        : batch[0].EmailAgeDays)
                    : 0;
                if (batch.Count > 0
                    && oldestAgeDays > EmailTriageService.OldEmailThresholdDays
                    && !session.IsRetriagedPhase)
                {
                    session.IsRetriagedPhase = true;
                    await ShowRetriageTransitionNoticeAsync(cancellationToken);

                    // Re-fetch as a properly mixed re-triage batch for this pass
                    var retriageResult = await _triageService.GetRetriageQueueAsync(PageSize, cancellationToken);
                    if (!retriageResult.IsSuccess)
                    {
                        _console.MarkupLine(
                            $"{ConsoleColors.Error}✗{ConsoleColors.Close} " +
                            $"{ConsoleColors.ErrorText}Failed to load re-triage emails: " +
                            $"{Markup.Escape(retriageResult.Error.Message)}{ConsoleColors.Close}");
                        break;
                    }
                    batch = retriageResult.Value;
                }
            }

            if (batch.Count == 0)
            {
                if (session.IsRetriagedPhase)
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Success}✓{ConsoleColors.Close} " +
                        $"All emails reviewed — re-triage complete.");
                }
                else if (session.LabeledCount == 0 && session.SessionProcessedCount == 0)
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Warning}⚠{ConsoleColors.Close} " +
                        $"{ConsoleColors.Warning}No emails in triage queue yet. " +
                        $"The Gmail scan may still be running, or try restarting the app.{ConsoleColors.Close}");
                }
                else
                {
                    _console.MarkupLine(
                        $"{ConsoleColors.Success}✓{ConsoleColors.Close} " +
                        $"All emails in queue have been labeled.");
                }
                break;
            }

            // ── Quality warning banner (per-batch) ──────────────────────────
            if (_qualityMonitor is not null && autoApplyConfig is not null)
            {
                var warningResult = await _qualityMonitor.CheckForWarningAsync(autoApplyConfig, cancellationToken);
                if (warningResult.IsSuccess && warningResult.Value is { } warning)
                {
                    RenderQualityWarning(warning);

                    // If auto-apply was disabled by the Critical path, persist the change
                    if (warning.AutoApplyDisabled && _autoApplyService is not null)
                    {
                        await _autoApplyService.SaveConfigAsync(autoApplyConfig, cancellationToken);
                    }
                }
            }

            foreach (var feature in batch)
            {
                if (cancellationToken.IsCancellationRequested || exitRequested)
                    break;

                bool isRetriage = feature.TrainingLabel is not null;

                _logger.LogDebug("Displaying email #{Seq}: [{EmailId}] subject={Subject} EmailAgeDays={Age} isRetriage={IsRetriage} isArchived={IsArchived} isInInbox={IsInInbox}",
                    session.SessionProcessedCount + 1,
                    feature.EmailId,
                    feature.SubjectText,
                    feature.EmailAgeDays,
                    isRetriage,
                    feature.IsArchived,
                    feature.IsInInbox);

                // T039 — Get AI recommendation (null in ColdStart, null for re-triage items)
                ActionPrediction? prediction = null;
                if (!isRetriage && session.Mode == TriageMode.AiAssisted)
                {
                    var recResult = await _triageService.GetAiRecommendationAsync(feature, cancellationToken);
                    prediction = recResult.IsSuccess ? recResult.Value : null;
                }

                // ── Auto-apply branch (FR-001, FR-003, FR-024) ───────────────
                // Only eligible for AI-assisted, non-re-triage emails with a prediction.
                var wasAutoApplied = false;
                if (!isRetriage
                    && session.Mode == TriageMode.AiAssisted
                    && prediction is not null
                    && autoApplyConfig is not null
                    && _autoApplyService is not null
                    && _autoApplyService.ShouldAutoApply(
                        autoApplyConfig,
                        new TrashMailPanda.Models.ClassificationResult
                        {
                            EmailId = feature.EmailId,
                            PredictedAction = prediction.PredictedLabel,
                            Confidence = prediction.Confidence,
                            ReasoningSource = TrashMailPanda.Models.ReasoningSource.ML,
                        }))
                {
                    var action = prediction.PredictedLabel;
                    var isRedundant = _autoApplyService.IsActionRedundant(action, feature);
                    var autoApplySucceeded = false;

                    if (!isRedundant)
                    {
                        // Execute Gmail action + store training label
                        var applyResult = await _triageService.ApplyDecisionAsync(
                            feature.EmailId, action, action,
                            forceUserCorrected: false, cancellationToken);

                        if (applyResult.IsSuccess)
                        {
                            autoApplySucceeded = true;
                        }
                        else
                        {
                            // Auto-apply failed — fall through to manual review
                            _logger.LogWarning(
                                "Auto-apply failed for {EmailId}: {Error}",
                                feature.EmailId, applyResult.Error.Message);
                        }
                    }
                    else if (_archiveService is not null)
                    {
                        // Redundant: skip Gmail API, store training label directly
                        await _archiveService.SetTrainingLabelAsync(
                            feature.EmailId, action, userCorrected: false, cancellationToken);
                        autoApplySucceeded = true;
                    }
                    else
                    {
                        // IEmailArchiveService is not wired — redundant action cannot be persisted.
                        // This is a DI misconfiguration; log and skip auto-apply so the email
                        // falls through to manual review rather than silently doing nothing.
                        _logger.LogWarning(
                            "Auto-apply skipped for {EmailId}: action '{Action}' is redundant but " +
                            "IEmailArchiveService is not registered — training label cannot be stored.",
                            feature.EmailId, action);
                    }

                    if (autoApplySucceeded)
                    {
                        // Record in session log and update counters
                        var entry = new AutoApplyLogEntry(
                            feature.EmailId,
                            feature.SenderDomain ?? string.Empty,
                            feature.SubjectText ?? string.Empty,
                            action,
                            prediction.Confidence,
                            DateTime.UtcNow,
                            isRedundant);
                        _autoApplyService.LogAutoApply(entry);
                        session.AutoApplyLog.Add(entry);
                        session.AutoAppliedCount++;

                        // Maintain rolling window for quality monitor
                        if (session.RollingDecisions.Count >= RollingWindowSize)
                            session.RollingDecisions.Dequeue();
                        session.RollingDecisions.Enqueue((action, action, false));

                        // Update session counters
                        session.LabeledCount++;
                        session.SessionProcessedCount++;
                        if (session.ActionCounts.ContainsKey(action))
                            session.ActionCounts[action]++;

                        var redundantNote = isRedundant ? " [dim](redundant — skipped API)[/]" : string.Empty;
                        _console.MarkupLine(
                            $"  {ConsoleColors.Success}⚡ Auto-applied:{ConsoleColors.Close} " +
                            $"[bold]{Markup.Escape(action)}[/] " +
                            $"{ConsoleColors.Dim}{(int)(prediction.Confidence * 100)}%{ConsoleColors.Close}" +
                            redundantNote);

                        // Show threshold prompt if crossed
                        if (!exitRequested
                            && !session.ThresholdPromptShownThisSession
                            && session.LabeledCount >= session.LabelingThreshold)
                        {
                            await ShowThresholdPromptAsync(cancellationToken);
                            session.ThresholdPromptShownThisSession = true;
                        }
                        // Record for quality monitor (auto-applied = predicted == chosen, isOverride=false)
                        _qualityMonitor?.RecordDecision(action, action, isOverride: false);

                        wasAutoApplied = true;
                    }
                }

                if (wasAutoApplied)
                    continue; // skip manual rendering — advance to next email

                manuallyReviewedCount++;

                // Fetch live email details (From header, ThreadId, body) with a brief spinner.
                // Failure is non-fatal — card renders with the stored feature data as fallback.
                EmailTriageDetails? details = null;
                await _console.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[dim]Loading...[/]", async _ =>
                    {
                        var fetchResult = await _triageService.FetchEmailDetailsAsync(
                            feature.EmailId, cancellationToken);
                        if (fetchResult.IsSuccess)
                            details = fetchResult.Value;
                    });

                // T033 — Render email card
                bool expanded = false;
                RenderEmailCard(feature, details, prediction, session, expanded, isRetriage);

                // T034 — Key reading loop for this email
                var decided = false;
                while (!decided && !cancellationToken.IsCancellationRequested && !exitRequested)
                {
                    var keyInfo = _readKey();
                    var upper = char.ToUpperInvariant(keyInfo.KeyChar);

                    // E — toggle expanded body
                    if (upper == 'E')
                    {
                        expanded = !expanded;
                        RenderEmailCard(feature, details, prediction, session, expanded, isRetriage);
                        continue;
                    }

                    // O — open in browser
                    if (upper == 'O')
                    {
                        var threadId = details?.ThreadId ?? feature.EmailId;
                        OpenBrowserToEmail(threadId);
                        _console.MarkupLine(
                            $"  {ConsoleColors.Info}ℹ Opened in browser{ConsoleColors.Close}");
                        continue;
                    }

                    var outcome = await HandleKeyAsync(keyInfo, feature, prediction, session, isRetriage, cancellationToken);

                    switch (outcome)
                    {
                        case KeyHandleResult.Decided:
                            decided = true;
                            break;
                        case KeyHandleResult.Exit:
                            decided = true;
                            exitRequested = true;
                            break;
                        case KeyHandleResult.Reprompt:
                            // Help was shown — re-render the action hint
                            RenderActionHint(session.Mode, prediction, isRetriage);
                            break;
                            // KeyHandleResult.Retry: fall through to re-read key (card already re-rendered)
                    }
                }

                // T035 — Show threshold prompt once when threshold reached
                if (!exitRequested
                    && !session.ThresholdPromptShownThisSession
                    && session.LabeledCount >= session.LabelingThreshold)
                {
                    await ShowThresholdPromptAsync(cancellationToken);
                    session.ThresholdPromptShownThisSession = true;
                }
            }
        }

        // T036 — Session summary
        var summary = BuildSummary(session, manuallyReviewedCount);
        RenderSessionSummary(summary);

        // Auto-apply session summary (shown only when auto-apply was active)
        if (_autoApplyService is not null && autoApplyConfig is not null && session.AutoAppliedCount > 0)
        {
            var autoSummary = _autoApplyService.GetSessionSummary(manuallyReviewedCount);
            RenderAutoApplySummary(autoSummary);
        }

        return Result<TriageSessionSummary>.Success(summary);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T033 — Cold-start card rendering (+ T039 — AI recommendation display)
    // ──────────────────────────────────────────────────────────────────────────

    private void RenderEmailCard(
        EmailFeatureVector feature,
        EmailTriageDetails? details,
        ActionPrediction? prediction,
        EmailTriageSession session,
        bool expanded = false,
        bool isRetriage = false)
    {
        _console.WriteLine();

        var ruleTitle = isRetriage
            ? $"[bold]Re-evaluate #{session.SessionProcessedCount + 1}[/]"
            : $"[bold]Email #{session.SessionProcessedCount + 1}[/]";
        var rule = new Rule(ruleTitle)
            .LeftJustified()
            .RuleStyle(isRetriage ? "yellow dim" : "dim");
        _console.Write(rule);

        // Dynamic age: prefer the absolute received date (always correct);
        // fall back to stored age + drift for legacy rows without ReceivedDateUtc.
        var displayAgeDays = feature.ReceivedDateUtc.HasValue
            ? Math.Max(0, (int)(DateTime.UtcNow - feature.ReceivedDateUtc.Value).TotalDays)
            : feature.EmailAgeDays + (int)(DateTime.UtcNow - feature.ExtractedAt).TotalDays;

        // Re-triage badge: show previous label and re-evaluate prompt
        if (isRetriage)
        {
            _console.MarkupLine(
                $"  {ConsoleColors.Warning}↩ Previously: [bold]{Markup.Escape(feature.TrainingLabel!)}[/]  " +
                $"— This email was archived {displayAgeDays}d ago. " +
                $"Is it still worth keeping?{ConsoleColors.Close}");
        }

        // From: prefer live-fetched full header; fall back to stored domain
        var sender = !string.IsNullOrWhiteSpace(details?.From)
            ? details.From
            : (!string.IsNullOrWhiteSpace(feature.SenderDomain) ? feature.SenderDomain : "(unknown sender)");

        var subject = string.IsNullOrWhiteSpace(feature.SubjectText)
            ? "(no subject)"
            : feature.SubjectText;

        _console.MarkupLine($"  {ConsoleColors.Dim}From:{ConsoleColors.Close}    {Markup.Escape(sender)}");
        _console.MarkupLine($"  {ConsoleColors.Dim}Subject:{ConsoleColors.Close} [bold]{Markup.Escape(subject)}[/]");

        // Derive the folder from stored flags so the user can see where the email lives.
        var folder = feature.WasInSpam == 1 ? "Spam"
            : feature.WasInTrash == 1 ? "Trash"
            : feature.IsArchived == 1 ? "Archive"
            : feature.IsInInbox == 1 ? "Inbox"
            : "Unknown";

        _console.MarkupLine($"  {ConsoleColors.Dim}Age:{ConsoleColors.Close}     {displayAgeDays}d old  ·  " +
                            $"{ConsoleColors.Dim}folder: {folder}  ·  {ConsoleColors.Close}" +
                            $"{(feature.IsStarred == 1 ? "⭐ Starred  · " : string.Empty)}" +
                            $"{(feature.HasAttachments == 1 ? "📎 Attachment  · " : string.Empty)}" +
                            $"{(feature.HasListUnsubscribe == 1 ? "📧 Mailing list  · " : string.Empty)}" +
                            $"{ConsoleColors.Dim}domain freq: {feature.SenderFrequency}  ·  " +
                            $"id: …{feature.EmailId[^Math.Min(6, feature.EmailId.Length)..]}{ConsoleColors.Close}");

        // Body: expanded shows up to 2 000 chars; collapsed shows the stored snippet
        if (expanded && !string.IsNullOrWhiteSpace(details?.BodyText))
        {
            var body = details.BodyText.Length > 2000
                ? details.BodyText[..2000] + "\n…"
                : details.BodyText;
            _console.MarkupLine($"  {ConsoleColors.Dim}{Markup.Escape(body)}{ConsoleColors.Close}");
        }
        else if (!string.IsNullOrWhiteSpace(feature.BodyTextShort))
        {
            var snippet = feature.BodyTextShort;
            var truncated = snippet.Length > 160 ? snippet[..160] + "…" : snippet;
            _console.MarkupLine($"  {ConsoleColors.Dim}{Markup.Escape(truncated)}{ConsoleColors.Close}");
        }

        _console.WriteLine();

        // T039 — AI recommendation (omitted in ColdStart)
        if (session.Mode == TriageMode.ColdStart || prediction == null)
        {
            var progress = $"{session.LabeledCount} / {session.LabelingThreshold}";
            _console.MarkupLine(
                $"  {ConsoleColors.Info}ℹ No AI suggestions yet{ConsoleColors.Close}  " +
                $"{ConsoleColors.Dim}({progress} labels collected){ConsoleColors.Close}");
        }
        else
        {
            // T040 — Confidence colors
            var confPct = (int)(prediction.Confidence * 100);
            var confColor = confPct >= 80 ? ConsoleColors.Success
                : confPct >= 50 ? ConsoleColors.Warning
                : ConsoleColors.ErrorText;

            _console.MarkupLine(
                $"  {ConsoleColors.AiRecommendation}🤖 AI suggests:{ConsoleColors.Close} " +
                $"[bold]{Markup.Escape(prediction.PredictedLabel)}[/]  " +
                $"{confColor}{confPct}% confidence{ConsoleColors.Close}");
        }

        _console.WriteLine();
        RenderActionHint(session.Mode, prediction, isRetriage);
    }

    private void RenderActionHint(TriageMode mode, ActionPrediction? prediction, bool isRetriage = false)
    {
        if (isRetriage)
        {
            // Re-triage hint: Enter/Y confirms the previous label, K/A/D/S overrides it.
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}Enter/Y{ConsoleColors.Close}=Confirm  " +
                $"{ConsoleColors.ActionHint}K{ConsoleColors.Close}=Keep  " +
                $"{ConsoleColors.ActionHint}A/1{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}3{ConsoleColors.Close}=Arch→1y  " +
                $"{ConsoleColors.ActionHint}4{ConsoleColors.Close}=Arch→5y  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}E{ConsoleColors.Close}=Expand  " +
                $"{ConsoleColors.ActionHint}O{ConsoleColors.Close}=Open  " +
                $"{ConsoleColors.ActionHint}Q{ConsoleColors.Close}=Exit  " +
                $"{ConsoleColors.ActionHint}?{ConsoleColors.Close}=Help");
        }
        else if (mode == TriageMode.AiAssisted && prediction != null)
        {
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}Enter/Y{ConsoleColors.Close}=Accept  " +
                $"{ConsoleColors.ActionHint}K{ConsoleColors.Close}=Keep  " +
                $"{ConsoleColors.ActionHint}A/1{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}3{ConsoleColors.Close}=Arch→1y  " +
                $"{ConsoleColors.ActionHint}4{ConsoleColors.Close}=Arch→5y  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}E{ConsoleColors.Close}=Expand  " +
                $"{ConsoleColors.ActionHint}O{ConsoleColors.Close}=Open  " +
                $"{ConsoleColors.ActionHint}Q{ConsoleColors.Close}=Exit  " +
                $"{ConsoleColors.ActionHint}?{ConsoleColors.Close}=Help");
        }
        else
        {
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}K{ConsoleColors.Close}=Keep  " +
                $"{ConsoleColors.ActionHint}A/1{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}3{ConsoleColors.Close}=Arch→1y  " +
                $"{ConsoleColors.ActionHint}4{ConsoleColors.Close}=Arch→5y  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}E{ConsoleColors.Close}=Expand  " +
                $"{ConsoleColors.ActionHint}O{ConsoleColors.Close}=Open  " +
                $"{ConsoleColors.ActionHint}Q{ConsoleColors.Close}=Exit  " +
                $"{ConsoleColors.ActionHint}?{ConsoleColors.Close}=Help");
        }
    }

    private static void OpenBrowserToEmail(string threadId)
    {
        var url = $"https://mail.google.com/mail/#all/{threadId}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Fallback for environments where shell execute fails (e.g. some Linux setups)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T034 — Key handling (T041 Enter/Y accept, T042/T043 retry/skip on error)
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<KeyHandleResult> HandleKeyAsync(
        ConsoleKeyInfo keyInfo,
        EmailFeatureVector feature,
        ActionPrediction? prediction,
        EmailTriageSession session,
        bool isRetriage,
        CancellationToken cancellationToken)
    {
        var key = char.ToUpperInvariant(keyInfo.KeyChar);
        var consoleKey = keyInfo.Key;

        // Exit
        if (key == 'Q' || consoleKey == ConsoleKey.Escape)
            return KeyHandleResult.Exit;

        // Help
        if (key == '?')
        {
            await _helpPanel.ShowAsync(HelpContext.ForEmailTriage(session.Mode), cancellationToken);
            return KeyHandleResult.Reprompt;
        }

        // Model quality stats
        if (key == 'M' && _qualityMonitor is not null)
        {
            var metricsResult = await _qualityMonitor.GetMetricsAsync(cancellationToken);
            if (metricsResult.IsSuccess)
                RenderModelQualityDashboard(metricsResult.Value);
            else
                _console.MarkupLine($"[red]✗ Could not load model metrics: {Markup.Escape(metricsResult.Error.Message)}[/]");
            return KeyHandleResult.Reprompt;
        }
        // Auto-apply review / undo
        if (key == 'R' && _autoApplyService is not null)
        {
            var log = _autoApplyService.GetSessionLog();
            if (log.Count == 0)
            {
                _console.MarkupLine("[dim]  No auto-applied decisions to review this session.[/]");
            }
            else
            {
                await RenderAutoApplyReviewTableAsync(log, cancellationToken);
            }
            return KeyHandleResult.Reprompt;
        }
        // Map key to action
        string? action = null;

        if (key == 'K')
            action = "Keep";
        else if (key == 'A' || key == '1')
            action = "Archive";
        else if (key == 'D')
            action = "Delete";
        else if (key == 'S')
            action = "Spam";
        else if (key == '2')
            action = "archive-then-delete-30d";
        else if (key == '3')
            action = "archive-then-delete-1y";
        else if (key == '4')
            action = "archive-then-delete-5y";
        else if ((key == 'Y' || consoleKey == ConsoleKey.Enter) && isRetriage && feature.TrainingLabel is not null)
            action = feature.TrainingLabel; // Confirm previous label during re-triage
        else if ((key == 'Y' || consoleKey == ConsoleKey.Enter)
                 && session.Mode == TriageMode.AiAssisted
                 && prediction != null)
            action = prediction.PredictedLabel; // T041 — Accept AI recommendation

        if (action is null)
            return KeyHandleResult.Reprompt; // Unknown key — reprompt

        // Apply decision (dual-write: Gmail first, then training label)
        // For re-triage items: forceUserCorrected=true so the email leaves the re-triage
        // pool regardless of whether the user confirmed or changed the previous label.
        // For re-triage: aiRec is the previous label (acts as the baseline recommendation).
        var aiRec = isRetriage ? feature.TrainingLabel : prediction?.PredictedLabel;
        var decisionResult = await _triageService.ApplyDecisionAsync(
            feature.EmailId, action, aiRec, forceUserCorrected: isRetriage, cancellationToken);

        if (decisionResult.IsSuccess)
        {
            var decision = decisionResult.Value;
            // T044 — Update session state
            session.LabeledCount++;
            session.SessionProcessedCount++;
            if (decision.IsOverride) session.SessionOverrideCount++;
            if (session.ActionCounts.ContainsKey(action))
                session.ActionCounts[action]++;

            // Record for quality monitor (manual review path)
            if (session.Mode == TriageMode.AiAssisted && prediction is not null)
                _qualityMonitor?.RecordDecision(prediction.PredictedLabel, action, decision.IsOverride);

            RenderDecisionFeedback(action, decision.IsOverride, aiRec);
            return KeyHandleResult.Decided;
        }
        else
        {
            // T042 / T043 — Retry or skip on error
            _console.MarkupLine(
                $"  {ConsoleColors.Error}✗{ConsoleColors.Close} " +
                $"{ConsoleColors.ErrorText}Action failed: " +
                $"{Markup.Escape(decisionResult.Error.Message)}{ConsoleColors.Close}");
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}R{ConsoleColors.Close}=Retry  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Skip (no training signal)");

            while (!cancellationToken.IsCancellationRequested)
            {
                var retryKey = _readKey();
                var rk = char.ToUpperInvariant(retryKey.KeyChar);

                if (rk == 'R')
                    return KeyHandleResult.Retry; // Caller re-renders card and loops
                if (rk == 'S')
                    return KeyHandleResult.Decided; // Skip — advance past email, no label stored
            }

            return KeyHandleResult.Decided;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Re-triage transition notice
    // ──────────────────────────────────────────────────────────────────────────

    private Task ShowRetriageTransitionNoticeAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule().RuleStyle("yellow"));
        _console.MarkupLine(
            $"  {ConsoleColors.Warning}⏳ You've reached emails older than 5 years.{ConsoleColors.Close}");
        _console.MarkupLine($"  [bold]Switching to Re-Triage mode:[/]");
        _console.MarkupLine($"  {ConsoleColors.Dim}· Continuing to label any remaining old emails{ConsoleColors.Close}");
        _console.MarkupLine(
            $"  {ConsoleColors.Dim}· Also surfacing archived emails from the last 3 years{ConsoleColors.Close}");
        _console.MarkupLine(
            $"  {ConsoleColors.Dim}  that may now be worth deleting as their content ages{ConsoleColors.Close}");
        _console.Write(new Rule().RuleStyle("yellow"));
        _console.WriteLine();
        _console.MarkupLine($"  {ConsoleColors.Dim}Press any key to continue…{ConsoleColors.Close}");
        _readKey();
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T035 — Threshold prompt
    // ──────────────────────────────────────────────────────────────────────────

    private Task ShowThresholdPromptAsync(CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.Write(new Rule().RuleStyle("green"));
        _console.MarkupLine(
            $"  {ConsoleColors.Success}✓{ConsoleColors.Close} " +
            $"{ConsoleColors.PromptOption}Training threshold reached![/]  " +
            $"You now have enough data to train the AI model.");
        _console.MarkupLine(
            $"  {ConsoleColors.Dim}Run '{ConsoleColors.Close}" +
            $"{ConsoleColors.ActionHint}train{ConsoleColors.Close}" +
            $"{ConsoleColors.Dim}' from the main menu to train the model.{ConsoleColors.Close}");
        _console.Write(new Rule().RuleStyle("green"));
        _console.WriteLine();

        // Non-blocking notice — press any key to continue
        _console.MarkupLine($"  {ConsoleColors.Dim}Press any key to continue labeling…{ConsoleColors.Close}");
        _readKey();

        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T036 — Session summary rendering
    // ──────────────────────────────────────────────────────────────────────────

    private void RenderSessionHeader(EmailTriageSession session)
    {
        _console.WriteLine();
        var modeLabel = session.Mode == TriageMode.ColdStart
            ? $"{ConsoleColors.Info}Cold-Start Labeling{ConsoleColors.Close}"
            : $"{ConsoleColors.AiRecommendation}AI-Assisted Triage{ConsoleColors.Close}";

        _console.MarkupLine($"  [bold]Email Triage[/] — {modeLabel}");

        if (session.Mode == TriageMode.ColdStart)
        {
            var remaining = Math.Max(0, session.LabelingThreshold - session.LabeledCount);
            _console.MarkupLine(
                $"  {ConsoleColors.Dim}Label {remaining} more email(s) to enable AI assistance " +
                $"({session.LabeledCount}/{session.LabelingThreshold}){ConsoleColors.Close}");
        }

        _console.Write(new Rule().RuleStyle("dim"));
        _console.WriteLine();
    }

    private void RenderDecisionFeedback(string action, bool isOverride, string? aiRec)
    {
        var overrideNote = isOverride && aiRec is not null
            ? $" {ConsoleColors.Warning}(override — AI suggested {Markup.Escape(aiRec)}){ConsoleColors.Close}"
            : string.Empty;

        _console.MarkupLine(
            $"  {ConsoleColors.Success}✓{ConsoleColors.Close} " +
            $"{ConsoleColors.Highlight}{Markup.Escape(action)}{ConsoleColors.Close}{overrideNote}");
    }

    private void RenderSessionSummary(TriageSessionSummary summary)
    {
        _console.WriteLine();
        _console.Write(new Rule("[bold]Session Summary[/]").RuleStyle("dim"));
        _console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Action")
            .AddColumn(new TableColumn("Count").RightAligned());

        table.AddRow("Keep", $"[green]{summary.KeepCount}[/]");
        table.AddRow("Archive", summary.ArchiveCount.ToString());
        table.AddRow("Archive→30d", summary.ArchiveThenDelete30dCount.ToString());
        table.AddRow("Archive→1y", summary.ArchiveThenDelete1yCount.ToString());
        table.AddRow("Archive→5y", summary.ArchiveThenDelete5yCount.ToString());
        table.AddRow("Delete", summary.DeleteCount.ToString());
        table.AddRow("Spam", summary.SpamCount.ToString());
        table.AddRow("[dim]─────[/]", "[dim]────[/]");
        table.AddRow("[bold]Total[/]", $"[bold]{summary.TotalProcessed}[/]");

        if (summary.OverrideCount > 0)
            table.AddRow($"{ConsoleColors.Warning}AI overrides{ConsoleColors.Close}", $"{ConsoleColors.Warning}{summary.OverrideCount}{ConsoleColors.Close}");

        _console.Write(table);

        var elapsed = summary.Elapsed.TotalMinutes >= 1
            ? $"{(int)summary.Elapsed.TotalMinutes}m {summary.Elapsed.Seconds}s"
            : $"{summary.Elapsed.TotalSeconds:F1}s";
        _console.MarkupLine($"  {ConsoleColors.Dim}Duration: {elapsed}{ConsoleColors.Close}");
        _console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static TriageSessionSummary BuildSummary(EmailTriageSession session, int manuallyReviewedCount)
    {
        session.ActionCounts.TryGetValue("Keep", out var keep);
        session.ActionCounts.TryGetValue("Archive", out var archive);
        session.ActionCounts.TryGetValue("archive-then-delete-30d", out var archiveThenDelete30d);
        session.ActionCounts.TryGetValue("archive-then-delete-1y", out var archiveThenDelete1y);
        session.ActionCounts.TryGetValue("archive-then-delete-5y", out var archiveThenDelete5y);
        session.ActionCounts.TryGetValue("Delete", out var delete);
        session.ActionCounts.TryGetValue("Spam", out var spam);

        return new TriageSessionSummary(
            TotalProcessed: session.SessionProcessedCount,
            KeepCount: keep,
            ArchiveCount: archive,
            ArchiveThenDelete30dCount: archiveThenDelete30d,
            ArchiveThenDelete1yCount: archiveThenDelete1y,
            ArchiveThenDelete5yCount: archiveThenDelete5y,
            DeleteCount: delete,
            SpamCount: spam,
            OverrideCount: session.SessionOverrideCount,
            Elapsed: DateTime.UtcNow - session.StartedAtUtc,
            AutoAppliedCount: session.AutoAppliedCount,
            ManuallyReviewedCount: manuallyReviewedCount
        );
    }

    private void RenderAutoApplySummary(AutoApplySessionSummary summary)
    {
        _console.WriteLine();
        _console.Write(new Rule("[bold]Auto-Apply Summary[/]").RuleStyle("dim"));
        _console.WriteLine();

        _console.MarkupLine(
            $"  {ConsoleColors.Success}⚡{ConsoleColors.Close} " +
            $"Auto-applied: [bold]{summary.TotalAutoApplied}[/]  " +
            $"{ConsoleColors.Dim}({summary.TotalRedundant} redundant, {summary.TotalUndone} undone){ConsoleColors.Close}");
        _console.MarkupLine(
            $"  {ConsoleColors.Info}👤{ConsoleColors.Close} " +
            $"Manually reviewed: [bold]{summary.TotalManuallyReviewed}[/]");

        if (summary.PerActionCounts.Count > 0)
        {
            var breakdown = string.Join("  ",
                summary.PerActionCounts.Select(kv => $"{Markup.Escape(kv.Key)}: {kv.Value}"));
            _console.MarkupLine($"  {ConsoleColors.Dim}Per action: {breakdown}{ConsoleColors.Close}");
        }
        _console.WriteLine();
    }

    private async Task RenderAutoApplyReviewTableAsync(
        IReadOnlyList<AutoApplyLogEntry> entries,
        CancellationToken ct)
    {
        _console.WriteLine();
        _console.MarkupLine("[bold blue]━━ Auto-Apply Review ━━[/]");
        _console.MarkupLine("[dim]Press [bold]U[/] next to an entry number to undo it, or [bold]Q[/] to return.[/]");
        _console.WriteLine();

        var table = new Table();
        table.AddColumn("#");
        table.AddColumn("Sender");
        table.AddColumn("Subject");
        table.AddColumn("Action");
        table.AddColumn("Confidence");
        table.AddColumn("Status");

        int idx = 1;
        foreach (var entry in entries)
        {
            var statusMarkup = entry.Undone
                ? $"[dim]Undone → {Markup.Escape(entry.UndoneToAction ?? "?")}[/]"
                : "[green]Applied[/]";
            var confidenceText = $"{entry.Confidence:P0}";

            table.AddRow(
                idx.ToString(),
                Markup.Escape(entry.SenderDomain),
                Markup.Escape(entry.Subject.Length > 40 ? entry.Subject[..40] + "…" : entry.Subject),
                Markup.Escape(entry.AppliedAction),
                confidenceText,
                statusMarkup);
            idx++;
        }

        _console.Write(table);

        // Interactive undo loop
        while (!ct.IsCancellationRequested)
        {
            _console.Markup("[dim]Enter entry # to undo (e.g. 2), or [bold]Q[/] to return: [/]");
            var inputKey = _readKey();
            _console.WriteLine();

            var inputChar = char.ToUpperInvariant(inputKey.KeyChar);

            if (inputChar == 'Q' || inputKey.Key == ConsoleKey.Escape)
                break;

            // Parse digit(s): for simplicity accept single digit 1-9
            if (!char.IsDigit(inputChar))
                continue;

            int entryNumber = inputChar - '0';
            if (entryNumber < 1 || entryNumber > entries.Count)
            {
                _console.MarkupLine($"[yellow]  Entry {entryNumber} out of range.[/]");
                continue;
            }

            var selected = entries[entryNumber - 1];
            if (selected.Undone)
            {
                _console.MarkupLine($"[dim]  Entry {entryNumber} already undone.[/]");
                continue;
            }

            if (_undoService is null)
            {
                _console.MarkupLine("[yellow]  Undo service is unavailable.[/]");
                continue;
            }

            // Prompt for corrected action
            _console.Markup("[dim]  Correct action [bold]K[/]=Keep [bold]A[/]=Archive [bold]D[/]=Delete [bold]S[/]=Spam: [/]");
            var actionKey = char.ToUpperInvariant(_readKey().KeyChar);
            _console.WriteLine();

            string? correctedAction = actionKey switch
            {
                'K' => "Keep",
                'A' => "Archive",
                'D' => "Delete",
                'S' => "Spam",
                _ => null
            };

            if (correctedAction is null)
            {
                _console.MarkupLine("[dim]  Invalid action — undo cancelled.[/]");
                continue;
            }

            var undoResult = await _undoService.UndoAsync(
                selected.EmailId, selected.AppliedAction, correctedAction, ct);

            if (undoResult.IsSuccess)
            {
                selected.Undone = true;
                selected.UndoneToAction = correctedAction;
                _qualityMonitor?.RecordDecision(selected.AppliedAction, correctedAction, isOverride: true);
                _console.MarkupLine($"[green]✓ Entry {entryNumber} undone ({Markup.Escape(selected.AppliedAction)} → {Markup.Escape(correctedAction)})[/]");
            }
            else
            {
                _console.MarkupLine(
                    $"[red]✗ Undo failed: {Markup.Escape(undoResult.Error.Message)}[/]");
            }
        }

        _console.WriteLine();
    }

    private void RenderModelQualityDashboard(ModelQualityMetrics metrics)
    {
        _console.WriteLine();
        _console.MarkupLine("[bold blue]━━ Model Quality Dashboard ━━[/]");
        _console.MarkupLine(
            $"  Rolling accuracy: [bold]{metrics.RollingAccuracy:P0}[/] " +
            $"over last {metrics.TotalDecisions} decisions (window: {metrics.RollingWindowSize})");
        _console.MarkupLine(
            $"  Corrections since last training: [bold]{metrics.CorrectionsSinceLastTraining}[/]");

        if (metrics.PerActionMetrics.Count > 0)
        {
            _console.WriteLine();
            var table = new Spectre.Console.Table();
            table.AddColumn("Action");
            table.AddColumn("Corrections");
            table.AddColumn("Correction Rate");

            foreach (var (action, m) in metrics.PerActionMetrics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var rateMarkup = m.CorrectionRate > 0.40f
                    ? $"[red]{m.CorrectionRate:P0}[/]"
                    : $"{m.CorrectionRate:P0}";

                table.AddRow(
                    Markup.Escape(action),
                    m.TotalRecommended.ToString(),
                    rateMarkup);
            }

            _console.Write(table);
        }

        _console.MarkupLine("[dim]Press any key to continue.[/]");
        _readKey();
        _console.WriteLine();
    }

    private void RenderQualityWarning(QualityWarning warning)
    {
        _console.WriteLine();
        switch (warning.Severity)
        {
            case QualityWarningSeverity.Critical:
                _console.MarkupLine("[bold red]⚠ MODEL QUALITY CRITICAL[/]");
                _console.MarkupLine($"[red]{Markup.Escape(warning.Message)}[/]");
                if (warning.AutoApplyDisabled)
                    _console.MarkupLine("[red]→ Auto-apply has been disabled automatically.[/]");
                _console.MarkupLine("[yellow]→ Recommend retraining before continuing. Press [bold]T[/] to retrain.[/]");
                break;

            case QualityWarningSeverity.Warning:
                _console.MarkupLine($"[yellow]⚠ {Markup.Escape(warning.Message)}[/]");
                _console.MarkupLine($"[yellow]→ {Markup.Escape(warning.RecommendedAction)}[/]");
                break;

            default: // Info
                _console.MarkupLine($"[cyan]ℹ {Markup.Escape(warning.Message)}[/]");
                _console.MarkupLine($"[cyan]→ {Markup.Escape(warning.RecommendedAction)}[/]");
                break;
        }

        if (warning.ProblematicActions is { Count: > 0 })
        {
            var actions = string.Join(", ", warning.ProblematicActions.Select(Markup.Escape));
            _console.MarkupLine($"[dim]  Problematic actions: {actions}[/]");
        }

        _console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Inner enum for key dispatch
    // ──────────────────────────────────────────────────────────────────────────

    private enum KeyHandleResult
    {
        /// <summary>User made a triage decision; advance to next email.</summary>
        Decided,

        /// <summary>User requested exit.</summary>
        Exit,

        /// <summary>Help shown or unknown key; re-render hint line.</summary>
        Reprompt,

        /// <summary>Action failed; caller should retry (re-render card and loop).</summary>
        Retry,
    }
}

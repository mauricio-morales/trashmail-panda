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

    private readonly IEmailTriageService _triageService;
    private readonly IConsoleHelpPanel _helpPanel;
    private readonly ILogger<EmailTriageConsoleService> _logger;
    private readonly IAnsiConsole _console;
    private readonly Func<ConsoleKeyInfo> _readKey;

    public EmailTriageConsoleService(
        IEmailTriageService triageService,
        IConsoleHelpPanel helpPanel,
        ILogger<EmailTriageConsoleService> logger,
        IAnsiConsole? console = null,
        Func<ConsoleKeyInfo>? readKey = null)
    {
        _triageService = triageService ?? throw new ArgumentNullException(nameof(triageService));
        _helpPanel = helpPanel ?? throw new ArgumentNullException(nameof(helpPanel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _console = console ?? AnsiConsole.Console;
        _readKey = readKey ?? (() => System.Console.ReadKey(intercept: true));
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
        };

        RenderSessionHeader(session);

        var exitRequested = false;

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
        var summary = BuildSummary(session);
        RenderSessionSummary(summary);
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
                $"{ConsoleColors.ActionHint}A{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}1{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→1y  " +
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
                $"{ConsoleColors.ActionHint}A{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}1{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→1y  " +
                $"{ConsoleColors.ActionHint}E{ConsoleColors.Close}=Expand  " +
                $"{ConsoleColors.ActionHint}O{ConsoleColors.Close}=Open  " +
                $"{ConsoleColors.ActionHint}Q{ConsoleColors.Close}=Exit  " +
                $"{ConsoleColors.ActionHint}?{ConsoleColors.Close}=Help");
        }
        else
        {
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}K{ConsoleColors.Close}=Keep  " +
                $"{ConsoleColors.ActionHint}A{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
                $"{ConsoleColors.ActionHint}1{ConsoleColors.Close}=Arch→30d  " +
                $"{ConsoleColors.ActionHint}2{ConsoleColors.Close}=Arch→1y  " +
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

        // Map key to action
        string? action = null;

        if (key == 'K')
            action = "Keep";
        else if (key == 'A')
            action = "Archive";
        else if (key == 'D')
            action = "Delete";
        else if (key == 'S')
            action = "Spam";
        else if (key == '1')
            action = "archive-then-delete-30d";
        else if (key == '2')
            action = "archive-then-delete-1y";
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

    private static TriageSessionSummary BuildSummary(EmailTriageSession session)
    {
        session.ActionCounts.TryGetValue("Keep", out var keep);
        session.ActionCounts.TryGetValue("Archive", out var archive);
        session.ActionCounts.TryGetValue("archive-then-delete-30d", out var archiveThenDelete30d);
        session.ActionCounts.TryGetValue("archive-then-delete-1y", out var archiveThenDelete1y);
        session.ActionCounts.TryGetValue("Delete", out var delete);
        session.ActionCounts.TryGetValue("Spam", out var spam);

        return new TriageSessionSummary(
            TotalProcessed: session.SessionProcessedCount,
            KeepCount: keep,
            ArchiveCount: archive,
            ArchiveThenDelete30dCount: archiveThenDelete30d,
            ArchiveThenDelete1yCount: archiveThenDelete1y,
            DeleteCount: delete,
            SpamCount: spam,
            OverrideCount: session.SessionOverrideCount,
            Elapsed: DateTime.UtcNow - session.StartedAtUtc
        );
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

using System;
using System.Collections.Generic;
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
            // Always fetch from offset=0 because labeled emails are excluded by the query
            // (WHERE training_label IS NULL), so the pool naturally shrinks.
            var batchResult = await _triageService.GetNextBatchAsync(PageSize, 0, cancellationToken);
            if (!batchResult.IsSuccess)
            {
                _console.MarkupLine(
                    $"{ConsoleColors.Error}✗{ConsoleColors.Close} " +
                    $"{ConsoleColors.ErrorText}Failed to load emails: " +
                    $"{Markup.Escape(batchResult.Error.Message)}{ConsoleColors.Close}");
                break;
            }

            var batch = batchResult.Value;
            if (batch.Count == 0)
            {
                _console.MarkupLine(
                    $"{ConsoleColors.Success}✓{ConsoleColors.Close} " +
                    $"All emails in queue have been labeled.");
                break;
            }

            foreach (var feature in batch)
            {
                if (cancellationToken.IsCancellationRequested || exitRequested)
                    break;

                // T039 — Get AI recommendation (null in ColdStart)
                ActionPrediction? prediction = null;
                if (session.Mode == TriageMode.AiAssisted)
                {
                    var recResult = await _triageService.GetAiRecommendationAsync(feature, cancellationToken);
                    prediction = recResult.IsSuccess ? recResult.Value : null;
                }

                // T033 — Render email card
                RenderEmailCard(feature, prediction, session);

                // T034 — Key reading loop for this email
                var decided = false;
                while (!decided && !cancellationToken.IsCancellationRequested && !exitRequested)
                {
                    var keyInfo = _readKey();
                    var outcome = await HandleKeyAsync(keyInfo, feature, prediction, session, cancellationToken);

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
                            RenderActionHint(session.Mode, prediction);
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
        ActionPrediction? prediction,
        EmailTriageSession session)
    {
        _console.WriteLine();

        var rule = new Rule($"[bold]Email #{session.SessionProcessedCount + 1}[/]")
            .LeftJustified()
            .RuleStyle("dim");
        _console.Write(rule);

        // Sender & subject
        var sender = string.IsNullOrWhiteSpace(feature.SenderDomain)
            ? "(unknown sender)"
            : feature.SenderDomain;
        var subject = string.IsNullOrWhiteSpace(feature.SubjectText)
            ? "(no subject)"
            : feature.SubjectText;
        var snippet = feature.BodyTextShort;

        _console.MarkupLine($"  {ConsoleColors.Dim}From:{ConsoleColors.Close}    {Markup.Escape(sender)}");
        _console.MarkupLine($"  {ConsoleColors.Dim}Subject:{ConsoleColors.Close} [bold]{Markup.Escape(subject)}[/]");
        _console.MarkupLine($"  {ConsoleColors.Dim}Age:{ConsoleColors.Close}     {feature.EmailAgeDays}d old  ·  " +
                            $"{(feature.IsStarred == 1 ? "⭐ Starred  · " : string.Empty)}" +
                            $"{(feature.HasAttachments == 1 ? "📎 Attachment  · " : string.Empty)}" +
                            $"{(feature.HasListUnsubscribe == 1 ? "📧 Mailing list  · " : string.Empty)}" +
                            $"{ConsoleColors.Dim}domain freq: {feature.SenderFrequency}{ConsoleColors.Close}");

        if (!string.IsNullOrWhiteSpace(snippet))
        {
            var truncated = snippet.Length > 120 ? snippet[..120] + "…" : snippet;
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
        RenderActionHint(session.Mode, prediction);
    }

    private void RenderActionHint(TriageMode mode, ActionPrediction? prediction)
    {
        if (mode == TriageMode.AiAssisted && prediction != null)
        {
            _console.MarkupLine(
                $"  {ConsoleColors.ActionHint}Enter/Y{ConsoleColors.Close}=Accept  " +
                $"{ConsoleColors.ActionHint}K{ConsoleColors.Close}=Keep  " +
                $"{ConsoleColors.ActionHint}A{ConsoleColors.Close}=Archive  " +
                $"{ConsoleColors.ActionHint}D{ConsoleColors.Close}=Delete  " +
                $"{ConsoleColors.ActionHint}S{ConsoleColors.Close}=Spam  " +
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
                $"{ConsoleColors.ActionHint}Q{ConsoleColors.Close}=Exit  " +
                $"{ConsoleColors.ActionHint}?{ConsoleColors.Close}=Help");
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
        else if ((key == 'Y' || consoleKey == ConsoleKey.Enter)
                 && session.Mode == TriageMode.AiAssisted
                 && prediction != null)
            action = prediction.PredictedLabel; // T041 — Accept AI recommendation

        if (action is null)
            return KeyHandleResult.Reprompt; // Unknown key — reprompt

        // Apply decision (dual-write: Gmail first, then training label)
        var aiRec = prediction?.PredictedLabel;
        var decisionResult = await _triageService.ApplyDecisionAsync(
            feature.EmailId, action, aiRec, cancellationToken);

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
        session.ActionCounts.TryGetValue("Delete", out var delete);
        session.ActionCounts.TryGetValue("Spam", out var spam);

        return new TriageSessionSummary(
            TotalProcessed: session.SessionProcessedCount,
            KeepCount: keep,
            ArchiveCount: archive,
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

# Research: Console TUI with Spectre.Console (#060)

**Feature**: Console TUI — Email Triage, Bulk Ops, Provider Settings, Color Scheme, Help System
**Date**: 2026-03-19
**Status**: Complete — all NEEDS CLARIFICATION items resolved

---

## 1. Session State Persistence (FR-010)

**Decision**: No dedicated `triage_sessions` table. Session continuity is stateless: at session
start, query `email_features WHERE training_label IS NULL` to build the queue of untriaged emails.
An interrupted session automatically resumes by simply re-running the same query next time the
user opens Email Triage. No extra infrastructure is required.

**Rationale**: Adding `TrainingLabel string?` to `EmailFeatureVector` (see §2) makes
`training_label IS NULL` the canonical "not yet triaged" predicate. The session queue IS the
table. Because the triage service sources emails from the local DB (not Gmail's API), paging is a
simple SQL `OFFSET`/`LIMIT` that needs no externally-stored cursor. The labeled count is derived
from `COUNT(*) WHERE training_label IS NOT NULL` (see §4) — a live query that is always accurate
across sessions without a persisted counter.

**Alternatives considered**:
- `triage_sessions` SQLite table with `last_processed_email_id` cursor and persisted
  `labeled_count` — rejected; the cursor is made redundant by the `training_label IS NULL` filter
  (already-processed emails are excluded by definition), and the persisted count duplicates
  information already in `email_features`.
- JSON flat file — rejected; duplicates storage responsibility.
- Keep state only in memory — rejected for labeled count (needs to survive restarts); but the
  queue itself is naturally stateless via a DB query.

**Affected files**: `EmailFeatureVector.cs` (add `TrainingLabel string?`),
`IEmailArchiveService` (add `GetUntriagedAsync`, `CountLabeledAsync`, `SetTrainingLabelAsync`),
one new EF Core migration to add `training_label` column to `email_features`.

---

## 2. Training Signal Storage for Triage Decisions (FR-008)

**Decision**: Add `TrainingLabel string?` to `EmailFeatureVector` / `email_features` table.
When `NULL`, the label is inferred at training time from boolean feature flags (`WasInSpam`,
`WasInTrash`, `IsInInbox`, `IsArchived`) via `ITrainingSignalAssigner`. When non-null, the value
is an explicit user decision from triage ("Keep", "Archive", "Delete", "Spam") and is used
directly as the ground-truth `Label` in `ActionTrainingInput`, overriding any inference.

The triage action has a **dual obligation**:
1. **Execute the Gmail action** — `IEmailProvider.BatchModifyAsync` (Keep/Archive/Delete) or
   `ReportSpamAsync` (Spam). This runs FIRST.
2. **Store the training label** — only if the Gmail action succeeds. Calls the new
   `IEmailArchiveService.SetTrainingLabelAsync(emailId, label, userCorrected)` which sets
   `training_label = label` and (when `userCorrected = true`) sets `UserCorrected = 1` on the
   same `email_features` row. If the Gmail action fails, do NOT store the label — no false
   training signals enter the DB.

**Implementation note**: Existing `UserCorrected` field is ORTHOGONAL to `TrainingLabel`:
- `UserCorrected = 1` means the user overrode an AI recommendation (retention + retrain trigger)
- `TrainingLabel` is the explicit ground-truth action label for the ML pipeline
Both can be set independently. A user-corrected vector has both non-null `TrainingLabel` and
`UserCorrected = 1`.

**Bug fix**: `ModelTrainingPipeline.MapToTrainingInput` and `IncrementalUpdateService` both
currently set `Label = string.Empty` with a comment "Ground-truth label populated by user action
pipeline." This feature implements that pipeline. After the migration, `MapToTrainingInput`
becomes: `Label = v.TrainingLabel ?? InferLabel(v)` where `InferLabel` delegates to
`ITrainingSignalAssigner` (already exists, just never called).

**Rationale**: `email_features` is already the single source of truth for ML training. Adding
one nullable column is the minimal change that: (a) fixes the training label bug, (b) provides
explicit ground truth for user-triaged emails, (c) avoids a duplicate storage entity that the
training pipeline would have to join.

**Alternatives considered**:
- Separate `triage_decisions` table — rejected; ML pipeline doesn't read from it; would require
  a join or a duplicate mapper.
- Store only `UserCorrected = 1`, infer label from folder at training time — rejected; scan-time
  folder flags reflect the email's state during the initial scan, not the user's triage decision.
  The user may "Archive" an email that was already in the inbox (no folder change visible to
  inference). Explicit `TrainingLabel` is unambiguous.
- Separate `label_source` column — rejected as over-engineering; the implicit rule is: `NULL`
  training_label = inferred at training time; non-null = explicit triage decision.

---

## 3. Cold-Start vs AI-Assisted Mode Detection (FR-002)

**Decision**: At session start, call `IMLModelProvider.GetActiveModelVersionAsync("action")`.
If it returns `Result.Success`, use `AiAssisted` triage mode. If it returns a failure
(no model trained), use `ColdStart` triage mode.

**Rationale**: `GetActiveModelVersionAsync` is the definitive authoritative check for whether
a live trained model exists. It is already used internally by the provider for self-assessment.
No extra infrastructure is needed.

**Alternatives considered**:
- `IMLModelProvider.ClassifyActionAsync` call with a dummy input — rejected; wasteful and
  side-effectful.
- Flag in app settings — rejected; flag could become stale after model training.

---

## 4. Labeled Count Query for Progress Indicator (FR-004)

**Decision**: At session start, query `COUNT(*) FROM email_features WHERE training_label IS NOT NULL`
via a new `IEmailArchiveService.CountLabeledAsync(CancellationToken)` method. Store this count in
`EmailTriageSession.LabeledCount` (in-memory `int`). Increment it on each successful triage
decision without re-querying. After sessions end, the next session start re-queries to get the
accurate live count (naturally handles any background incremental scan that may have added labels
between sessions).

**Threshold source**: Read from `MLModelProviderConfig.MinTrainingSamples` (default 100) injected
into `EmailTriageConsoleService` via `IOptions<MLModelProviderConfig>`. The spec states framing
as a configuration value: "defined by the ML pipeline and consumed by this feature."

**Rationale**: `email_features.training_label` is the authoritative source of labeled count.
A single `COUNT` query at session start is cheap (SQLite, indexed primary key scan). In-memory
increment during the session avoids one DB round-trip per email. This naturally survives app
restarts (next start re-counts from DB) without a dedicated persisted counter.

**Alternatives considered**:
- Persisted `labeled_count` column in a `triage_sessions` table — rejected (see §1); duplicates
  information already derivable from `email_features`; can drift from reality if the DB is
  modified by other processes.
- Query `COUNT` on every email — rejected; O(1) query on each decision is marginally cheap but
  unnecessary given in-memory tracking within a session.

---

## 5. Email Batch Loading and Presentation (FR-001)

**Decision**: Source the triage queue from the **local SQLite DB** via a new
`IEmailArchiveService.GetUntriagedAsync(int pageSize, int offset, CancellationToken)` method that
queries `email_features WHERE training_label IS NULL ORDER BY extracted_at DESC`. Display metadata
(sender, subject, date) from `EmailFeatureVector` fields already in the DB. If a enriched snippet
is needed, look it up from `email_archives` on the same `EmailId` (join or separate call).

The Gmail API is called **only for action execution** (step 1 of the dual-write in §2), never
for listing emails to triage. `IEmailProvider.ListAsync` is NOT called during triage.

**Rationale**: 
- All emails already processed by the initial Gmail scan have `email_features` rows.
  These are the emails most suited for triage (features already extracted).
- Local DB query is sub-millisecond; no latency or API quota cost per page.
- Emails already triaged (`training_label IS NOT NULL`) are automatically excluded — stateless
  resume for free.
- Works offline for presentation (only action execution requires network).

**Session queue boundary**: When `GetUntriagedAsync` returns 0 rows, the entire available queue
has been processed. Show the batch-complete summary. New emails will enter the queue as the
incremental scan runs in the background — user can re-enter triage to pick up new arrivals.

**Alternatives considered**:
- `IEmailProvider.ListAsync` (Gmail API) as email source — rejected; requires network per page,
  may surface emails not yet in `email_features` (no features to train on), and returns emails
  already triaged requiring client-side de-duplication.
- Fetch `email_archives` snippet on every card render — `email_archives` JOIN is acceptable for
  snippet enrichment but is optional; `EmailFeatureVector` has enough metadata (sender, subject,
  date) for the card. Snippet from archive used only if available.

---

## 6. Gmail Action Execution Mapping (FR-009)

| User Action | IEmailProvider Call | Gmail Effect |
|-------------|---------------------|--------------|
| Keep        | `BatchModifyAsync` with `RemoveLabelIds: ["UNREAD"]` only | Marks read, stays in inbox |
| Archive     | `BatchModifyAsync` with `RemoveLabelIds: ["INBOX"]` | Moves out of inbox |
| Delete      | `BatchModifyAsync` with `AddLabelIds: ["TRASH"]` | Moves to trash (recoverable) |
| Spam        | `ReportSpamAsync(id)` | Moves to spam folder |

**Decision**: Use `BatchModifyAsync` for Keep/Archive/Delete so a single API call applies
label changes. Use `ReportSpamAsync` for Spam since it triggers Gmail-side spam classification
in addition to the label move.

**Rationale**: `BatchModifyAsync(BatchModifyRequest)` is the existing interface method for
label operations. `ReportSpamAsync` is the semantically correct call for spam reporting.

**Alternatives considered**:
- `DeleteAsync` for Delete — rejected; that method performs a hard (permanent) delete. Spec
  intentions imply "trash" (soft delete, recoverable). Bold-delete is inappropriate for
  a triage workflow.
- Single-email `BatchModifyAsync` for Spam — rejected; misses the spam-reporting signal at
  Gmail that improves their spam filters.

---

## 7. Centralized Color Scheme (FR-022)

**Decision**: Static class `ConsoleColors` in
`src/TrashMailPanda/TrashMailPanda/Services/Console/ConsoleColors.cs` with `public const string`
properties for each semantic token.

**Token list** (from spec FR-019–FR-021 + edge cases):

| Token | Markup Value | Semantic Use |
|-------|-------------|--------------|
| `Error` | `"[bold red]"` | Error indicator |
| `ErrorText` | `"[red]"` | Red error body text |
| `Success` | `"[green]"` | Success confirmation |
| `Warning` | `"[yellow]"` | Warning messages, advisories |
| `Info` | `"[blue]"` | Information messages |
| `Metric` | `"[magenta]"` | Training metric values |
| `Highlight` | `"[cyan]"` | Highlights, action prompts |
| `ActionHint` | `"[cyan]"` | Key binding hints |
| `Dim` | `"[dim]"` | Secondary text |
| `Close` | `"[/]"` | Closes any open markup tag |

**Usage pattern**:
```csharp
_console.MarkupLine($"{ConsoleColors.Error}✗[/] {ConsoleColors.ErrorText}Failed to fetch email: {Markup.Escape(message)}[/]");
```

**Rationale**: Compile-time constants eliminate typos, are greppable with `ConsoleColors\.`, and
make "zero occurrences of raw markup outside the definition file" verifiable by a single grep.

**Alternatives considered**:
- `enum SemanticColor` with a lookup dictionary — rejected; overhead not justified for a small
  fixed list of string constants.
- `record ConsoleColorScheme` with instance methods — rejected; singleton state is unnecessary
  for read-only constants.
- Existing `ProfessionalColors` Avalonia class — rejected; that class targets Avalonia XAML colors
  (ARGB structs), incompatible with Spectre.Console markup strings.

---

## 8. Help System Architecture (FR-023)

**Decision**: `ConsoleHelpPanel` static class with a `ShowAsync(IAnsiConsole, HelpContext)` method.
Each mode constructs a `HelpContext` (title + list of `KeyBinding` records) before calling `ShowAsync`.
The panel renders as a Spectre.Console `Table` wrapped in a `Panel` widget.

**Key binding display pattern**: Render two-column table (Key | Description) with grid lines.
Press any key to dismiss (read single keypress with `System.Console.ReadKey(intercept:true)`).

**Rationale**: A static helper with injected console keeps the help panel testable and keeps
mode-specific key definitions colocated with the mode service (not in a central registry
that coupling concerns would require).

**Alternatives considered**:
- Central registry of all mode key bindings — rejected; creates hidden coupling between mode
  services and the help registry.
- Spectre.Console `Layout` for overlay — rejected; terminal layout panels don't overlay; they
  replace the current render. Simpler to clear and redraw after dismissal.

---

## 9. Bulk Operations Mode (FR-025)

**Decision**: Implement `BulkOperationConsoleService` with a two-step UI:
1. Criteria selection (sender filter, date range, confidence threshold, action type).
2. Preview summary showing matched email count and estimated storage impact.
3. Confirmation prompt before batch execution via `IEmailProvider.BatchModifyAsync`.

**Rationale**: Bulk operations are high-risk (affects many emails at once); the two-step
confirmation flow is required by the spec and standard TUI practice.

**Alternatives considered**:
- Single-step bulk execute — rejected; spec requires user confirmation before bulk actions.
- Real-time streaming of affected emails — rejected; too complex for P2; summary count sufficient.

---

## 10. Provider Settings Mode (FR-016–FR-018)

**Decision**: `ProviderSettingsConsoleService` wraps the existing `ConfigurationWizard` for
re-authorization flows. For storage display, call `IEmailArchiveService.GetStorageUsageAsync()`
(already implemented). For storage limit adjustment, update `MLModelProviderConfig` via
`IOptions<MLModelProviderConfig>` and persist to `appsettings.json` (or a user settings key).

**Rationale**: Re-using `ConfigurationWizard` for OAuth re-auth avoids duplicating the OAuth
flow. The provider settings mode acts as an entry point to the existing wizard for Gmail
reconfiguration specifically.

**Storage limit persistence note**: The storage limit is in `StorageConfig.StorageLimitBytes`.
Updating it requires writing to the SQLite config table or appsettings. This implementation
will write to the SQLite `config` table (key-value) which is the existing pattern.

**Alternatives considered**:
- Build a separate OAuth re-auth flow from scratch — rejected; `ConfigurationWizard` already
  handles the full flow including PKCE, browser launch, and callback listener.

---

## 11. Training Mode: Save Confirmation (FR-015)

**Decision**: After `TrainingConsoleService.RunTrainingAsync` completes with a success report,
add an explicit `AnsiConsole.Confirm("Save this model as the active model?")` prompt. If confirmed,
call `IModelTrainingPipeline.SaveModelAsync` (or the existing save mechanism within the pipeline).

**Finding**: Looking at `ModelTrainingPipeline`, the save is currently done automatically as
part of `TrainActionModelAsync` (crash-safe file persistence + DB versioning). The gap is that
there is no *user-facing confirmation step* before the model becomes active. The save confirmation
must be threaded through as a `saveConfirmed` parameter or by splitting training from model
activation.

**Decision**: Introduce a `saveModel` bool parameter to `TrainingConsoleService.RunTrainingAsync`.
The training service prompts the user and passes the result. The pipeline already handles
the file write, so no new pipeline changes are needed — the existing behavior writes the file;
the service layer just needs to prompt and only display success if user confirmed.

**Note**: The pipeline writes the model file as part of training (crash-safe temp file pattern).
The "confirmation" is effectively about whether to register the new version in the DB as the
active version. This requires a `PromoteModelAsync(modelVersion)` method or similar. Research
into `ModelVersionRepository` needed during implementation.

---

## 12. Spectre.Console Testability Pattern (IAnsiConsole)

**Decision**: All new console services accept `IAnsiConsole` as an optional constructor parameter,
defaulting to `AnsiConsole.Console`. This matches the existing `TrainingConsoleService` pattern.

**Test pattern** (from existing `TrainingConsoleServiceTests`):
```csharp
var writer = new StringWriter();
var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
var service = new EmailTriageConsoleService(..., console);
```

---

## 9. UI / Business Logic Separation (Architecture)

**Decision**: Introduce a two-layer service architecture that isolates triage and bulk-operation
business logic from any UI-specific concerns. Each major feature area exposes two interfaces:

| Layer | Interface | Location | Depends on |
|-------|-----------|----------|------------|
| Business logic | `IEmailTriageService` | `TrashMailPanda/Services/` | `IEmailProvider`, `IMLModelProvider`, `IEmailArchiveService` |
| TUI presenter | `IEmailTriageConsoleService` | `TrashMailPanda/Services/Console/` | `IEmailTriageService`, `IAnsiConsole` |
| Business logic | `IBulkOperationService` | `TrashMailPanda/Services/` | `IEmailProvider`, `IEmailArchiveService` |
| TUI presenter | `IBulkOperationConsoleService` | `TrashMailPanda/Services/Console/` | `IBulkOperationService`, `IAnsiConsole` |

`IEmailTriageService` and `IBulkOperationService` contain **all orchestration logic**:
- mode detection, Gmail action execution, dual-write via `SetTrainingLabelAsync`
- queue retrieval, AI recommendation fetching, labeled count queries
- returns pure data (`TriageDecision`, `TriageSessionInfo`) — no rendering

`IEmailTriageConsoleService` and `IBulkOperationConsoleService` are **thin presenters**:
- render email cards, tables, and progress bars with `IAnsiConsole`
- capture keypresses and map them to actions
- call the corresponding `IEmailTriageService` / `IBulkOperationService` method
- handle error display (bold red) when operations fail
- NO direct dependency on `IEmailProvider`, `IMLModelProvider`, `IEmailArchiveService`

**Future UI substitution**: A web API, Avalonia desktop, or MCP tool would implement its own
presenter (`IEmailTriageWebController`, etc.) depending on the same `IEmailTriageService`. The
business logic is never rewritten — only a new presenter is added.

**Extraction path**: Both `IEmailTriageService` and `IBulkOperationService` live in the
`TrashMailPanda` console app for now. When a second UI project is created, they move to
`TrashMailPanda.Shared` with no breaking changes (interface-typed DI throughout).

**Rationale**: The triage workflow has significant orchestration complexity (mode detection,
dual-write ordering, session state, threshold tracking, error recovery). If this logic lives
inside `EmailTriageConsoleService`, every future UI must re-implement it from scratch or depend
on Spectre.Console, which is unacceptable. The two-layer split is the minimum required for
long-term reusability.

**Alternatives considered**:
- Single `EmailTriageConsoleService` mixing rendering + logic — rejected; prevents reuse by
  future UIs; `IAnsiConsole` dependency blocks non-console consumers.
- MVVM (ViewModel + View) — rejected for console-first architecture (see constitution Principle IV
  scope note). The Presenter pattern achieves the same separation without Avalonia data binding.
- Shared `IEmailTriageService` in `TrashMailPanda.Shared` from day one — considered but deferred;
  premature extraction adds project complexity before a second UI materializes. The extraction is
  trivial once needed (move file + update namespace).

---

## 13. Terminal Color Degradation (FR-024)

**Decision**: Rely on Spectre.Console's built-in terminal capability detection.
`AnsiConsole.Capabilities.SupportsAnsi` is checked by Spectre.Console automatically before
rendering markup. No additional custom detection is needed per the spec assumption:
"Terminal capability detection (ANSI support) is handled by Spectre.Console automatically."

**Rationale**: Spectre.Console degrades gracefully — if the terminal doesn't support ANSI,
it strips markup tags and renders plain text. This is the documented default behavior.

---

## Summary of Resolved Unknowns

| Item | Status | Decision |
|------|--------|----------|
| Session state persistence mechanism | ✅ Resolved | Stateless: `email_features WHERE training_label IS NULL` |
| Training signal storage | ✅ Resolved | `email_features.training_label` (new nullable column) + dual-write with Gmail action |
| Cold-start detection | ✅ Resolved | `GetActiveModelVersionAsync` return value |
| Labeled count tracking | ✅ Resolved | `CountLabeledAsync` DB query at session start; in-memory increment during session |
| Email loading strategy | ✅ Resolved | Local DB `GetUntriagedAsync` (not Gmail `ListAsync`) |
| Gmail action mapping | ✅ Resolved | BatchModifyAsync for Keep/Archive/Delete; ReportSpamAsync for Spam |
| Color scheme implementation | ✅ Resolved | `ConsoleColors` static class with `const string` |
| Help system architecture | ✅ Resolved | `ConsoleHelpPanel.ShowAsync(console, HelpContext)` |
| Bulk ops confirmation flow | ✅ Resolved | Two-step: criteria → preview → confirm |
| Provider settings re-auth | ✅ Resolved | Delegate to existing `ConfigurationWizard` |
| Training save confirmation | ✅ Resolved | User prompt added to `TrainingConsoleService` |
| Terminal degradation | ✅ Resolved | Spectre.Console built-in, no custom code |
| UI/business logic separation | ✅ Resolved | Two-layer: `IEmailTriageService` (logic) + `IEmailTriageConsoleService` (TUI presenter) |

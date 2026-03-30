# Feature Specification: Bulk Auto-Apply Mode

**Feature Branch**: `065-bulk-auto-apply`
**Created**: 2026-03-26
**Status**: Draft
**Input**: User description: "feat: Bulk Auto-Apply mode — sweep full archive and apply high-confidence actions #89"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sweep Archive with High-Confidence Auto-Apply (Priority: P1)

A user has trained their local ML model over several weeks. Their archive now contains thousands of untriaged emails. They want to process all emails the model is highly confident about — without having to manually dismiss hundreds of medium-confidence emails along the way. They launch Bulk Auto-Apply from the main menu, watch a live progress bar as the sweep runs unattended, and receive a summary of what was applied when it finishes.

**Why this priority**: This is the core value proposition of the feature. All other stories depend on this one working correctly.

**Independent Test**: Can be fully tested by selecting Bulk Auto-Apply from the mode selection menu against a seeded archive of mixed-confidence emails, verifying that only emails above the threshold receive actions and the progress bar completes with accurate counters.

**Acceptance Scenarios**:

1. **Given** the user selects Bulk Auto-Apply from the mode menu, **When** the sweep begins, **Then** the total untriaged count is displayed in the progress bar before any email is processed.
2. **Given** an untriaged email whose confidence meets or exceeds the configured threshold, **When** it is evaluated during the sweep, **Then** the appropriate action is applied immediately without prompting the user.
3. **Given** an untriaged email whose confidence is below the threshold, **When** it is evaluated during the sweep, **Then** it is silently skipped with no user interaction required.
4. **Given** the sweep finishes, **When** the final screen is rendered, **Then** a summary shows total processed, actions applied, and emails skipped.

---

### User Story 2 - Respect User-Configured Confidence Threshold (Priority: P2)

A cautious user has raised their auto-apply confidence threshold to 99 % in settings. When they run Bulk Auto-Apply, the sweep must honour that setting — not the system default of 95 %. Conversely, a user who lowered the threshold to 80 % wants the sweep to act more aggressively.

**Why this priority**: Ignoring user-configured thresholds would silently override a deliberate user preference, potentially applying unwanted actions at scale.

**Independent Test**: Can be fully tested by running the sweep with a modified threshold and verifying that emails near the boundary are handled according to the updated value, not the default.

**Acceptance Scenarios**:

1. **Given** the user has set a custom confidence threshold, **When** Bulk Auto-Apply loads, **Then** it reads and applies that threshold — not a hardcoded default.
2. **Given** an email with confidence exactly equal to the user's threshold, **When** evaluated, **Then** it qualifies for auto-apply (threshold is inclusive).

---

### User Story 3 - Cancellable Sweep with Preserved Progress (Priority: P3)

A user starts a Bulk Auto-Apply sweep on 5,000 emails. After 2,000 emails they need to stop. They cancel the operation. All 2,000 already-applied decisions should be preserved — nothing should be rolled back.

**Why this priority**: Long-running unattended operations must be safely interruptible. Data loss or unintended reversals would erode user trust.

**Independent Test**: Can be fully tested by triggering cancellation mid-sweep and confirming that all decisions applied before cancellation are persisted, while no further actions are taken after cancellation.

**Acceptance Scenarios**:

1. **Given** a sweep is in progress, **When** the user cancels, **Then** the operation stops gracefully without rolling back already-applied decisions.
2. **Given** cancellation occurs mid-batch, **When** the sweep terminates, **Then** a partial summary is displayed reflecting work done up to the cancellation point.

---

### User Story 4 - Redundant-Action Skipping (Priority: P4)

An email is already archived in Gmail. The model recommends "Archive". Bulk Auto-Apply should recognise the action is redundant and count the email as skipped — not re-issue the remote API call unnecessarily.

**Why this priority**: Redundant API calls waste quota and can trigger rate limits. This is a correctness and efficiency concern.

**Independent Test**: Can be fully tested by seeding the archive with emails whose current remote state already matches the model's recommendation, verifying zero API calls are issued and correct skip counts appear in the summary.

**Acceptance Scenarios**:

1. **Given** an email whose current state already matches the recommended action, **When** evaluated, **Then** it is counted as skipped, not applied, and no remote API call is made.

---

### Edge Cases

- What happens when the untriaged archive is empty? The sweep completes immediately with zero counters and an informative message to the user.
- What happens when every email is below the confidence threshold? All emails are skipped; the summary shows 0 applied.
- What happens when every email is above the confidence threshold? All emails are applied; the progress bar reaches 100 %.
- What happens when a batch remote call fails for a subset of emails? Remaining emails in the current page continue to be processed; failed emails are recorded as errors in the summary.
- What happens when the database returns a partial page near the archive boundary? The sweep handles pages smaller than the configured page size and terminates without double-processing any email.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a new top-level mode called "Bulk Auto-Apply" accessible from the main mode selection menu alongside Email Triage, Provider Settings, Train Model, and Exit.
- **FR-002**: The system MUST read the user's persisted confidence threshold before the sweep begins and apply it for every email evaluated during the sweep.
- **FR-003**: The system MUST retrieve all untriaged emails from the local archive using paginated reads until the full archive is exhausted — not a single fixed-size page.
- **FR-004**: For each untriaged email, the system MUST evaluate whether the model's recommended action qualifies for auto-apply using the same qualification logic used during interactive triage.
- **FR-005**: For qualifying emails, the system MUST apply the recommended action without prompting the user for any confirmation or keypress.
- **FR-006**: For non-qualifying emails, the system MUST skip them silently without pausing or requesting any user interaction.
- **FR-007**: The system MUST check whether a recommended action is already redundant (the email's current state already matches the recommendation) and skip it without issuing a remote API call.
- **FR-008**: The system MUST batch remote email-label modification calls by grouping emails with the same action per page, to reduce API call volume and respect rate limits.
- **FR-009**: The system MUST display a live progress indicator before any email is processed, showing at minimum: total untriaged count, emails processed, actions applied, emails skipped, and elapsed time.
- **FR-010**: The system MUST display a final summary after the sweep completes (or after cancellation) showing total processed, actions applied, emails skipped, and elapsed time.
- **FR-011**: The system MUST support cancellation at any point; all decisions applied before cancellation MUST be preserved and not rolled back.
- **FR-012**: The system MUST route the new mode through the existing application orchestration layer in the same pattern as all other operational modes.
- **FR-013**: All console output MUST use the application's standard formatted markup; raw unformatted console output is not permitted.
- **FR-014**: All service methods participating in the sweep MUST follow the Result pattern — returning success/failure values rather than throwing exceptions.

### Key Entities

- **Bulk Auto-Apply Session**: A single sweep run; tracks total emails found, emails processed, actions applied, emails skipped, errors, and elapsed time.
- **AutoApplyConfig**: User-persisted configuration holding the confidence threshold; read once at session start and applied uniformly to all emails in the sweep.
- **Untriaged Email**: An email stored in the local archive with no assigned training label; the target population for the sweep.
- **Batch Action Group**: A set of email IDs sharing the same recommended action, submitted to the remote provider as a single grouped call per page.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with any number of untriaged emails can run a complete Bulk Auto-Apply sweep and receive a final summary without a single manual keypress after initiating the mode.
- **SC-002**: The progress indicator displays the total untriaged count before the first email is processed, enabling the user to estimate remaining time.
- **SC-003**: All emails at or above the configured confidence threshold in a completed (non-cancelled) sweep receive applied actions; zero qualifying emails remain untriaged at the end.
- **SC-004**: No email below the confidence threshold receives an applied action during the sweep, regardless of archive size.
- **SC-005**: Cancelling the sweep at any point results in zero rollbacks; every action applied before cancellation is preserved in the next triage session.
- **SC-006**: Emails whose current state already matches the recommended action generate zero remote API calls and are counted as skipped.
- **SC-007**: Unit tests covering all-high-confidence, all-low-confidence, mixed batch, and mid-sweep cancellation scenarios all pass.

## Assumptions

- The sweep operates only on emails already stored in the local archive; it does not fetch new emails from the remote provider during the sweep run.
- The `Enabled` flag in AutoApplyConfig governs whether interactive triage auto-applies actions; Bulk Auto-Apply is an explicit user-initiated mode and runs regardless of that flag. The sweep displays the active threshold so the user can verify it before the sweep begins.
- The mode is available in the menu only when the same prerequisites as Email Triage are satisfied (remote provider healthy and initial scan completed), since it requires a trained model and archived emails.
- The existing auto-apply qualification, redundancy-check, and action-logging methods are reused without modification; no new business logic surfaces are required.
- The existing batch label modification capability in the email provider is sufficient for issuing all remote actions; no new provider API surface is required.
- The final summary mirrors the format of the auto-apply summary already rendered after interactive triage sessions, reusing existing summary rendering logic where possible.
- The account identifier used for the sweep defaults to the authenticated account, consistent with how Email Triage invokes the service layer.
- Pagination page size for the sweep is larger than the interactive triage page size to reduce round-trip overhead; the exact value is an implementation detail.

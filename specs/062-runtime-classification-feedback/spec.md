# Feature Specification: Runtime Classification with User Feedback Loop

**Feature Branch**: `062-runtime-classification-feedback`
**Created**: 2026-03-21
**Status**: Draft
**GitHub Issue**: #64
**Related**: #53
**Dependencies**: #61 (model training), #62 (console UI), #63 (service abstraction)
**Input**: User description: "Runtime Classification with User Feedback Loop: Implement model-based classification (actions + labels) with user correction feedback. Github issue #64"

## Implementation Context

This feature builds on substantial existing infrastructure. The following components are already implemented:

- **Classification pipeline**: `IClassificationService` + `ClassificationService` (single + batch, UI-agnostic)
- **Triage workflow**: `IEmailTriageService` (fetch → present → decide → dual-write to Gmail + training label)
- **Console UI**: `EmailTriageConsoleService` (cold-start labeling + AI-assisted modes with confidence color coding)
- **Bootstrap signal inference**: `ITrainingSignalAssigner` + `TrainingSignalAssigner` (8-rule priority table for Gmail folder-based signals)
- **User correction storage**: `UserCorrected` + `TrainingLabel` columns in `email_features` table
- **Model training**: `ActionModelTrainer` (LightGbm/SdcaMaximumEntropy with class imbalance handling)
- **Incremental updates**: `IncrementalUpdateService` (triggers when ≥50 new user corrections)
- **Model loading**: `MLModelProvider.PerformInitializationAsync` loads active model at startup
- **Batch processing**: Full triage loop with re-triage phase for archived emails

This specification focuses on the **remaining gaps** that complete the runtime classification and feedback loop.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Confidence-Based Auto-Apply for High-Confidence Actions (Priority: P1)

As a user triaging a large inbox, I want the system to automatically apply its recommended action only when it is very highly confident (default ≥95%), so I only need to manually review emails where the model is less certain. Anything below the auto-apply threshold — even at 80% confidence — still requires my explicit confirmation. Additionally, when the model recommends an action that matches the email's current state (e.g., "Archive" for an already-archived email), the system should still present it to me for confirmation at sub-threshold confidence levels, because my confirmation is a valuable training signal that strengthens future recommendations.

**Why this priority**: This is the highest-impact gap in the current system. Today every single email requires explicit user confirmation regardless of confidence level. For users with thousands of emails, this makes triage prohibitively slow. Enabling auto-apply at very high confidence (≥95%) strikes the right balance: clear-cut emails are handled automatically while borderline cases still benefit from user input — especially for emails where the recommended action already matches their state, since user confirmation of those is high-value training data.

**Independent Test**: Can be tested by running a triage session with a trained model and verifying that emails above the confidence threshold are automatically actioned, while emails below the threshold are presented for manual review.

**Acceptance Scenarios**:

1. **Given** a trained action model is loaded and the auto-apply threshold is set to 95%, **When** the system classifies an Inbox email with 97% confidence as "Delete", **Then** the delete action is applied automatically without user confirmation and the email is recorded as auto-applied in the training data.
2. **Given** the auto-apply threshold is 95%, **When** the system classifies an email with 80% confidence as "Archive", **Then** the email is presented to the user for manual review with the AI recommendation displayed — even though 80% is relatively confident, it is below the auto-apply threshold.
3. **Given** the auto-apply feature is active, **When** a batch of emails is processed, **Then** the user sees a summary of how many emails were auto-applied vs. how many were queued for manual review.
4. **Given** the user wants full control, **When** the user disables auto-apply (sets threshold to 100% or equivalent "off" setting), **Then** every email requires manual confirmation as in the current behavior.
5. **Given** an email is already in Archive and the model recommends "Archive" with 97% confidence (above threshold), **When** auto-apply evaluates this email, **Then** no Gmail API action is taken (redundant), but the training label is stored silently and the email is counted as auto-confirmed.
6. **Given** an email is already in Archive and the model recommends "Archive" with 80% confidence (below threshold), **When** the system evaluates this email, **Then** it is presented to the user for confirmation with a note that the email is already in the recommended state — the user's confirmation or correction is stored as training data.
7. **Given** an email is in Inbox and the model recommends "Archive" with 97% confidence, **When** auto-apply evaluates this email, **Then** the archive action is applied because the email's current state differs from the recommendation.

---

### User Story 2 - First-Training Bootstrap Scan from Gmail History (Priority: P1)

As a new user who has never trained a model, I want the system to automatically seed initial training data by scanning my Gmail account for strong signals (Trash = Delete, Starred/Important = Keep), so I can reach the training threshold faster and get AI recommendations sooner. Archived emails must NOT be inferred as "Archive" — they remain unlabeled for manual triage.

**Why this priority**: Without bootstrap data, users must manually label 100+ emails from scratch before getting any AI assistance. This is a critical onboarding friction point. Gmail Trash and Starred folders provide high-quality ground truth that can seed 30-70% of the initial training data automatically.

**Independent Test**: Can be tested by running the bootstrap scan against a Gmail account and verifying that Trash emails are labeled "Delete", Starred/Important emails are labeled "Keep", and archived emails remain unlabeled.

**Acceptance Scenarios**:

1. **Given** a user has authenticated with Gmail and has no existing training data, **When** the bootstrap scan runs, **Then** emails in Trash are labeled "Delete" with high inferred confidence and emails that are Starred or marked Important are labeled "Keep" with high inferred confidence.
2. **Given** a user has emails only in Archive (All Mail, no Inbox label), **When** the bootstrap scan runs, **Then** those archived emails are NOT labeled — they remain unlabeled and are queued for manual triage.
3. **Given** the bootstrap scan has completed, **When** the user views the triage session info, **Then** the labeled count reflects the bootstrapped labels and progress toward the training threshold is updated.
4. **Given** the bootstrap scan has already been run once, **When** it is triggered again, **Then** only new (not previously scanned) emails are processed — no duplicates are created.

> **Implementation Note**: Much of this story may already be covered by the existing `ITrainingSignalAssigner` + `TrainingSignalAssigner` (8-rule priority table for Gmail folder-based signal inference) and the Gmail training data pipeline from spec 058. Verify during implementation whether the current infrastructure already handles Trash→Delete and Starred/Important→Keep inference, idempotent scanning, and progress tracking — then scope this story to only the remaining gaps (if any).

---

### User Story 3 - Model Quality Monitoring and Retraining Suggestions (Priority: P2)

As a user who has been using the system for a while, I want the system to continuously track model performance behind the scenes and proactively warn me when accuracy is degrading, suggesting specific actions I can take (retrain, review problem categories, provide more examples). I should not need to notice the degradation myself — the system must detect it and surface it.

**Why this priority**: Users cannot be expected to notice gradual model drift. By the time a pattern of bad recommendations becomes obvious, dozens or hundreds of emails may have been misclassified. The system must own the responsibility of monitoring quality and alerting the user before the problem becomes severe.

**Independent Test**: Can be tested by simulating a series of user corrections that diverge from model predictions and verifying that the system automatically detects the drift and surfaces actionable warnings in the console UI without the user requesting them.

**Acceptance Scenarios**:

1. **Given** the system is tracking correction rates in the background, **When** the rolling accuracy over the last 100 reviewed emails drops below 70%, **Then** the system proactively displays a warning banner at the start of the next triage batch with the current accuracy rate, correction count, and a recommended action ("Retrain now" or "Review problem categories").
2. **Given** the user has made 50+ corrections since the last training, **When** the user starts a triage session or advances to the next batch, **Then** the system displays a retraining suggestion with a summary of correction count, estimated current accuracy, and a one-key shortcut to start retraining.
3. **Given** the user accepts the retraining suggestion, **When** retraining completes, **Then** the user sees training metrics (accuracy, per-action precision/recall) and the new model is activated automatically.
4. **Given** the user dismisses the retraining suggestion, **When** the user continues triaging, **Then** the suggestion is not shown again until either 25 additional corrections accumulate or a new session starts.
5. **Given** the system detects that a specific action category has a correction rate above 40%, **When** the quality warning is shown, **Then** it includes a targeted recommendation (e.g., "The model struggles with Archive decisions — consider providing more Archive examples during triage").
6. **Given** model accuracy drops below 50%, **When** the system detects this during triage, **Then** auto-apply is automatically disabled (if enabled), a strong warning is shown, and the system recommends retraining before continuing AI-assisted triage.

---

### User Story 4 - Per-Action Performance Tracking (Priority: P2)

As a user, I want to see which action categories the model struggles with most (e.g., frequently misclassifying "Archive" as "Keep"), so I can understand the model's weaknesses and provide targeted corrections to improve accuracy.

**Why this priority**: Aggregate accuracy metrics hide action-specific problems. A model might be 85% accurate overall but consistently misclassify a particular action. Per-action visibility helps users trust the system and focus their correction efforts where they matter most.

**Independent Test**: Can be tested by reviewing a set of emails, making corrections, and verifying that the system tracks and displays per-action accuracy alongside aggregate metrics.

**Acceptance Scenarios**:

1. **Given** the user has triaged at least 50 emails in AI-assisted mode, **When** the user views model performance stats, **Then** they see per-action metrics: accuracy, total classified, and correction rate for each action (Keep, Archive, Delete, Spam).
2. **Given** the model has a high correction rate for "Archive" actions (>40% overridden), **When** the performance stats are displayed, **Then** the "Archive" action is highlighted as needing improvement.
3. **Given** the user requests detailed stats, **When** the breakdown is shown, **Then** it includes a confusion summary (e.g., "Archive was corrected to Delete 15 times, to Keep 8 times").

---

### User Story 5 - Auto-Apply Review and Undo (Priority: P3)

As a user, I want to review what the system auto-applied and undo specific decisions if I disagree, so I maintain confidence that auto-apply won't make irreversible mistakes behind my back.

**Why this priority**: Auto-apply saves time but can reduce user trust if they can't verify or reverse decisions. A review/undo mechanism balances speed with user control and provides high-value correction signals for retraining.

**Independent Test**: Can be tested by enabling auto-apply, processing a batch, reviewing the auto-applied summary, undoing a specific decision, and verifying the Gmail action reversal and correction storage.

**Acceptance Scenarios**:

1. **Given** auto-apply has processed 20 emails in the current session, **When** the user selects "Review auto-applied", **Then** they see a list of auto-applied emails with sender, subject, applied action, and confidence score.
2. **Given** the user is reviewing auto-applied emails, **When** the user selects an email and chooses "Undo", **Then** the Gmail action is reversed (e.g., email moved back to Inbox), the training label is updated to the user's corrected action, and the email is marked as user-corrected.
3. **Given** the user undoes an auto-applied decision, **When** the correction is stored, **Then** it is weighted as a high-value correction for retraining purposes (equivalent to a direct user override).

### Edge Cases

- What happens when the trained model file is corrupted or missing at startup? The system falls back to cold-start mode and notifies the user that no model is available, without crashing.
- What happens when the bootstrap scan encounters Gmail API rate limits? The scan pauses, respects rate limits with exponential backoff, and resumes from where it stopped. Partial results are preserved.
- What happens when a user's Gmail Trash is empty (no Delete signals for bootstrap)? The system completes the bootstrap with available signals only (Starred/Important → Keep) and informs the user that Delete examples will need manual labeling.
- What happens when confidence scores cluster near the threshold (e.g., many emails at 93-95%)? All emails below the threshold are sent to manual review — there is no buffer zone or special handling. The threshold is intentionally set very high (95% default) so borderline cases always get human review.
- What happens when the model recommends an action that matches the email's current state (e.g., "Archive" for an email already in Archive)? If confidence is above the auto-apply threshold, the Gmail API call is skipped (redundant) and the training label is stored silently. If confidence is below the threshold, the email is still presented to the user for confirmation with a note that it's already in the recommended state — the user's confirmation or correction strengthens future training.
- What happens when auto-apply encounters a Gmail API failure mid-batch? The failed email is skipped (not labeled), the error is logged, and the user is notified of partial failures in the batch summary.
- What happens when the user has no Starred or Important emails and Trash is empty? The bootstrap scan completes with zero labels, the user is informed, and they proceed to manual cold-start labeling as normal.
- What happens when the model accuracy degrades to below 50%? The system disables auto-apply automatically, displays a strong warning, and strongly recommends retraining before continuing AI-assisted triage.

## Requirements *(mandatory)*

### Functional Requirements

#### Confidence-Based Auto-Apply

- **FR-001**: System MUST support a configurable confidence threshold (default: 95%) above which classified actions are applied automatically without user confirmation. The threshold MUST be set high to ensure only near-certain predictions bypass manual review.
- **FR-002**: System MUST default to requiring manual confirmation for all actions (auto-apply disabled) until the user explicitly enables it.
- **FR-003**: System MUST provide a session summary showing the count and breakdown of auto-applied actions and manually reviewed actions.
- **FR-004**: All emails classified below the auto-apply threshold MUST be presented to the user for manual confirmation, regardless of the confidence level (e.g., even at 80% confidence, the user still confirms).
- **FR-005**: System MUST automatically disable auto-apply if recent model accuracy drops below 50%, with a clear warning to the user.
- **FR-024**: When the recommended action matches the email's current state (e.g., "Archive" for an already-archived email) AND confidence is above the auto-apply threshold, the system MUST skip the Gmail API call but silently store the training label. When confidence is below the threshold, the system MUST still present the email to the user for confirmation — noting that the email is already in the recommended state — because the user's explicit confirmation or correction is valuable training data.

#### First-Training Bootstrap Scan

- **FR-006**: System MUST scan Gmail Trash folder and label those emails as "Delete" during the initial bootstrap.
- **FR-007**: System MUST scan Gmail for Starred and Important-marked emails and label those as "Keep" during the initial bootstrap.
- **FR-008**: System MUST NOT infer "Archive" as a training label for emails that are only in Archive (All Mail without an Inbox label).
- **FR-009**: System MUST track which emails have already been scanned to prevent duplicate processing on subsequent bootstrap runs.
- **FR-010**: System MUST preserve partial bootstrap results if the scan is interrupted (e.g., due to rate limiting or network failure).
- **FR-011**: System MUST display bootstrap progress to the user (number of emails scanned, labels inferred, progress toward training threshold).

#### Model Quality Monitoring

- **FR-012**: System MUST continuously track the user's correction rate (percentage of AI recommendations overridden) in the background during every triage session — the user MUST NOT need to request or check this manually.
- **FR-013**: System MUST proactively suggest retraining when the accumulated correction count since last training reaches the configured minimum (default: 50), displaying the suggestion automatically at the start of the next triage batch.
- **FR-014**: System MUST calculate rolling model accuracy based on user decisions in the last N reviewed emails (default: 100) and proactively display a warning when accuracy drops below 70%.
- **FR-015**: System MUST track per-action metrics: accuracy, total classified, and correction rate for each action category (Keep, Archive, Delete, Spam).
- **FR-016**: System MUST display a confusion summary showing what actions were corrected to which alternatives (e.g., "Archive→Delete: 15 times").
- **FR-025**: When accuracy drops below a critical threshold (default: 50%), the system MUST automatically disable auto-apply, display a strong warning, and recommend retraining before continuing AI-assisted triage.
- **FR-026**: When a specific action category has a correction rate above 40%, the system MUST include a targeted recommendation in quality warnings identifying which category is underperforming and suggesting the user focus on providing examples for that category.

#### Auto-Apply Review and Undo

- **FR-017**: System MUST maintain a reviewable log of auto-applied decisions within the current session, including email metadata, applied action, and confidence score.
- **FR-018**: System MUST allow users to undo individual auto-applied decisions, reversing both the Gmail action and updating the training label.
- **FR-019**: System MUST mark undone auto-applied decisions as high-value user corrections for retraining purposes.

#### Integration and Resilience

- **FR-020**: System MUST fall back gracefully to cold-start mode if the trained model file is missing or unloadable at startup.
- **FR-021**: System MUST handle Gmail API failures during auto-apply by skipping the failed email, logging the error, and continuing with the batch.
- **FR-022**: System MUST respect Gmail API rate limits during bootstrap scanning using backoff and retry mechanisms.
- **FR-023**: System MUST persist auto-apply configuration (threshold, enabled/disabled, buffer zone) across sessions.

### Key Entities

- **Auto-Apply Configuration**: User-configurable settings for confidence-based auto-apply, including enabled/disabled flag and confidence threshold (default: 95%). No buffer zone — everything below the threshold goes to manual review.
- **Bootstrap Scan State**: Tracks the state of the first-training Gmail history scan, including which folders have been scanned, how many emails processed, and a checkpoint for resuming interrupted scans.
- **Model Quality Metrics**: Aggregated performance data derived from user corrections, including per-action accuracy, correction rate, confusion matrix, and rolling accuracy window.
- **Auto-Apply Session Log**: Ephemeral per-session record of automatically applied actions, supporting review and undo operations during the active session.

## Assumptions

- The action model (Keep, Archive, Delete, Spam) is the only classification model in scope. Label prediction (Gmail labels/categories) was descoped in a prior decision and is not part of this feature.
- The existing `IncrementalUpdateService` with its ≥50 correction threshold is the correct retraining trigger mechanism. This feature adds monitoring and user-facing suggestions on top of it.
- Gmail API access is already authenticated and functional via the existing OAuth flow before any bootstrap or auto-apply operations begin.
- The existing `email_features` table schema with `UserCorrected` and `TrainingLabel` columns is sufficient for storing correction data — no schema changes are needed for the feedback loop.
- The auto-apply confidence threshold defaults to 95% — intentionally very high so only near-certain predictions bypass manual review. There is no buffer zone; everything below the threshold requires explicit user confirmation.
- When the model's recommended action matches the email's current Gmail state, sub-threshold emails are still presented for user confirmation because the explicit feedback is valuable training data.
- "Undo" for auto-applied actions means reversing the Gmail modification (e.g., removing from Trash, adding back to Inbox). This relies on existing Gmail API batch modify capabilities.
- Per-session auto-apply logs are ephemeral (not persisted to database); they exist only for the duration of the active triage session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users with auto-apply enabled spend at least 50% less time per triage session compared to fully manual review, measured by the ratio of auto-applied to manually reviewed emails.
- **SC-002**: The bootstrap scan seeds at least 30% of the training threshold from Gmail history signals for users who have a typical Gmail usage pattern (some Trash, some Starred emails).
- **SC-003**: 95% of auto-applied actions are correct (not subsequently undone by the user), measured over a rolling window of the last 200 auto-applied decisions.
- **SC-004**: Users receive a retraining suggestion within one session of the correction threshold being reached, and the suggestion includes actionable metrics (correction count, estimated accuracy).
- **SC-005**: Per-action performance metrics are available to the user after triaging at least 50 emails in AI-assisted mode, with accuracy and correction rates broken down by action category.
- **SC-006**: The bootstrap scan completes within a reasonable time for accounts with up to 10,000 emails across Trash, Starred, and Important folders, with visible progress updates throughout.
- **SC-007**: Auto-apply undo operations successfully reverse the Gmail action and store the correction as high-value training data in 100% of cases where the Gmail API call succeeds.

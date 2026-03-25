# Feature Specification: Archive-Then-Delete Training Labels (Time-Bounded Retention)

**Feature Branch**: `064-archive-then-delete-labels`
**Created**: 2026-03-23
**Status**: Draft
**GitHub Issue**: #83

## Overview

The triage UI already exposes three time-bounded archive labels --- `Archive for 30d`, `Archive for 1y`, and `Archive for 5y` --- but they currently behave identically to a plain `Archive`. No enforcement of the implied retention window ever happens.

This feature gives those labels meaning in two ways:

**Classification (content-based)**: The ML engine recommends a time-bounded label based on *what type of email it is*, not how old it is. A shipping notification email that arrived three years ago should still be classified as `Archive for 30d` because that is the appropriate retention category for that type of email. The label is a content classifier, not a time gate.

**Retention enforcement (age-based)**: A separate, periodic scan checks all emails in the archive that carry a time-bounded label. For any email whose received date plus the label's threshold has passed, the service deletes the email in Gmail. The stored `training_label` is never changed by this operation --- it always reflects the original content-based classification.

**Action execution (bridge)**: When an action is taken on an email during triage or bulk operations, the system checks the email's current age against the label's threshold at the moment of execution. If the email already exceeds its threshold, the physical action is Delete rather than Archive. The training label is still written as the content-based classification (e.g., `Archive for 30d`), not as `Delete`.

**EmailAgeDays** is already an email feature and is already part of model training. It must be recalculated from the email's received date at the moment of inference --- the stored integer in `email_features` is never used for classification decisions.

| Label | Key | Threshold |
|---|---|---|
| `Archive for 30d` | `2` | 30 days |
| `Archive for 1y` | `3` | 365 days |
| `Archive for 5y` | `4` | 1825 days |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - AI recommends time-bounded label based on email content (Priority: P1)

The user triages a shipping notification. The AI engine recommends `Archive for 30d` because this is a short-retention email type. The email may be brand new or three years old --- the label recommendation is the same either way, because the classification is content-driven. When the user confirms the action, the system checks the email's current age and decides whether the physical Gmail action is Archive (email is under 30 days old) or Delete (email is already past 30 days). Either way, `Archive for 30d` is stored as the training label.

**Why this priority**: The content-based classification is the foundation. It decouples the label's meaning from the email's age, which is what makes the label useful as a training signal for all future emails of the same type --- regardless of when they were received.

**Independent Test**: Can be tested by presenting the same email content at two different "current ages" (one under threshold, one over) and verifying: (a) the AI recommendation is `Archive for 30d` in both cases, (b) the physical Gmail action differs between the two cases, and (c) `email_features.training_label` is `Archive for 30d` in both cases.

**Acceptance Scenarios**:

1. **Given** an email that matches the pattern for short-term retention (e.g., a shipping notification), **When** the AI engine produces a recommendation, **Then** the recommendation is `Archive for 30d` regardless of email age
2. **Given** the AI recommends `Archive for 30d` and the email's received date is 10 days ago, **When** the user confirms, **Then** the physical Gmail action is Archive and `training_label` is stored as `Archive for 30d`
3. **Given** the AI recommends `Archive for 30d` and the email's received date is 60 days ago (already past threshold), **When** the user confirms, **Then** the physical Gmail action is Delete and `training_label` is still stored as `Archive for 30d`
4. **Given** the AI recommends `Archive for 1y` or `Archive for 5y`, **When** the user confirms, **Then** the same age-at-execution logic applies: archive if under threshold, delete if over threshold
5. **Given** EmailAgeDays is needed for classification, **When** the engine is invoked, **Then** EmailAgeDays is computed fresh from the email's received date at that moment, not read from any stored integer

---

### User Story 2 - Retention enforcement service deletes aged archived emails (Priority: P2)

The user had labeled a batch of shipping notifications as `Archive for 30d` a few months ago. Those emails were archived in Gmail at the time. A periodic retention scan now finds these emails, determines their received date plus 30 days has passed, and deletes them from Gmail. The `training_label` in `email_features` remains `Archive for 30d` throughout.

**Why this priority**: Without enforcement, the time-bounded labels are just cosmetic. The retention service is what makes the labels actionable for emails that were correctly archived but have since aged past their threshold. This is the primary mechanism for actually clearing out old emails.

**Independent Test**: Can be tested in isolation by inserting `email_features` rows with time-bounded labels and known received dates, then running the retention scan, and verifying: (a) emails past threshold are deleted in Gmail, (b) emails under threshold are not touched, (c) `training_label` is unchanged for all rows after the scan.

**Acceptance Scenarios**:

1. **Given** an email in Gmail archive with `training_label = Archive for 30d` and a received date 45 days ago, **When** the retention scan runs, **Then** the email is deleted from Gmail
2. **Given** an email in Gmail archive with `training_label = Archive for 30d` and a received date 15 days ago, **When** the retention scan runs, **Then** the email is not touched
3. **Given** an email in Gmail archive with `training_label = Archive for 1y` and a received date 400 days ago, **When** the retention scan runs, **Then** the email is deleted from Gmail
4. **Given** an email in Gmail archive with `training_label = Archive for 5y` and a received date 1000 days ago, **When** the retention scan runs, **Then** the email is not touched (1000 < 1825)
5. **Given** retention scan executes a delete, **When** the operation completes, **Then** `email_features.training_label` is still `Archive for 30d` (not overwritten to `Delete`)
6. **Given** an email with label `Archive for 30d` and received date exactly 30 days ago, **When** the retention scan runs, **Then** the email is deleted (boundary is inclusive: age >= threshold means delete)
7. **Given** a Gmail delete fails for one email during a batch scan, **When** the error occurs, **Then** the scan continues processing remaining emails; the failed email is retried or flagged, and `training_label` is not modified

---

### User Story 3 - ML model learns to classify email type into retention category (Priority: P3)

The ML training pipeline uses labeled rows to train the model to predict which retention category fits a given email's content. The model's job is to output `Archive for 30d`, `Archive for 1y`, `Archive for 5y`, `Archive`, `Keep`, or `Delete` based on the email's content features. EmailAgeDays is included as a feature because age correlates with some content patterns, but the model should generalize the label to email type, not hard-code an age rule.

**Why this priority**: This improves future recommendation quality but does not block P1 or P2. The triage UI can use user-selected labels (P1) and the retention service (P2) works independently of the classifier.

**Independent Test**: Can be tested by training the model on labeled data and verifying it produces the same label prediction for a shipping notification email regardless of whether EmailAgeDays is 5 or 500, confirming the label is driven by content patterns, not by age alone.

**Acceptance Scenarios**:

1. **Given** training data contains rows labeled `Archive for 30d`, `Archive for 1y`, and `Archive for 5y`, **When** the training pipeline runs, **Then** EmailAgeDays is included as a fresh-computed feature (from received date), not a stored stale integer
2. **Given** two training examples with identical content features but different EmailAgeDays values, **When** the model is evaluated, **Then** the predicted label is the same (label reflects email type, not age)
3. **Given** the trained model is queried with a new email, **When** inference runs, **Then** EmailAgeDays is recomputed from the email's received date at inference time before being passed to the model

---

### Edge Cases

- What happens when the email's received date is missing or unparseable? EmailAgeDays defaults to 0 for classification; the retention scan skips the email and logs a warning.
- What happens when a user manually selects `Archive for 30d` for an email already 60 days old? Physical action at that moment is Delete; `training_label` is stored as `Archive for 30d`.
- What happens if the same email is labeled twice? The latest label overwrites the prior one in `email_features`.
- What happens if the retention scan runs very frequently (e.g., on every bulk operation pass)? The scan is idempotent --- an already-deleted email will simply not be found in Gmail on subsequent passes.
- What happens to emails labeled with `Archive for 30d` in the database before this feature was activated? They are already stored correctly. The retention scan will pick them up and delete any whose received date has expired, without any data migration.
- What happens if Gmail reports the email as already deleted when the retention scan tries to delete it? The scan treats this as a successful outcome and does not modify the `training_label`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST recognize three time-bounded label values and their thresholds: `Archive for 30d` (30 days), `Archive for 1y` (365 days), `Archive for 5y` (1825 days)
- **FR-002**: The AI classification engine MUST produce label recommendations based on email content features, and the recommendation MUST be the same label for the same email content regardless of email age
- **FR-003**: At the moment of action execution (triage confirmation or bulk operation), the system MUST compare the email's current age (computed fresh from received date) against the label's threshold: if age >= threshold, execute Delete in Gmail; if age < threshold, execute Archive in Gmail
- **FR-004**: The system MUST store the content-based label (e.g., `Archive for 30d`) in `email_features.training_label` in both cases, regardless of whether the physical action was Delete or Archive
- **FR-005**: EmailAgeDays MUST be recalculated from the email's received date at the time of inference; the stored integer in `email_features` MUST NOT be used for classification or action decisions
- **FR-006**: The system MUST provide a retention enforcement service that can be triggered periodically (e.g., during bulk operations or on a scheduled scan)
- **FR-007**: The retention enforcement service MUST find all emails with time-bounded labels that are still present in Gmail and whose received date plus threshold has passed, and MUST delete them in Gmail
- **FR-008**: The retention enforcement service MUST NOT modify `email_features.training_label` when executing a retention delete
- **FR-009**: The retention scan MUST process emails independently; a Gmail delete failure for one email MUST NOT abort processing of remaining emails
- **FR-010**: The ML training pipeline MUST use EmailAgeDays computed fresh from the email's received date, not a stored stale value, when building training examples
- **FR-011**: System MUST include unit tests covering: content-based label classification independent of age, age-at-execution routing (archive vs. delete), retention scan threshold evaluation for all three labels at and beyond boundary, and preservation of `training_label` after a retention delete

### Key Entities

- **TrainingLabel**: A content-based classification of what retention category an email belongs to. `Archive for 30d`, `Archive for 1y`, and `Archive for 5y` describe the *type* of email (short, medium, or long retention), not a time-gated decision. Stored immutably in `email_features.training_label` after classification.
- **EmailFeatureVector**: A stored record in `email_features`. The `training_label` field reflects the content-based classification and is never overwritten by execution outcomes. EmailAgeDays in this record is a snapshot; it MUST be recomputed fresh before use in classification or action execution.
- **ActionExecutionDecision**: A transient, runtime-only determination at action time: given a label and the email's current age, should the physical Gmail action be Archive or Delete? This decision is not persisted.
- **RetentionEnforcementRecord**: The runtime state during a retention scan: an email with a time-bounded label whose received date plus threshold has been exceeded. The Gmail delete action is executed, but `email_features` is not modified.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The AI recommendation for a given email content type is identical regardless of EmailAgeDays, verifiable by presenting the same feature vector with two different age values and confirming the predicted label is the same
- **SC-002**: The physical Gmail action at execution time is Archive for 100% of under-threshold emails and Delete for 100% of over-threshold emails, while `training_label` is the content-based label in all cases, verifiable via unit tests with controlled received dates
- **SC-003**: The retention scan correctly deletes 100% of archived emails with expired time-bounded labels and leaves 100% of non-expired emails untouched, verifiable via automated tests with injected `email_features` rows and known received dates
- **SC-004**: `email_features.training_label` is never modified by the retention scan or by an over-threshold execution, verifiable by inspecting the database record after each operation
- **SC-005**: EmailAgeDays passed to the classification engine reflects the email's received date at inference time, verifiable by computing the expected age value from the received date and comparing it to the value actually passed to the engine during a test invocation

## Assumptions

- The three time-bounded label constant strings (`Archive for 30d`, `Archive for 1y`, `Archive for 5y`) are already present in the UI and database; this feature activates their behavioral semantics.
- The re-triage mechanism (from branch `060-console-tui-spectre`) can be extended to include the age-at-execution routing without redesign.
- The email's received date is available at the time of triage, bulk action execution, and retention scan.
- The retention scan can be invoked as part of an existing bulk operation pass or as a standalone scheduled scan; the invocation mechanism is an implementation detail.
- The Gmail API client supports archive and delete as separate, discrete operations.
- The ML training pipeline (spec `059-mlnet-training-pipeline`) supports custom pre-processing to ensure EmailAgeDays is recomputed from received date rather than read from the stored feature vector.
- Emails already stored in `email_features` with time-bounded labels and expired received dates require no migration; they will be processed by the retention scan on the next pass.

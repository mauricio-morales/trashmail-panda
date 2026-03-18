# Feature Specification: Gmail Provider Extension for Training Data

**Feature Branch**: `058-gmail-training-data`  
**Created**: 2026-03-17  
**Status**: Draft  
**Related**: GitHub Issue #60, Depends on #55 (ML Data Storage), Related to #53

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Initial Training Data Collected From Email History (Priority: P1)

As a user setting up TrashMail Panda for the first time, I want the app to scan my Gmail history across all folders — including Spam, Trash, and Archive — so the AI learns my past behavior and can make accurate classifications from day one, without me having to manually label anything.

**Why this priority**: Without historical training data, the ML classifier starts cold and cannot make useful predictions. Importing existing folder placements and labels is the fastest way to build a high-quality baseline. Everything else builds on this.

**Independent Test**: Can be fully tested by initiating a fresh scan, observing that emails are retrieved from Spam, Trash, and Archive, and confirming that classification signals (folder type + read/unread status) are recorded for each email. The system delivers value as a standalone data import step even before any classification is performed.

**Acceptance Scenarios**:

1. **Given** the user has authenticated with Gmail, **When** the system scans for training data, **Then** emails from Spam, Trash, and Archive folders are retrieved with their full label sets and read/unread status.
2. **Given** emails have been retrieved, **When** classification signals are assigned, **Then** Spam and Trash emails are each tagged as a strong "auto-delete" signal, Archive + Unread emails are tagged as "auto-archive", Archive + Read emails are excluded, Inbox + Read emails are excluded, and Inbox + Unread emails are flagged as low-confidence signals.
3. **Given** a prior scan was completed, **When** a subsequent scan is triggered, **Then** new emails not yet seen are fetched and stored, AND previously imported emails whose state has changed (e.g., moved from Archive to Trash, read status flipped, labels added or removed) have their training records updated to reflect the new state.

---

### User Story 2 - Engagement Signals Protect Emails the User Found Valuable (Priority: P2)

As a user, I want the AI to recognize when I've replied to or forwarded an email as definitive proof that I found it worth acting on, so the system never recommends auto-deleting or auto-archiving emails I actively engaged with — even if they currently live in Archive or Trash.

**Why this priority**: Replying or forwarding is the most unambiguous behavioral signal the system can passively observe. It ranks alongside starring/flagging in the existing ML training pipeline (⭐⭐⭐⭐⭐ strength per `MODEL_TRAINING_PIPELINE.md`). Missing these signals would produce the highest-impact false positives — deleting emails whose threads are still live. This belongs at P2 because it prevents catastrophic classifier errors, not just inaccuracies.

**Independent Test**: Can be tested independently by importing a set of emails marked as replied-to or forwarded and confirming that none receive a delete or auto-archive training signal, regardless of their current folder.

**Acceptance Scenarios**:

1. **Given** an email has been replied to or forwarded by the user, **When** training data is collected, **Then** the email is recorded with a "keep" engagement signal as a context feature, regardless of its folder.
2. **Given** an email in Archive that was replied to or forwarded, **When** classification signals are assigned, **Then** it is excluded from the "auto-archive without reading" signal — active engagement overrides the folder-based archive signal.
3. **Given** an email in Trash with a replied-to or forwarded flag, **When** classification signals are assigned, **Then** the delete signal is downgraded to low-confidence (user engaged, then deleted — ambiguous intent; do not train confidently on this).
4. **Given** an email in Spam with a replied-to or forwarded flag, **When** classification signals are assigned, **Then** the strong "auto-delete" spam signal is preserved — replying to spam is typically accidental and does not override the spam classification.
5. **Given** a previously imported email gains a replied or forwarded flag on an incremental scan (user acted on it after the prior import), **When** the incremental scan processes that email, **Then** the stored training record is updated to reflect the new engagement flag and any resulting signal change.

---

### User Story 3 - Existing Gmail Labels Inform the Classifier (Priority: P3)

As a user who has spent years organizing email with Gmail's label system, I want the AI to learn from the labels I've already applied to emails, so my personal taxonomy (e.g., "Receipts", "Work", "Promotions") is carried directly into the classifier without me retraining from scratch.

**Why this priority**: User-applied labels are high-confidence training signals. Each label a user created and applied represents explicit intent, making label-based data the most valuable source of supervised training signals after folder placement.

**Independent Test**: Can be tested independently by importing the user's full label taxonomy and confirming that each email with a user-applied label records that label as a positive training signal. Delivers value even if folder-based signals are not yet processed.

**Acceptance Scenarios**:

1. **Given** the user has Gmail labels, **When** the system imports the label taxonomy, **Then** all user-created labels (names, colors, types) are recorded for future classification targets.
2. **Given** an email has one or more user-applied labels, **When** training data is collected, **Then** each user-applied label on that email is recorded as a positive training signal for that label category.
3. **Given** an email has only system labels (INBOX, UNREAD, STARRED, IMPORTANT), **When** training data is collected, **Then** system labels are recorded as features (context signals), not as classification targets.
4. **Given** multiple user labels on a single email, **When** training data is recorded, **Then** multi-label support is maintained — all labels are associated with that email.

---

### User Story 4 - Large Email History Fetched Without Disrupting Gmail Access (Priority: P4)

As a user with a large Gmail history (tens of thousands of emails), I want training data to be fetched in safe batches that respect Gmail's usage limits, so the scan completes without errors, throttling, or causing my other Gmail usage to be interrupted.

**Why this priority**: A user with a large mailbox represents the primary target audience. Any quota violation or API block during the initial scan would damage trust and leave the app in a broken state. Rate-aware fetching is a correctness concern, not just a nice-to-have.

**Independent Test**: Can be tested independently by simulating a large fetch against a mailbox with 10,000+ emails and verifying that all emails are eventually retrieved, that the system pauses and retries when quota limits are encountered, and that no emails are skipped or duplicated.

**Acceptance Scenarios**:

1. **Given** a large mailbox, **When** training data is fetched, **Then** emails are retrieved in batches small enough to stay within Gmail API rate limits.
2. **Given** a quota limit is reached during a batch, **When** the system detects the limit, **Then** it waits an appropriate amount of time and resumes automatically without user intervention.
3. **Given** a partial scan was interrupted, **When** the scan resumes, **Then** progress is picked up from where it left off and previously processed emails are not re-fetched.

---

### User Story 5 - Training Data Always Written to the User's Configured Database (Priority: P2)

As a user who accepted a database location during the setup wizard (e.g., `~/Library/Application Support/TrashMailPanda/app.db` on macOS), I want all training data imported by this feature to be stored in that exact file, so there is never a situation where my emails have been scanned but the data was silently written to a different, unknown database — and I can't find it.

**Why this priority**: This is a correctness and trust issue, not a convenience one. The system currently has two fallback paths (`./data/transmail.db` as a hardcoded default and `./data/app.db` as a config fallback) that silently override the path the user accepted in the wizard. Training data written to the wrong file is effectively lost — the classifier will never see it. This must be fixed as part of this feature because it is the first feature that writes large volumes of training data to storage. P2 because it gates correctness of everything else.

**Independent Test**: Can be fully tested by running the setup wizard, accepting a database path, triggering a training data scan, and confirming that all imported emails appear in the wizard-accepted database — and that no `./data/` files are created or written to.

**Acceptance Scenarios**:

1. **Given** the user has completed the setup wizard and accepted a database path, **When** a training data scan writes records, **Then** all records are written exclusively to the wizard-accepted path with no fallback to `./data/app.db` or `./data/transmail.db`.
2. **Given** no setup wizard has been run, **When** the application starts, **Then** it uses the OS-standard application data directory (e.g., `~/Library/Application Support/TrashMailPanda/app.db` on macOS, `%APPDATA%\TrashMailPanda\app.db` on Windows) as the single authoritative default — not a relative `./data/` path inside the project folder.
3. **Given** the stale files `data/app.db` and `data/transmail.db` exist in the repository working directory, **When** the application runs, **Then** it does not read from or write to those files, and they are safe to delete.
4. **Given** the hardcoded default `./data/transmail.db` exists in `StorageProviderConfig`, **When** the storage provider initializes, **Then** the hardcoded default is replaced with the OS-standard path so no code path can create a `transmail.db` file.

---

### User Story 6 - Label Usage Frequency Guides Classifier Priorities (Priority: P6)

As a user whose email habits vary, I want the system to track how frequently I use each Gmail label, so the classifier can weight high-frequency labels as stronger signals and deprioritize rarely-used labels during training.

**Why this priority**: Label frequency provides additional signal quality information. Labels applied to hundreds of emails are reliable patterns; labels used once are outliers. This metadata makes the classifier more robust without requiring additional user input.

**Independent Test**: Can be tested independently by verifying that after a label import, each label record includes an occurrence count derived from how many emails carry that label. Delivers metadata value even if no ML training has been run yet.

**Acceptance Scenarios**:

1. **Given** training data has been collected, **When** label statistics are retrieved, **Then** each label record includes a count of how many emails carry that label.
2. **Given** a new email scan is completed, **When** label counts are updated, **Then** any new emails with user labels increment the associated label frequency.

---

### Edge Cases

- What happens when Spam or Trash folders are empty? → System completes without error, recording zero emails from those folders.
- What if replied/forwarded detection metadata is unavailable for some emails? → System defaults to IsReplied=false / IsForwarded=false and continues; those emails are not blocked from import.
- What if an email in Trash was replied to — does that mean the user wants to keep it? → Signal is downgraded to low-confidence (not deleted outright from training); the ambiguity is preserved rather than resolved arbitrarily.
- What if an email in Spam was forwarded? → The spam signal is preserved; forwarding spam is treated as accidental and does not constitute a keep signal.
- How does the system handle a temporary quota-limit error mid-batch? → Retry with backoff; do not mark those emails as processed until successfully stored.
- What if a user has hundreds of custom labels? → All labels are imported; storage must handle large label sets without truncation.
- What if an email has been re-labeled or moved after a prior scan (e.g., Archive → Trash, or a label removed)? → The incremental scan re-checks all previously imported emails for state changes, updates their stored classification signal and label associations accordingly, and recalculates label frequency counts to stay accurate.
- What if an email's state change moves it from an included signal to an excluded one (e.g., Archive + Unread read by the user, now Archive + Read)? → The training record is marked as excluded and its signal is removed so the classifier is not trained on stale data.
- What if Gmail returns partial data for an email (e.g., missing headers)? → The email is stored with available fields; missing fields are recorded as absent rather than causing a failure.
- What if the system encounters a permanently purged Trash email? → Skipped gracefully; the fetch continues.
- What if multiple Gmail accounts are connected? → Each account's training data is scoped separately.
- What if the configured database path does not exist yet when a scan starts? → The directory is created automatically and the database is initialized before the first write; no scan data is lost.
- What if `./data/app.db` or `./data/transmail.db` exist in the working directory and a user runs the app without completing the setup wizard? → The relative `./data/` files are never used as a load source; the OS-standard default path is used instead.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST retrieve emails from Spam, Trash, and Archive folders during a training data scan.
- **FR-002**: The system MUST record the read/unread status and engagement flags (replied-to, forwarded) of each retrieved email.
- **FR-003**: The system MUST retrieve all Gmail labels (both user-created and system) applied to each retrieved email.
- **FR-004**: The system MUST assign a classification signal to each retrieved email based on its folder, read/unread status, and engagement flags per the following rules (engagement flags take precedence over folder-based signals, as consistent with the ⭐⭐⭐⭐⭐ "keep" signal strength defined in `MODEL_TRAINING_PIPELINE.md`):
  - Spam folder → strong "auto-delete" signal (replied/forwarded in Spam does NOT override — likely accidental)
  - Trash folder + NOT (replied or forwarded) → "auto-delete" signal
  - Trash folder + (replied OR forwarded) → low-confidence signal (user engaged then deleted — ambiguous intent)
  - Archive + (replied OR forwarded) → excluded from training (strong engagement overrides archive placement)
  - Archive + Unread + NOT (replied or forwarded) → "auto-archive without reading" signal
  - Archive + Read → excluded from training
  - Inbox + Read → excluded from training
  - Inbox + Unread → low-confidence signal, stored with a caution flag
- **FR-005**: The system MUST import and store the user's complete Gmail label taxonomy, including label names, colors, and types (user-created vs. system).
- **FR-006**: The system MUST associate all user-applied labels on an email as positive training signals for those label categories.
- **FR-007**: The system MUST record system labels (INBOX, UNREAD, STARRED, IMPORTANT) as context features on an email, not as classification training targets.
- **FR-008**: The system MUST support multi-label association — a single email may carry multiple training label signals simultaneously.
- **FR-009**: Each incremental scan MUST both (a) identify and import emails not yet seen, and (b) detect state changes on previously imported emails — including folder moves, read/unread flips, engagement flag changes (IsReplied, IsForwarded), and label additions or removals — and update those records accordingly (upsert semantics, not skip-if-seen).
- **FR-010**: The system MUST fetch emails in batches that comply with Gmail API rate and quota limits.
- **FR-011**: The system MUST automatically pause and retry when API quota limits are reached, without user intervention.
- **FR-012**: The system MUST be able to resume an interrupted scan from the last successfully processed position.
- **FR-013**: The system MUST track per-label usage frequency (count of emails bearing each label).
- **FR-014**: The system MUST gracefully handle missing or partial email data by recording what is available and continuing the scan.
- **FR-015**: When a state change causes an email's classification signal to change (e.g., Archive + Unread → Trash, which upgrades signal from "auto-archive" to "auto-delete"), the stored signal and all derived label associations MUST be updated atomically so the training store never contains a mix of old and new state for the same email.
- **FR-016**: When a state change moves an email from an included signal to an excluded category (e.g., user reads an archived email), the training record MUST be invalidated so the classifier is not trained on the now-stale signal.
- **FR-017**: The system MUST detect and record whether each email has been replied to or forwarded by the user, storing these as canonical engagement flags (`IsReplied`, `IsForwarded`) on the training record. These extend the canonical flag model defined in `docs/architecture/ML_ARCHITECTURE.md` alongside the existing `IsRead`, `IsFlagged`, `IsImportant`, and `HasAttachment` flags.
- **FR-018**: When engagement flag detection is unavailable or inconclusive for a given email, the system MUST default to `IsReplied=false` and `IsForwarded=false` and continue processing — missing engagement data must not block the scan.
- **FR-019**: The storage provider MUST derive the active database path exclusively from (in precedence order): (1) the path saved by the setup wizard, (2) the OS-standard application data directory. Relative `./data/` paths MUST NOT be used as defaults or fallbacks at any layer (config defaults, DI registration, or `StorageProviderConfig`).
- **FR-020**: The hardcoded default value `./data/transmail.db` in `StorageProviderConfig` MUST be replaced with the OS-standard path so that no application code path can create or read a file named `transmail.db`.
- **FR-021**: If the directory for the configured database path does not exist at startup, the system MUST create it automatically before attempting to open the database.

### Key Entities

- **TrainingEmail**: An email retrieved for training purposes, carrying its folder origin, read/unread status, engagement flags (IsReplied, IsForwarded), assigned classification signal, and list of labels.
- **ClassificationSignal**: The training intent derived from folder placement and read status (auto-delete, auto-archive, low-confidence, excluded).
- **LabelTaxonomy**: The complete set of a user's Gmail labels — including name, color, type (user-created or system), and usage frequency count.
- **LabelAssociation**: A link between a specific email and a specific label, representing a positive training signal when the label is user-created.
- **ScanProgress**: A record tracking (a) which emails have been imported, (b) the last-seen state of each email (folder, read status, engagement flags, label set), and (c) the scan cursor position — used to enable incremental new-email discovery, state-change detection on existing emails, and resumable scans after interruption.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the initial scan, 100% of emails in Spam, Trash, and Archive are represented in the training data store (no silent data loss).
- **SC-002**: Classification signals are assigned correctly to 100% of retrieved emails per the signal rule table — zero emails are mistagged, including correct precedence of engagement flags over folder-based signals.
- **SC-003**: A mailbox with 10,000 emails is fully processed without a single unhandled quota error or data gap, completing within a time window determined by Gmail API limits.
- **SC-004**: Users with 50+ custom labels see all labels imported into the taxonomy with accurate frequency counts and no truncation.
- **SC-005**: A scan interrupted at any point can be resumed and completes without creating duplicate training records.
- **SC-008**: After a subsequent incremental scan, 100% of previously imported emails whose Gmail state changed (folder, read status, engagement flags, or labels) have their training records updated to reflect the new state — zero stale classification signals remain.
- **SC-006**: System labels (INBOX, UNREAD, etc.) are never recorded as classification training targets — 0% contamination of target labels with system labels.
- **SC-007**: Multi-label emails (emails carrying 2+ user labels) have all labels correctly associated as training signals — no labels dropped.
- **SC-009**: After this feature is implemented, no training data is ever written to `./data/app.db`, `./data/transmail.db`, or any path under the repository working directory — 100% of writes go to the path accepted by the user during setup (or the OS-standard default if setup has not been run).

## Assumptions

- "Archive" in Gmail is operationally defined as emails that are not in Inbox, Spam, or Trash — identified by the absence of INBOX, SPAM, and TRASH system labels.
- Label taxonomy import happens once per connection and is refreshed incrementally on subsequent scans.
- The storage system (from #55) can accept the volume of training emails and label associations produced by an initial full-mailbox scan.
- Retry behavior for quota limits uses exponential backoff as the standard approach.
- Progress tracking is persisted to durable storage so it survives app restarts.
- Gmail metadata sufficient for signal assignment (folder membership, read/unread status, label identifiers) is available per-message.
- Replied/Forwarded status is detected via message threading metadata, sent-folder analysis, or provider-specific flags; availability is best-effort — absence does not block import.
- `IsReplied` and `IsForwarded` are new canonical flags that extend the model defined in `docs/architecture/ML_ARCHITECTURE.md`; the planning phase must coordinate updating that document alongside this implementation.

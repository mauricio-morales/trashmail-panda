# Feature Specification: Console TUI with Spectre.Console

**Feature Branch**: `060-console-tui-spectre`  
**Created**: 2026-03-18  
**Status**: Draft  
**GitHub Issue**: #62  
**Dependencies**: #53 (console architecture), #61 (runtime mode model)  
**Input**: Console UI with Spectre.Console: Implement console-based TUI replacing Avalonia UI

> **Implementation Note**: Significant portions of this feature are already implemented (startup orchestration, training mode, configuration wizard, mode selection). This spec focuses on the remaining gaps — principally the Email Triage workflow, Bulk Operations, Provider Settings reconfiguration, color scheme constants, and help system — while documenting the full intended console experience for reference.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cold Start Labeling: Build Initial Training Data Before Any AI Model Exists (Priority: P1)

A brand-new user has completed the initial Gmail data scan but has not yet trained any model. They enter Email Triage mode, which operates in a manual labeling mode because no AI model is available yet. The system presents one email at a time — sender, subject, date, and snippet — and asks the user to pick an action for it (Keep / Archive / Delete / Spam) without any AI suggestions. A visible counter shows how many emails have been labeled so far out of the minimum required to enable training. At any point the user can exit back to the main menu and resume where they left off in a future session. Once the minimum threshold is reached, the system pauses and prompts the user to choose: stop labeling and go train the model now, or continue labeling more emails before training.

**Why this priority**: This is the mandatory bootstrap phase every new user must pass through before any intelligence is available. Without a clear, guided labeling workflow, users have no path to the AI-powered experience. It must be intuitive with no AI dependency and must surface the progress signal that motivates continued labeling.

**Independent Test**: Can be fully tested with zero trained models present, feeding a set of mock emails and verifying: no AI recommendation or confidence score is shown, the user can assign any action with a single keystroke, a main-menu exit option is always visible, the labeled-count progress indicator increments correctly, a transition prompt appears when the minimum threshold is crossed, and choosing "go train now" exits Email Triage. Delivers standalone value as a complete cold-start labeling session.

**Acceptance Scenarios**:

1. **Given** no trained model exists and the user enters Email Triage mode, **When** the mode loads, **Then** a clear notice explains that AI suggestions are unavailable until the minimum labeling threshold is met, and the first unprocessed email is displayed without any AI recommendation or confidence score
2. **Given** an email is displayed in cold-start labeling mode, **When** the user selects an action (Keep / Archive / Delete / Spam) from the on-screen key map, **Then** the label is persisted as a training signal, the action is executed against Gmail, and the next email is shown
3. **Given** the user is labeling emails, **When** any email is displayed, **Then** a progress indicator shows the current labeled count and the minimum threshold (e.g., "47 / 100 labels collected") so the user can see how far they are from enabling training
4. **Given** the user labels the email that reaches the minimum threshold, **When** the action is confirmed, **Then** the system pauses and displays a cyan prompt asking whether to stop labeling and go to Training now, or continue labeling more emails; no further emails are shown until the user chooses
5. **Given** the threshold prompt is shown, **When** the user chooses "Go to Training", **Then** the labeling session is cleanly exited and the user is returned to the main menu with a cyan message indicating they should now select Training
6. **Given** the threshold prompt is shown, **When** the user chooses "Continue Labeling", **Then** the prompt is dismissed and the next email is presented; the prompt does not appear again in the same session
7. **Given** the user wants to exit to the main menu at any point during labeling, **When** they press the quit key, **Then** all labels collected so far are preserved, the session exits cleanly, and a future session resumes from the next unlabeled email
8. **Given** a labeling action fails (network error), **When** the error occurs, **Then** it is displayed in bold red and the label is not counted until the action succeeds

---

### User Story 2 - AI-Assisted Triage: Review Emails With Model Recommendations (Priority: P1)

A user who has already trained at least one model enters Email Triage mode. The system now presents each email alongside an AI-generated action recommendation and a confidence score. The user can accept the recommendation with a single keystroke or override it with a different action. Every decision — whether acceptance or override — is stored as a new training signal that improves the model in future training runs. The system then advances to the next email.

**Why this priority**: This is the primary ongoing workflow and the product's core value delivery — fast, AI-accelerated inbox management. It builds on the cold-start labeling base (User Story 1) and depends on the Training mode (User Story 3) to have produced a model first.

**Independent Test**: Can be fully tested by pre-loading a trained model and a set of mock emails with pre-computed classifications, verifying each email is presented with the correct color-coded confidence score, accept and override keystrokes work correctly, every decision is recorded as a training signal, and the app advances to the next email without additional prompts.

**Acceptance Scenarios**:

1. **Given** a trained model exists and the user enters Email Triage mode, **When** the mode loads, **Then** the first unprocessed email is displayed with sender, subject, date, snippet, and the AI recommendation and confidence score clearly visible with appropriate color coding
2. **Given** an email is displayed with a high-confidence AI recommendation (≥ 80%), **When** the user presses the accept key, **Then** the action is executed, the decision is stored as a positive training signal, and the next email is shown without additional confirmation
3. **Given** an email is displayed, **When** the user disagrees with the AI recommendation and selects a different action, **Then** the override action is executed, and the correction is stored as a high-value training signal (user correction)
4. **Given** the user selects an action, **When** the action execution fails (network error, API timeout), **Then** the error is displayed in bold red with a specific description and the user may retry or skip; skipped emails are not counted as training signals
5. **Given** the user wants to return to the main menu during triage, **When** they press the quit key, **Then** session state is preserved, the user is returned to the main menu, and the next session resumes from the last unprocessed email
6. **Given** all emails in the current batch have been triaged, **When** no more unprocessed emails remain, **Then** a session summary is displayed showing total emails processed, actions taken, overrides made, and time elapsed, and the user is returned to the main menu

---

### User Story 3 - Training Mode: Monitor Training Progress and Evaluate Results (Priority: P2)

A user initiates the model training workflow. The console displays a live multi-phase progress view with labeled bars for each training stage. When training completes, a formatted metrics table shows accuracy, precision, recall, and F1 scores per class. Quality advisories flag if the model may not meet minimum thresholds, and the user receives explicit confirmation before the model is saved.

**Why this priority**: Training mode is partially implemented. The remaining gaps — model evaluation display completeness, save confirmation, and quality advisory messaging — must be completed to give users full visibility into model quality before it goes live.

**Independent Test**: Can be tested by running a training session against a known dataset, verifying all progress phases are shown sequentially, the metrics table displays per-class breakdowns, and a model quality advisory appears when F1 scores fall below threshold.

**Acceptance Scenarios**:

1. **Given** the user initiates training, **When** each pipeline phase executes (data loading, pipeline building, training, evaluation), **Then** a labeled progress bar advances in real time with a distinct color indicating phase status (cyan for active, green for complete, yellow for slow, red for failed)
2. **Given** training completes, **When** the metrics report is shown, **Then** accuracy, precision, recall, and F1 scores are displayed in a formatted table with per-class rows, using magenta for metric values
3. **Given** a completed training run with overall F1 below the quality threshold, **When** the metrics report is displayed, **Then** a yellow advisory clearly states the model may not be suitable and recommends collecting more labeled data before activating
4. **Given** training produces a model that passes quality thresholds, **When** the user is prompted to save, **Then** confirmation is required before the new model becomes active, and a green success message is shown on save

---

### User Story 4 - Configuration UI: Reconfigure Providers and Manage Storage (Priority: P2)

A user who has already completed first-time setup enters Provider Settings mode to update their Gmail credentials, view current storage usage, adjust the storage limit, or re-run the initial scan. All settings changes are confirmed with clear success or error feedback.

**Why this priority**: Without a functional reconfiguration UI, users who encounter token expiry, credential changes, or quota issues have no recovery path within the application.

**Independent Test**: Can be tested independently by navigating to Provider Settings, modifying Gmail credentials, verifying the stored values update correctly (without logging the token values), and confirming the change is reflected in subsequent health checks.

**Acceptance Scenarios**:

1. **Given** the user enters Provider Settings, **When** the menu loads, **Then** current configuration status for Gmail and storage is shown (configured/not configured) alongside available actions
2. **Given** the user chooses to reconfigure Gmail, **When** they follow the OAuth re-authorization flow, **Then** new tokens are securely stored and a green confirmation is displayed
3. **Given** the user views storage settings, **When** the screen loads, **Then** current database usage, email archive count, feature vector count, and the configurable storage limit are shown
4. **Given** the user adjusts the storage limit, **When** they save, **Then** the new limit is persisted and any cleanup rules relying on the limit are updated accordingly
5. **Given** any configuration change fails (invalid credentials, network error, permission denied), **When** the error occurs, **Then** a bold red error message with a specific description is shown and no partial state is saved

---

### User Story 5 - Help System: Access Key Bindings and Command Reference (Priority: P3)

A user who is unfamiliar with the application or who wants to recall a keyboard shortcut can invoke a help command at any point. The help system displays context-aware key bindings for the current mode, plus a general command reference.

**Why this priority**: A help system reduces friction for new users and is expected in any professional CLI/TUI application. It is a supporting capability that enhances all other modes.

**Independent Test**: Can be tested by pressing the help key in each mode and verifying that context-appropriate key bindings and descriptions are shown, then confirming the user can return to the previous screen.

**Acceptance Scenarios**:

1. **Given** the user is in any mode, **When** they press the help key (`?` or `F1`), **Then** a formatted help panel appears listing available keys and their actions for the current context
2. **Given** the help panel is open, **When** the user presses the dismiss key, **Then** the help panel closes and the user returns to the exact state they left
3. **Given** the user is on the main menu, **When** they view help, **Then** a brief application description and version number are also displayed

---

### User Story 6 - Consistent Color Scheme Across All Modes (Priority: P3)

All console output across startup, menus, email triage, training, and configuration uses a consistent semantic color scheme. Errors always appear in bold red, success in green, metrics in magenta, highlights in cyan, information in blue, and warnings in yellow. Color constants are defined centrally so enforcement is systematic rather than ad hoc.

**Why this priority**: Color consistency builds professional polish and reduces cognitive load when scanning output. Centralized constants prevent drift as new UI components are added.

**Independent Test**: Can be tested by reviewing all output-generating components against the color scheme specification, and by running the application end-to-end and visually confirming each message type uses the correct color.

**Acceptance Scenarios**:

1. **Given** any error condition occurs anywhere in the application, **When** the error message is displayed, **Then** it appears in bold red with the error description also in red (not just the icon or prefix)
2. **Given** any success event occurs (provider init, action executed, model saved), **When** the confirmation is displayed, **Then** it appears in green
3. **Given** training metrics values are shown, **When** they are rendered, **Then** numeric metric values appear in magenta
4. **Given** a centralized color scheme definition exists, **When** a developer adds new console output, **Then** they can reference named constants (not raw markup strings) for each semantic color

---

### Edge Cases

- What happens when the terminal does not support ANSI color codes? → Application gracefully degrades to uncolored output; no markup tags appear as raw text
- What happens when the user has zero emails in their Gmail during Email Triage? → A clear message explains there are no emails to triage and the user is returned to the main menu
- What happens when a triage session is interrupted mid-action (Ctrl+C)? → The in-progress action is not partially applied; the email is marked as unprocessed for the next session
- What happens when the user enters Email Triage mode without a trained model and without having started labeling yet? → Cold-start labeling mode begins immediately; the first email is shown with a notice that AI suggestions are not yet available
- What happens if the user dismisses the threshold prompt and continues labeling, but then decides later to go train? → They press the quit/return key to exit to the main menu and navigate to Training from there; progress is not lost
- What happens if the user reaches the threshold, chooses "Continue Labeling", labels many more emails, and then exits? → All labeled emails are preserved; the Training option remains accessible from the main menu at any time after the threshold has been met
- What happens when training data is insufficient (< 100 labeled samples)? → Training does not start; a yellow advisory explains the minimum data requirement and the current labeled count
- What happens when the terminal window is too narrow to render tables? → Columns are wrapped or truncated gracefully without breaking layout or hiding critical information
- What happens when a provider goes offline mid-triage session? → The current email action fails with a bold red error; the user may retry or exit to the main menu

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The application MUST present emails in Email Triage mode one at a time, displaying sender, subject, date, and a snippet regardless of whether a trained model exists
- **FR-002**: The application MUST detect whether a trained model is available before entering Email Triage mode and route to cold-start labeling mode (no AI suggestions) or AI-assisted triage mode accordingly
- **FR-003**: In cold-start labeling mode the application MUST NOT display any AI recommendation or confidence score, and MUST show a notice explaining that suggestions will be available once the training threshold is met
- **FR-004**: The application MUST display a live progress indicator showing the current labeled count versus the minimum threshold required to enable first-time training (e.g., "47 / 100 labels collected")
- **FR-005**: When the minimum labeling threshold is crossed, the application MUST pause and present an interactive prompt (in cyan) offering two choices: "Go to Training" (exit Email Triage and return to main menu) or "Continue Labeling" (dismiss and keep going); no further emails are shown until the user selects
- **FR-006**: In AI-assisted triage mode the application MUST display the AI action recommendation (Keep / Archive / Delete / Spam) with a confidence score for each email
- **FR-007**: Users MUST be able to select any of the four actions with a single keystroke in both triage modes; in AI-assisted mode they may also accept the recommendation with a single keystroke
- **FR-007a**: A "Return to Main Menu" option MUST be visible on screen at all times during both cold-start labeling and AI-assisted triage, and selecting it MUST cleanly exit the current session and return the user to the main menu without losing any previously stored labels or training signals
- **FR-008**: The application MUST store every triage decision (accept or override) as a training signal before advancing to the next email
- **FR-009**: The application MUST execute the selected action against Gmail before advancing to the next email
- **FR-010**: The application MUST preserve triage session state so an interrupted session can be resumed from the last unprocessed email
- **FR-011**: The application MUST display a session summary when a triage batch is complete (emails processed, actions taken, overrides made, time elapsed)
- **FR-012**: Training mode MUST display multi-phase live progress with a labeled progress bar per phase
- **FR-013**: Training mode MUST display a formatted per-class metrics table (accuracy, precision, recall, F1) after each training run
- **FR-014**: Training mode MUST issue a quality advisory when model metrics fall below the minimum acceptable threshold
- **FR-015**: Training mode MUST require explicit user confirmation before saving a newly trained model as the active model
- **FR-016**: Provider Settings mode MUST allow re-authorization of Gmail without requiring a full re-installation or manual credential deletion
- **FR-017**: Provider Settings mode MUST display current storage usage (bytes, email count, feature vector count)
- **FR-018**: Provider Settings mode MUST allow the user to adjust the storage limit
- **FR-019**: All error messages throughout the application MUST be displayed in bold red with a descriptive message also in red
- **FR-020**: All success confirmations MUST be displayed in green
- **FR-021**: All training metric values MUST be displayed in magenta
- **FR-022**: A centralized color scheme definition MUST exist so all color markup is referenced by semantic name, not raw markup strings
- **FR-023**: A help system MUST be accessible at any point via a dedicated key, displaying context-aware key bindings for the active mode
- **FR-024**: The application MUST degrade gracefully to uncolored output on terminals that do not support ANSI colors
- **FR-025**: The Bulk Operations mode MUST allow users to execute Delete, Archive, and Label actions across a set of emails meeting user-defined criteria

### Key Entities

- **Email Triage Session**: Represents an active triage session with current email index, total count, actions taken, overrides made, labeled count, and start time; also tracks whether it is in cold-start labeling mode or AI-assisted mode
- **Triage Decision**: A user's action decision for a single email (email ID, chosen action, original AI recommendation if any, confidence score if any, whether it was an override, timestamp)
- **Color Scheme**: A centralized mapping of semantic names (Error, Success, Warning, Info, Metric, Highlight, ActionHint, Dim) to Spectre.Console markup strings
- **Help Context**: Mode-specific key binding definitions that the help panel renders at runtime
- **Bulk Operation Criteria**: Filter parameters for selecting emails in bulk (sender, label, date range, size, AI confidence threshold)

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can complete a triage decision (view email → select or accept action → next email) in under 5 seconds per email in both cold-start labeling mode and AI-assisted mode once familiar with the interface
- **SC-002**: No raw markup syntax (e.g., `[red]`) appears in any console output when run on a standard color-capable terminal
- **SC-003**: Users can complete Gmail re-authorization without leaving the application in under 3 minutes following an expired token scenario
- **SC-004**: All four application modes (Email Triage, Training, Configuration, Bulk Operations) are fully functional (no "Coming soon" stubs remain)
- **SC-005**: Training mode displays progress updates at least every 2 seconds so the application never appears frozen during a training run
- **SC-006**: 100% of error messages across all modes use bold red for both the error indicator and the error text (no partially-colored errors)
- **SC-007**: Developers can apply any semantic color by referencing a named constant, confirmed by zero occurrences of hardcoded markup strings outside the color scheme definition
- **SC-008**: The help panel is accessible and dismissable within 2 keystrokes from any mode

---

## Assumptions

- The `TrashMailPanda` project remains the single runnable console app entry point; no separate `TrashMailPanda.Console` project is created unless a future spec calls for it
- Avalonia UI (`UIMode`) will remain as a stub mode rather than being removed in this feature — removal is a separate architectural decision
- Email action execution uses the existing Gmail provider (`IEmailProvider`) API surface rather than requiring new provider capabilities
- The minimum labeling threshold count (e.g., 100 labeled samples) is defined by the ML pipeline (spec #059) and consumed by this feature as a configuration value; this feature is not responsible for setting or changing the threshold
- Terminal capability detection (ANSI support) is handled by Spectre.Console automatically; no custom capability detection is needed
- The four triage actions (Keep, Archive, Delete, Spam) are fixed and match the ML model's output classes

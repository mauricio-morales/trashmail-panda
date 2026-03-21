# Data Model: Backend Refactoring for UI Abstraction

**Feature**: 061-backend-ui-abstraction
**Date**: 2026-03-21

## Overview

This feature introduces no new database tables. All new types are in-memory domain
models (records and enums) used for classification results and application lifecycle
events. Existing storage schemas from previous features are unchanged.

---

## New Types

### 1. ClassificationResult (Record)

Immutable output from `IClassificationService`. Wraps the richer output of ML
prediction with explicit reasoning source attribution.

| Field | Type | Description |
|-------|------|-------------|
| `EmailId` | `string` | Unique email identifier from `EmailFeatureVector.EmailId` |
| `PredictedAction` | `string` | One of: `"Keep"`, `"Archive"`, `"Delete"`, `"Spam"` |
| `Confidence` | `float` | Confidence score [0.0, 1.0] from model or rule-based |
| `ReasoningSource` | `ReasoningSource` | Enum: `ML` or `RuleBased` |

**Relationships**: Maps 1:1 from `EmailFeatureVector` input. Derived from `ActionPrediction` (ML provider output).

**Validation Rules**:
- `EmailId` must not be null or whitespace
- `PredictedAction` must be one of the four valid actions
- `Confidence` must be in range [0.0, 1.0]

**State Transitions**: None — immutable record, created once.

---

### 2. ReasoningSource (Enum)

Indicates whether a classification was produced by the ML model or by the rule-based
cold-start fallback.

| Value | Description |
|-------|-------------|
| `ML` | Classification produced by a trained ML.NET model |
| `RuleBased` | Classification produced by rule-based fallback (cold-start or model unavailable) |

---

### 3. ApplicationEvent (Base Record) and Subtypes

Discriminated event hierarchy for the unified event stream emitted by
`IApplicationOrchestrator`. Uses standard .NET `EventHandler<ApplicationEventArgs>`
pattern.

#### 3a. ApplicationEvent (abstract base)

| Field | Type | Description |
|-------|------|-------------|
| `Timestamp` | `DateTimeOffset` | When the event was created |
| `EventType` | `string` | Discriminator (derived from concrete type name) |

#### 3b. ModeSelectionRequestedEvent : ApplicationEvent

Emitted when the orchestrator is ready for user mode selection.

| Field | Type | Description |
|-------|------|-------------|
| `AvailableModes` | `IReadOnlyList<OperationalMode>` | Modes available based on provider health |

#### 3c. BatchProgressEvent : ApplicationEvent

Emitted during batch classification progress.

| Field | Type | Description |
|-------|------|-------------|
| `ProcessedCount` | `int` | Number of emails processed so far in current batch |
| `TotalCount` | `int` | Total emails in current batch |
| `EstimatedSecondsRemaining` | `double?` | Estimated time remaining (null if unknown) |

#### 3d. ProviderStatusChangedEvent : ApplicationEvent

Emitted when a provider's health state changes.

| Field | Type | Description |
|-------|------|-------------|
| `ProviderName` | `string` | Name of the provider (e.g., "Storage", "Gmail") |
| `IsHealthy` | `bool` | New health state |
| `StatusMessage` | `string?` | Optional descriptive message |

#### 3e. ApplicationWorkflowCompletedEvent : ApplicationEvent

Emitted when the application workflow loop exits.

| Field | Type | Description |
|-------|------|-------------|
| `ExitCode` | `int` | Process exit code (0 = success) |
| `Reason` | `string` | Human-readable exit reason |

---

### 4. ApplicationEventArgs (EventArgs)

Standard `EventArgs` wrapper for the event stream.

| Field | Type | Description |
|-------|------|-------------|
| `Event` | `ApplicationEvent` | The concrete event instance (pattern-match to access specific fields) |

---

## Existing Types (Referenced, Not Modified)

| Type | Location | Role |
|------|----------|------|
| `EmailFeatureVector` | `Providers.Storage.Models` | Input to classification — 38-feature vector |
| `ActionPrediction` | `Providers.ML.Models` | Raw ML.NET output; mapped to `ClassificationResult` |
| `ClassificationMode` | `Providers.ML.Models` | Enum: ColdStart, Hybrid, MlPrimary |
| `OperationalMode` | `Models.Console` | Enum: EmailTriage, BulkOperations, ProviderSettings, TrainModel, UIMode, Exit |
| `TriageSessionInfo` | `Models.Console` | Session snapshot used by `IEmailTriageService` |
| `Result<T>` | `Shared.Base` | Result pattern wrapper used by all service methods |
| `ProviderStatusChangedEventArgs` | `Services` | Existing event args in `IProviderStatusService` |

---

## Entity Relationship Summary

```
Program.cs
    └─→ IApplicationOrchestrator.RunAsync(ct)
            ├─→ ConsoleStartupOrchestrator (existing, delegated)
            ├─→ ModeSelectionMenu (existing, delegated)
            ├─→ IEmailTriageConsoleService (existing, dispatched)
            ├─→ IBulkOperationConsoleService (existing, dispatched)
            ├─→ IProviderSettingsConsoleService (existing, dispatched)
            ├─→ TrainingConsoleService (existing, dispatched)
            ├─→ GmailTrainingScanCommand (existing, delegated)
            ├─→ IClassificationService (new, called during triage)
            │       └─→ IMLModelProvider (existing, delegated)
            └─⇢ event ApplicationEventRaised → ConsoleEventRenderer (new)
                                                  └─→ Spectre.Console output
```

No database schema changes. No migrations needed.

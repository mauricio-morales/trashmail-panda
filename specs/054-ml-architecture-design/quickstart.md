# Quickstart: ML Architecture Design

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14

## What This Feature Produces

Three architecture documents in `/docs/`:

| Document | Purpose |
|----------|---------|
| `ML_ARCHITECTURE.md` | System architecture — layers, components, data flow, provider integration |
| `FEATURE_ENGINEERING.md` | Feature extraction pipeline — what signals are extracted and how |
| `MODEL_TRAINING_PIPELINE.md` | Training workflow — cold start, full training, incremental updates, versioning |

## Primary Use Case: Archive Reclamation

The tool's **core value** is helping users reclaim storage from accumulated archived emails. The ML pipeline:

1. **Scans all mailbox folders** via any email provider (Gmail, IMAP, Outlook, etc.)
2. **Bootstraps training** from existing folder signals (Trash → delete, Starred/Inbox → keep)
3. **Classifies archived emails** and recommends bulk deletions with confidence scores
4. **Learns from decisions** — user accepts/rejects feed back into the model

Incoming email classification is a secondary, steady-state workflow.

## Key Design Decisions

1. **ML.NET** as the classification framework (native .NET, good multi-class support)
2. **Two-tier features**: Structured (sender, auth, time, folder placement) + Text (TF-IDF on subject/body)
3. **Three classification modes**: Cold Start (rules only) → Hybrid (ML + rules) → ML Primary
4. **New `IClassificationProvider`** interface extending `IProvider<TConfig>` (replaces `ILLMProvider` for classification)
5. **`IFeatureExtractor`** service for email → feature vector transformation
6. **`IModelTrainer`** service for training lifecycle management
7. **SQLite schema extensions**: `email_features`, `email_archive`, `archive_triage_results`, `ml_models`, `training_events`, `user_corrections`
8. **File-based model versioning** with metadata in SQLite. Last 5 versions retained for rollback.
9. **Provider-agnostic feature engineering**: All features derived from canonical email metadata (sender, date, size, canonical folder, canonical flags). No provider-specific concepts leak into the ML model.
10. **Multi-provider email support**: Canonical folder semantics (Inbox, Trash, Spam, Archive, Flagged, etc.) map uniformly across Gmail labels, IMAP folders, and Outlook folders+categories.
11. **Topic signals deferred to Phase 2+**: Schema reserves nullable fields for topic clusters, sender categories, and semantic embeddings. Phase 1 uses TF-IDF only. Phases 2-3 add LDA topic modeling and local ONNX embeddings (no LLM). LLM-based extraction is Phase 4 and strictly optional. See R10 in research.md.

## Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│                       Console TUI                         │
│        (Spectre.Console — triage, classify, train)        │
└─────────────┬──────────────────────┬─────────────────────┘
              │                      │
   ┌──────────▼──────────┐  ┌───────▼────────┐
   │IClassificationProv  │  │ IModelTrainer   │
   │ ┌─────────────────┐ │  │ Train / Eval /  │
   │ │ML.NET Prediction │ │  │ Rollback / Prune│
   │ │Engine            │ │  └───────┬────────┘
   │ │                  │ │          │
   │ │TriageArchiveAsync│ │          │
   │ └────────┬────────┘ │          │
   └──────────┼──────────┘          │
              │                     │
   ┌──────────▼──────────┐          │
   │ IFeatureExtractor   │◄─────────┘
   │ Email → FeatureVec  │
   │ (canonical folders)  │
   └──────────┬──────────┘
              │
   ┌──────────▼──────────────────────────────┐
   │         SQLite (SQLCipher)               │
   │  email_features │ email_archive          │
   │  ml_models      │ training_events        │
   │  user_corrections│ archive_triage_results│
   └──────────┬──────────────────────────────┘
              │
   ┌──────────▼──────────┐
   │ data/models/        │
   │  model_v1_*.zip     │
   │  model_v2_*.zip     │
   └─────────────────────┘
              │
   ┌──────────▼──────────────────────────────┐
   │         IEmailProvider (abstract)        │
   │  Gmail │ IMAP │ Outlook │ Yahoo │ ...   │
   │  (maps native folders → canonical)       │
   └─────────────────────────────────────────┘
```

## Multi-Provider Email Support

The ML pipeline operates on **canonical email metadata** — a provider-agnostic representation. Each provider adapter normalizes its native concepts:

| Canonical | Gmail | IMAP | Outlook |
|-----------|-------|------|---------|
| `Inbox` | INBOX label | INBOX folder | Inbox folder |
| `Trash` | TRASH label | Trash folder | DeletedItems |
| `Spam` | SPAM label | Junk folder | JunkEmail |
| `Flagged` | STARRED label | \Flagged flag | flagStatus |
| `Archive` | All Mail (no INBOX) | Archive folder | Archive folder |
| `UserFolder` | User labels | User folders | User folders |

See R9 in `research.md` for full mapping table.

## Constitution Compliance

| Principle | How Satisfied |
|-----------|---------------|
| Provider-Agnostic | `IClassificationProvider` extends `IProvider<TConfig>`; features use canonical folders/flags; no provider-specific concepts in ML pipeline |
| Result Pattern | All methods return `Result<T>` — no exceptions |
| Security First | Features/archive in SQLCipher; no external API calls |
| One Public Type/File | Architecture specifies this for all new types |
| Null Safety | All new types use explicit nullable annotations; ThreadId nullable for non-threading providers |
| Test Coverage | 95% for providers, 100% for data pipeline security |

## Implementation Sequence (for future issues)

1. **Issue #54** (this): Architecture docs ← YOU ARE HERE
2. **Issue #55** (planned): Local email storage system (schema + archive service)
3. **Issue #56** (planned): Feature extraction pipeline implementation
4. **Issue #57** (planned): ML.NET model training pipeline
5. **Issue #58** (planned): IClassificationProvider implementation
6. **Issue #59** (planned): Console TUI with Spectre.Console

## Files to Reference

| Artifact | Path |
|----------|------|
| Plan | `specs/054-ml-architecture-design/plan.md` |
| Research | `specs/054-ml-architecture-design/research.md` |
| Data Model | `specs/054-ml-architecture-design/data-model.md` |
| IClassificationProvider | `specs/054-ml-architecture-design/contracts/IClassificationProvider.md` |
| IFeatureExtractor | `specs/054-ml-architecture-design/contracts/IFeatureExtractor.md` |
| IModelTrainer | `specs/054-ml-architecture-design/contracts/IModelTrainer.md` |
| Parent Architecture | `docs/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md` |

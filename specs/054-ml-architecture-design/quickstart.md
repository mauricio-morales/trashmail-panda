# Quickstart: ML Architecture Design

**Feature**: #54 — ML Architecture Design  
**Date**: 2026-03-14

## What This Feature Produces

Three architecture documents in `/docs/`:

| Document | Purpose | Status |
|----------|---------|--------|
| [`ML_ARCHITECTURE.md`](../../../docs/ML_ARCHITECTURE.md) | System architecture — layers, components, data flow, provider integration | ✅ Complete |
| [`FEATURE_ENGINEERING.md`](../../../docs/FEATURE_ENGINEERING.md) | Feature extraction pipeline — what signals are extracted and how | ✅ Complete |
| [`MODEL_TRAINING_PIPELINE.md`](../../../docs/MODEL_TRAINING_PIPELINE.md) | Training workflow — cold start, full training, incremental updates, versioning | ✅ Complete |

## Key Takeaways

1. **Archive reclamation is the primary use case** — scan mailbox folders → bootstrap from Trash/Spam/Starred signals → train model → classify archived emails → recommend bulk deletions with confidence scores
2. **Provider-agnostic design** — canonical folder abstraction (Inbox, Archive, Trash, Spam, Flagged) maps uniformly across Gmail labels, IMAP folders, Outlook folders+categories, enabling multi-provider support
3. **Three-phase training** — Cold Start (<100 labels, rules only) → Hybrid (100-500, ML + rules) → ML Primary (500+, ML-first)
4. **44 feature fields** — Tier 1 structured (23), Archive-specific (9), Tier 2 text (6), Phase 2+ topic signals (6 reserved)
5. **ML.NET native classification** — no external API calls, local processing, SQLCipher encryption for all email data
6. **Complete model lifecycle** — versioning (`model_v{N}_{timestamp}.zip`), rollback, automatic retraining (50+ corrections, 7-day schedule), 5-version retention
7. **Performance targets** — <10ms classification, <50ms feature extraction with text, <5min training for 100K emails

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

| Artifact | Path | Description |
|----------|------|-------------|
| **Architecture Documents** | | |
| ML Architecture | [`docs/ML_ARCHITECTURE.md`](../../../docs/ML_ARCHITECTURE.md) | System architecture, component interactions, archive reclamation workflow, canonical folder abstraction |
| Feature Engineering | [`docs/FEATURE_ENGINEERING.md`](../../../docs/FEATURE_ENGINEERING.md) | 44 feature fields, extraction logic, provider compatibility, schema versioning |
| Training Pipeline | [`docs/MODEL_TRAINING_PIPELINE.md`](../../../docs/MODEL_TRAINING_PIPELINE.md) | Three-phase training, bootstrapping, retraining triggers, model versioning |
| **Planning Artifacts** | | |
| Plan | `specs/054-ml-architecture-design/plan.md` | Tech stack, architecture layers, file structure plan |
| Research | `specs/054-ml-architecture-design/research.md` | R1-R10 architecture decisions, performance targets |
| Data Model | `specs/054-ml-architecture-design/data-model.md` | Complete SQL schema for email_features, ml_models, archive_triage_results |
| **Contracts** | | |
| IClassificationProvider | `specs/054-ml-architecture-design/contracts/IClassificationProvider.md` | Classification provider interface, ClassifyAsync, TriageArchiveAsync |
| IFeatureExtractor | `specs/054-ml-architecture-design/contracts/IFeatureExtractor.md` | Feature extraction service interface |
| IModelTrainer | `specs/054-ml-architecture-design/contracts/IModelTrainer.md` | Model training lifecycle interface |
| **Supporting Docs** | | |
| Parent Architecture | [`docs/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md`](../../../docs/ARCHITECTURE_SHIFT_TO_LOCAL_ML.md) | High-level shift from OpenAI to ML.NET |

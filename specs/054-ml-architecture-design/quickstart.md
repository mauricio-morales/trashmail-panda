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

## Key Design Decisions

1. **ML.NET** as the classification framework (native .NET, good multi-class support)
2. **Two-tier features**: Structured (sender, auth, time) + Text (TF-IDF on subject/body)
3. **Three classification modes**: Cold Start (rules only) → Hybrid (ML + rules) → ML Primary
4. **New `IClassificationProvider`** interface extending `IProvider<TConfig>` (replaces `ILLMProvider` for classification)
5. **`IFeatureExtractor`** service for email → feature vector transformation
6. **`IModelTrainer`** service for training lifecycle management
7. **SQLite schema extensions**: `email_features`, `email_archive`, `ml_models`, `training_events`, `user_corrections`
8. **File-based model versioning** with metadata in SQLite. Last 5 versions retained for rollback.

## Architecture Overview

```
┌──────────────────────────────────────────────────────┐
│                    Console TUI                        │
│     (Spectre.Console — classify, train, status)      │
└──────────────┬──────────────────────┬────────────────┘
               │                      │
    ┌──────────▼──────────┐  ┌───────▼────────┐
    │ IClassificationProv │  │ IModelTrainer   │
    │ ┌─────────────────┐ │  │ Train / Eval /  │
    │ │ML.NET Prediction │ │  │ Rollback / Prune│
    │ │Engine            │ │  └───────┬────────┘
    │ └────────┬────────┘ │          │
    └──────────┼──────────┘          │
               │                     │
    ┌──────────▼──────────┐          │
    │ IFeatureExtractor   │◄─────────┘
    │ Email → FeatureVec  │
    └──────────┬──────────┘
               │
    ┌──────────▼──────────────────────────────┐
    │         SQLite (SQLCipher)               │
    │  email_features │ email_archive          │
    │  ml_models      │ training_events        │
    │  user_corrections│ (existing tables...)  │
    └─────────────────────────────────────────┘
               │
    ┌──────────▼──────────┐
    │ data/models/        │
    │  model_v1_*.zip     │
    │  model_v2_*.zip     │
    └─────────────────────┘
```

## Constitution Compliance

| Principle | How Satisfied |
|-----------|---------------|
| Provider-Agnostic | `IClassificationProvider` extends `IProvider<TConfig>` |
| Result Pattern | All methods return `Result<T>` — no exceptions |
| Security First | Features/archive in SQLCipher; no external API calls |
| One Public Type/File | Architecture specifies this for all new types |
| Null Safety | All new types use explicit nullable annotations |
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

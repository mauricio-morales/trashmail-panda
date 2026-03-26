---
name: Architecture Shift to Local ML
description: Planned migration from OpenAI to ML.NET with console TUI, dual classification models
type: project
---

**Status (as of 2026-03-14):** Planning phase. Avalonia UI already removed (063-remove-avalonia-ui complete). Console TUI with Spectre.Console is the permanent UI.

**Why:** Privacy-first open-source tool. Users own their model. No cloud services. MCP-compatible.

## Dual ML Models (ML.NET)
- **Action Classification Model** — multi-class: keep / archive / delete / spam
- **Label Classification Model** — multi-label: predict Gmail labels to apply (one email → multiple labels)
- Separate models for flexibility and independent performance tuning

## Classification Signal Rules (Gmail training data)
- Spam folder → strong "auto-delete" signal
- Trash folder → "auto-delete" signal
- Archive + Unread → "auto-archive without reading" signal
- Archive + Read → exclude from training (user wanted to read)
- Inbox + Read → exclude from training (user engaged)
- Inbox + Unread → unclear, use with caution

## Service Abstraction Pattern
```
Console UI → IApplicationOrchestrator → IClassificationService → IMLModelProvider
                                      → IEmailProvider
                                      → IStorageProvider
```

## Performance Targets
- Feature extraction: <50ms/email
- Action classification: <50ms/email
- Label classification: <100ms/email
- Combined inference: <150ms
- Training 10K emails: action model <3min, label model <5min
- Storage: <50GB typical user

## Implementation Phases
1. Foundation: Architecture, local storage, ML.NET infrastructure
2. Configuration & Auth: Console OAuth (Gmail + OpenAI optional), startup orchestration
3. Data Pipeline: Gmail extension for training data, backend refactor
4. Training mode
5. UI & Runtime: Console UI, runtime classification with user feedback loop
6. Polish: Performance optimization, documentation

**How to apply:** When working on classification or ML features, always consider dual-model architecture. OpenAI is optional/legacy — prefer ML.NET for core classification.

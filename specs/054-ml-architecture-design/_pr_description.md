# Pull Request: Feature #054 - ML Architecture Design

## Summary

This PR delivers comprehensive architecture documentation for TrashMail Panda's transition from OpenAI GPT-4o-mini to local ML.NET-based email classification. Three production-ready architecture documents define the complete system design, enabling implementation of issues #55-59.

**Branch**: `054-ml-architecture-design`  
**Feature**: ML Architecture Design (Documentation Deliverable)  
**Status**: ✅ **Ready for Review** — All success criteria satisfied (10/10)

---

## Documents Delivered

### 1. [ML_ARCHITECTURE.md](docs/ML_ARCHITECTURE.md) — System Architecture (16 sections, ~17k tokens)

Defines the complete system architecture including:
- **5-layer architecture**: UI, Services, Providers, Data Layer, Storage
- **IClassificationProvider integration**: How the new provider fits into existing `IProvider<TConfig>` framework with Result<T> patterns
- **Archive reclamation workflow**: Primary use case — 8-step process from mailbox scan to bulk deletion recommendations with storage estimates
- **Canonical folder abstraction**: Provider-agnostic email metadata enabling Gmail/IMAP/Outlook support through unified folder semantics
- **Data storage**: SQLCipher encryption, 50GB configurable cap, automatic pruning strategy
- **Provider compatibility matrix**: Complete mapping for 6 email providers (Gmail, IMAP, Outlook, Yahoo, iCloud, ProtonMail)
- **Performance targets**: <10ms classification, <50ms feature extraction with text
- **Security & privacy**: Zero external API calls, local ML.NET processing only

### 2. [FEATURE_ENGINEERING.md](docs/FEATURE_ENGINEERING.md) — Feature Extraction (14 sections, ~11k tokens)

Specifies the complete feature extraction pipeline:
- **44 feature fields** defined with types and extraction logic:
  - **Tier 1 Structured** (23 fields): SenderDomain, authentication (SPF, DKIM, DMARC), EmailAgeDays, time patterns
  - **Archive-Specific** (9 fields): IsInInbox, IsStarred, WasInTrash, WasInSpam, SenderFrequency
  - **Tier 2 Text** (6 fields): Subject/Body TF-IDF, LinkCount, HasTrackingPixel, UnsubscribeLinkInBody
  - **Phase 2+ Reserved** (6 fields): TopicClusterId, SenderCategory, SemanticEmbeddingJson (for future LDA/ONNX)
- **Provider-agnostic extraction**: All features derived from `CanonicalEmailMetadata` (not provider-specific types)
- **Schema versioning**: `FeatureSchemaVersion` tracking for model compatibility checks
- **Performance optimization**: Feature caching, parallel batch extraction, sender frequency pre-computation

### 3. [MODEL_TRAINING_PIPELINE.md](docs/MODEL_TRAINING_PIPELINE.md) — Training Workflow (18 sections, ~26k tokens)

Documents the complete model training lifecycle:
- **Three-phase training**: Cold Start (<100 labels, rules only) → Hybrid (100-500, ML+rules) → ML Primary (500+, ML-first)
- **Bootstrapping strategy**: Use existing folder placement as training labels (Trash/Spam → "delete", Starred/Inbox → "keep", Archive → triage target)
- **Provider-agnostic bootstrapping**: Works identically for Gmail labels, IMAP folders, Outlook folders+categories using canonical semantics
- **Retraining triggers**: 50+ corrections, 50+ triage decisions, 7-day schedule, feature schema changes
- **Model versioning**: `model_v{N}_{timestamp}.zip` naming, metadata in `ml_models` table, rollback procedure, 5-version retention
- **Archive reclamation integration**: User accept/reject decisions feed back into model training for continuous improvement
- **Training performance**: <2min for 10K emails, <5min for 100K emails
- **Model evaluation**: Accuracy, macro-F1, micro-F1, per-class precision/recall metrics

---

## Validation Summary

### ✅ All Requirements Satisfied

| Category | Status | Details |
|----------|--------|---------|
| **Functional Requirements** (FR-001 to FR-017) | ✅ 17/17 PASS | Cross-referenced in [`_validation_report.md`](specs/054-ml-architecture-design/_validation_report.md) |
| **Success Criteria** (SC-001 to SC-010) | ✅ 10/10 PASS | Reviewed in [`_success_criteria_review.md`](specs/054-ml-architecture-design/_success_criteria_review.md) |
| **Constitution Compliance** (Principles I-VII) | ✅ 7/7 PASS | Provider-agnostic, Result<T>, security-first, MVVM, one type/file, null safety, test coverage |
| **Performance Targets** | ✅ All documented | Classification <10ms, feature extraction <50ms, training <5min for 100K |
| **Provider Compatibility** | ✅ 6 providers | Gmail, IMAP, Outlook, Yahoo, iCloud, ProtonMail with canonical folder mappings |

### Key Quality Metrics

- **Total documentation**: ~54,000 tokens (~135 pages equivalent)
- **Total sections**: 48 sections across 3 documents
- **Code examples**: 40+ code blocks with proper syntax highlighting (C#, SQL, JSON, Bash)
- **Workflow diagrams**: 8+ state machines and data flow diagrams
- **Cross-references**: 12+ explicit cross-document links validated

---

## Constitution Compliance

All documents demonstrate compliance with project constitution:

| Principle | Evidence |
|-----------|----------|
| **I. Provider-Agnostic Architecture** | Canonical folder abstraction, `CanonicalEmailMetadata`, provider adapter contracts |
| **II. Result Pattern** | All interface methods return `Result<T>`, no exceptions from providers |
| **III. Security First** | SQLCipher encryption, OS keychain for tokens, zero external API calls (local ML.NET only) |
| **IV. MVVM with CommunityToolkit** | N/A (documentation-only deliverable) |
| **V. One Public Type Per File** | Documented pattern for all new types (`IClassificationProvider`, `IFeatureExtractor`, `IModelTrainer`) |
| **VI. Strict Null Safety** | All schemas use explicit nullability (e.g., `ThreadId?`, `TopicClusterId?`) |
| **VII. Test Coverage & Quality Gates** | Implementation testing strategy defined (95% provider coverage, 100% security coverage) |

---

## Primary Use Case: Archive Reclamation

The architecture documents **archive reclamation** as the core value proposition:

1. **Scan all mailbox folders** — Inbox, Archive, Trash, Spam, Sent, user folders/labels
2. **Bootstrap training data** — Existing folder placement provides pseudo-labels (Trash/Spam → "delete", Starred/Inbox → "keep")
3. **Train initial model** — ML.NET multi-class classifier on folder-based labels
4. **Classify archived emails** — Predict "keep" or "delete" with confidence scores
5. **Group by sender/topic** — Aggregate into bulk deletion recommendations
6. **Present to user** — Console TUI shows groups with storage reclaim estimates
7. **Execute decisions** — User accepts/rejects; deletions executed via `IEmailProvider.BatchDeleteAsync`
8. **Continuous improvement** — User decisions feed back into training for model refinement

**Example workflow**: User with 10,000 archived emails → 695 folder-based labels → train model → classify 8,500 archived emails → predict 3,200 deletions (~12GB reclaim) → user reviews/accepts → model retrains with feedback.

---

## Multi-Provider Support

The architecture is **provider-agnostic** using canonical folder semantics:

| Provider | Native Concept | Canonical Mapping |
|----------|---------------|-------------------|
| **Gmail** | INBOX label | `inbox` |
| **Gmail** | TRASH label | `trash` |
| **Gmail** | SPAM label | `spam` |
| **Gmail** | STARRED label | `flagged` |
| **IMAP** | INBOX folder | `inbox` |
| **IMAP** | Trash folder | `trash` |
| **IMAP** | Junk folder | `spam` |
| **IMAP** | \Flagged flag | `flagged` |
| **Outlook** | Inbox folder | `inbox` |
| **Outlook** | DeletedItems folder | `trash` |
| **Outlook** | JunkEmail folder | `spam` |
| **Outlook** | flagStatus property | `flagged` |

**Result**: ML training pipeline and feature extraction work identically across all providers. No provider-specific logic in classification engine.

---

## Implementation Roadmap

These architecture documents enable the following implementation issues:

| Issue | Deliverable | Dependencies |
|-------|-------------|--------------|
| **#54** | Architecture docs (this PR) | None |
| **#55** | Local email storage system (`email_features`, `email_archive` tables, SQLCipher setup) | #54 |
| **#56** | Feature extraction pipeline (`IFeatureExtractor` implementation) | #55 |
| **#57** | ML.NET model training pipeline (`IModelTrainer` implementation) | #55, #56 |
| **#58** | `IClassificationProvider` implementation (ML.NET `PredictionEngine` integration) | #55, #56, #57 |
| **#59** | Console TUI with Spectre.Console (archive triage workflow) | #58 |

**First implementation step**: Issue #55 (Local Email Storage) can begin immediately after this PR merges.

---

## Files Changed

### New Documentation Files

- ✅ `docs/ML_ARCHITECTURE.md` (system architecture, 16 sections)
- ✅ `docs/FEATURE_ENGINEERING.md` (feature extraction, 14 sections)
- ✅ `docs/MODEL_TRAINING_PIPELINE.md` (training workflow, 18 sections)

### Updated Planning Files

- ✅ `specs/054-ml-architecture-design/quickstart.md` (added document locations, key takeaways)
- ✅ `specs/054-ml-architecture-design/tasks.md` (all 70 tasks marked complete)
- ✅ `specs/054-ml-architecture-design/spec.md` (status updated to "Complete", completion date added)

### Validation Artifacts

- ✅ `specs/054-ml-architecture-design/_validation_report.md` (FR-001 to FR-017 cross-reference, constitution compliance)
- ✅ `specs/054-ml-architecture-design/_success_criteria_review.md` (SC-001 to SC-010 verification)
- ✅ `specs/054-ml-architecture-design/_outline.md` (document structure plan)
- ✅ `specs/054-ml-architecture-design/_foundational.md` (consolidated research/data-model/contracts reference)

---

## Testing Strategy

This is a **documentation-only deliverable** — no source code changes, no executable tests.

**Validation performed**:
- ✅ All functional requirements addressed (17/17)
- ✅ All success criteria satisfied (10/10)
- ✅ Constitution compliance verified (7/7 principles)
- ✅ Cross-document consistency checked (terminology, cross-references)
- ✅ Code syntax highlighting verified (C#, SQL, JSON, Bash)
- ✅ Performance targets documented
- ✅ Provider compatibility tables complete

**Future implementation testing** (defined in documents):
- Unit tests: 95% coverage for `IFeatureExtractor`, `IModelTrainer` implementations
- Provider tests: 95% coverage for `IClassificationProvider` implementation
- Security tests: 100% coverage for SQLCipher encryption, OS keychain storage
- Integration tests: Archive reclamation workflow, model training, feature extraction

---

## Review Checklist

### For Reviewers

- [ ] **Completeness**: Do all three documents address their respective user stories (US1-US3)?
- [ ] **Consistency**: Are canonical folder semantics used uniformly across all documents?
- [ ] **Constitution compliance**: Do interfaces follow `IProvider<TConfig>` with Result<T> returns?
- [ ] **Provider-agnostic design**: Is there any provider-specific logic in the ML pipeline?
- [ ] **Performance targets**: Are all targets realistic and documented?
- [ ] **Architecture clarity**: Can a developer implement issues #55-59 from these docs alone?

### Pre-Merge Checklist

- [x] All tasks in `tasks.md` marked complete (70/70)
- [x] Functional requirements satisfied (FR-001 to FR-017)
- [x] Success criteria met (SC-001 to SC-010)
- [x] Constitution compliance verified
- [x] Cross-document references validated
- [x] Code syntax highlighting verified
- [x] `quickstart.md` updated with document locations
- [x] `spec.md` status updated to "Complete"

---

## Related Issues

- **Parent Issue**: #54 — Define ML-based architecture and feature extraction strategy
- **Depends On**: None
- **Blocks**: #55 (Local Email Storage), #56 (Feature Extraction), #57 (Model Training), #58 (Classification Provider), #59 (Console TUI)

---

## Additional Context

### Why This Matters

TrashMail Panda's shift from OpenAI GPT-4o-mini to local ML.NET classification requires:
1. **Privacy**: No external API calls, all email processing local
2. **Cost**: Eliminate per-email API costs
3. **Speed**: <10ms classification vs. ~500ms LLM API latency
4. **Offline**: Works without internet connection

These architecture documents provide the complete blueprint for that transition while maintaining:
- **Provider-agnostic design** — works with Gmail, IMAP, Outlook, etc.
- **Result pattern** — deterministic error handling
- **Security-first** — SQLCipher encryption, OS keychain token storage

### Next Steps After Merge

1. **Create issue #55** (Local Email Storage) with subtasks from `specs/054-ml-architecture-design/data-model.md`
2. **Create issue #56** (Feature Extraction) with subtasks from `FEATURE_ENGINEERING.md`
3. **Create issue #57** (Model Training) with subtasks from `MODEL_TRAINING_PIPELINE.md`
4. Update project roadmap to reflect architecture completion milestone

---

**Ready for review** ✅  
**All validation checks pass** ✅  
**Implementation roadmap clear** ✅

# Implementation Plan: ML Data Storage System

**Branch**: `055-ml-data-storage` | **Date**: 2026-03-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/055-ml-data-storage/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Implement local storage system for ML feature vectors, complete email archives, and metadata to enable ML model training and continuous improvement. The system extends the existing SQLite/SQLCipher database with three new tables: `email_features` (lightweight feature vectors), `email_archive` (full email BLOB storage), and `storage_quota` (monitoring and cleanup). Storage enforces a configurable cap (default 50GB) with automatic pruning that removes oldest full email archives first while preserving feature data. User-corrected emails receive higher retention priority. The implementation adds `EmailArchiveService` to the Storage provider with methods for storing/retrieving features and archives, monitoring usage, and executing automatic cleanup.

## Technical Context

**Language/Version**: C# 12, .NET 9.0  
**Primary Dependencies**: Microsoft.Data.Sqlite 9.0.8, SQLitePCLRaw.bundle_e_sqlcipher 2.1.11, System.Text.Json (built-in)  
**Storage**: SQLite with SQLCipher encryption (existing encrypted database at `data/app.db`)  
**Testing**: xUnit 2.9.2 with Moq for mocking, coverlet for coverage  
**Target Platform**: Cross-platform desktop (Windows/macOS/Linux) via Avalonia UI 11
**Project Type**: Desktop application - extending existing Storage provider  
**Performance Goals**: Feature storage <100ms per email, batch retrieval of 1000 feature vectors <500ms, cleanup operations <5min per cycle  
**Constraints**: Storage limit configurable (default 50GB), feature vectors ~5-10KB each, full emails ~50-100KB each, encrypted database operations  
**Scale/Scope**: Support for 100-1000 emails/day, 5-10M feature vectors within 50GB limit, months to years of email history

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Provider-Agnostic Architecture** | ✅ PASS | Extends existing `IStorageProvider` which implements `IProvider<TConfig>`. New `EmailArchiveService` methods added to storage interface. No direct dependencies on email or ML providers. |
| **II. Result Pattern** | ✅ PASS | All new methods return `Result<T>` types: `Result<bool>` for storage operations, `Result<EmailFeatureVector>` for retrieval, `Result<StorageUsage>` for monitoring. No exceptions thrown from provider code. |
| **III. Security First** | ✅ PASS | All data stored in existing SQLCipher-encrypted database. Email content stored in encrypted BLOBs. No logging of email bodies or sensitive content. Parameterized queries for all database operations. |
| **IV. MVVM with CommunityToolkit** | N/A | This is data layer implementation; no UI components. |
| **V. One Public Type Per File** | ✅ PASS | Each entity (EmailFeatureVector, EmailArchiveEntry, StorageQuota) in separate file. EmailArchiveService in separate file. Internal helper classes allowed within files. |
| **VI. Strict Null Safety** | ✅ PASS | All projects have `<Nullable>enable</Nullable>`. All optional fields explicitly marked with `?` (e.g., `string? BodyText`, `int? TopicClusterId`). |
| **VII. Test Coverage & Quality Gates** | ✅ PASS | Target 95% coverage for Storage provider extension. Unit tests for each CRUD operation. Integration tests for cleanup and quota enforcement. Security tests for encryption verification. |

**Overall**: ✅ **APPROVED for Phase 0**

**Re-evaluation Required**: After Phase 1 design completion, verify data model entities follow one-type-per-file rule and all service methods use Result<T> pattern.

---

### Post-Phase 1 Re-evaluation (2026-03-14)

| Principle | Re-evaluation Status | Phase 1 Verification |
|-----------|---------------------|----------------------|
| **I. Provider-Agnostic Architecture** | ✅ PASS | IEmailArchiveService interface defined in contracts/. All methods operate on generic EmailFeatureVector and EmailArchiveEntry types, not provider-specific entities. |
| **II. Result Pattern** | ✅ PASS | All 16 interface methods return Result<T> types. Error types properly documented: ValidationError, QuotaExceededError, StorageError. |
| **III. Security First** | ✅ PASS | data-model.md specifies TEXT columns for email content with transparent SQLCipher encryption. All queries use parameterized statements. No sensitive data in logs. |
| **V. One Public Type Per File** | ✅ PASS | Project structure specifies separate files: EmailFeatureVector.cs, EmailArchiveEntry.cs, StorageQuota.cs, FeatureSchema.cs, EmailArchiveService.cs, IEmailArchiveService.cs. |
| **VI. Strict Null Safety** | ✅ PASS | All domain models in data-model.md use explicit nullable annotations: `string?`, `int?`, `DateTime?`. Required fields use non-nullable types. |

**Final Verdict**: ✅ **ALL CONSTITUTION PRINCIPLES SATISFIED**

Design artifacts (data-model.md, contracts/IEmailArchiveService.cs, quickstart.md) are complete and compliant. Ready to proceed to Phase 2 (task generation with `/speckit.tasks`).

## Project Structure

### Documentation (this feature)

```text
specs/055-ml-data-storage/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── IEmailArchiveService.cs  # Public API contract for email archive operations
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Providers/Storage/TrashMailPanda.Providers.Storage/
├── SqliteStorageProvider.cs           # Existing - extend with archive methods
├── EmailArchiveService.cs             # NEW - archive operations service
├── Models/                            # NEW - domain entities
│   ├── EmailFeatureVector.cs          # Feature vector entity
│   ├── EmailArchiveEntry.cs           # Full email archive entity  
│   ├── StorageQuota.cs                # Quota monitoring entity
│   └── FeatureSchema.cs               # Schema version tracking
└── Migrations/                        # NEW - schema evolution
    └── Migration_001_MLStorage.cs     # Initial ML tables

src/Shared/TrashMailPanda.Shared/
└── Base/
    └── IStorageProvider.cs            # MODIFY - add archive methods

src/Tests/TrashMailPanda.Tests/
├── Unit/
│   └── Storage/
│       ├── EmailArchiveServiceTests.cs       # NEW - service unit tests
│       ├── EmailFeatureVectorTests.cs        # NEW - entity tests
│       └── StorageQuotaTests.cs              # NEW - quota logic tests
└── Integration/
    └── Storage/
        ├── ArchiveStorageIntegrationTests.cs # NEW - E2E archive tests
        └── StorageCleanupIntegrationTests.cs # NEW - cleanup workflow tests
```

**Structure Decision**: This feature extends the existing Storage provider project (`TrashMailPanda.Providers.Storage`) rather than creating a new project. New domain entities are organized under `Models/` subdirectory. Migration logic is isolated in `Migrations/` subdirectory. This maintains the existing provider structure while adding ML-specific storage capabilities. No new projects required - complies with constitution principle of minimizing complexity.

## Complexity Tracking

**No constitution violations** - all gates passed in Constitution Check above. No additional justification required.

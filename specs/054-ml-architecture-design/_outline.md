# Documentation Outline for ML Architecture Design

**Feature**: #54  
**Created**: 2026-03-14

## Target Documents

### 1. ML_ARCHITECTURE.md (User Story 1 - Tasks T009-T025)

**Purpose**: System architecture defining component interactions and provider integration

**Sections**:
1. Overview (project context, architectural shift)
2. System Architecture (layer separation)
3. Provider Integration (IProvider pattern, DI lifecycle)
4. Archive Reclamation Workflow (scan → classify → recommend)
5. Canonical Folder Abstraction (universal folder mapping)
6. Data Storage (SQLCipher, storage cap, pruning)
7. Component Interaction Diagrams
8. Provider Adapter Contract
9. Provider Compatibility Matrix
10. Performance Targets
11. Security & Privacy
12. Constitution Compliance
13. Multi-Provider Support
14. Future Extension Points
15. References

### 2. FEATURE_ENGINEERING.md (User Story 2 - Tasks T026-T040)

**Purpose**: Feature extraction specification transforming emails to feature vectors

**Sections**:
1. Overview (purpose, input/output)
2. Tier 1 Structured Features (40+ enumerated)
3. Archive-Specific Features (age, frequency, folder signals)
4. Tier 2 Text Features (TF-IDF, links, tracking)
5. EmailFeatureVector Schema (complete field list)
6. Extraction from Canonical Metadata
7. Provider Compatibility
8. Feature Extraction Pipeline (IFeatureExtractor interface)
9. Schema Versioning (compatibility, regeneration)
10. Performance Characteristics
11. Phase 2+ Topic Signals (LDA, ONNX, optional LLM)
12. Edge Cases
13. Feature Importance
14. References

### 3. MODEL_TRAINING_PIPELINE.md (User Story 3 - Tasks T041-T059)

**Purpose**: Complete training workflow including cold start, retraining, versioning

**Sections**:
1. Overview (training pipeline purpose, ML.NET)
2. Three Training Phases (Cold Start, Hybrid, ML Primary)
3. Cold Start Procedures (bootstrapping from folders)
4. Training Data Sources (priority order)
5. Archive Reclamation Bootstrapping
6. Provider-Agnostic Bootstrapping
7. Retraining Triggers (automatic, manual)
8. Incremental Update Strategy
9. Model Versioning (file naming, metadata)
10. Rollback Procedure
11. Model Lifecycle States
12. IModelTrainer Interface Usage
13. Training Performance
14. Archive Triage Integration
15. Model Evaluation
16. Failure Scenarios
17. Training Event Audit Log
18. References

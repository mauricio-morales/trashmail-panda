# Feature Specification: ML.NET Model Training Infrastructure

**Feature Branch**: `059-mlnet-training-pipeline`  
**Created**: 2026-03-17  
**Status**: Draft  
**GitHub Issue**: #61  
**Dependencies**: #54 (feature engineering design), #55 (ML data storage)  
**Input**: ML.NET Model Training Infrastructure: Implement ML.NET training pipeline for email classification (actions + labels)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Train Email Action Classification Model (Priority: P1)

A user has accumulated enough labeled emails (from folder placement signals and explicit corrections) to train a model that predicts the recommended action for any email: Keep, Archive, Delete, or Spam. They initiate training from the console and receive a trained, evaluated model ready for use.

**Why this priority**: The action classifier is the core value-delivering component — without it, no intelligent triage recommendations are possible. All other model work depends on this foundation.

**Independent Test**: Can be fully tested by feeding a set of pre-labeled feature vectors into the training pipeline, verifying a model is produced, evaluation metrics are generated, and the model correctly classifies held-out test emails. Delivers value as a standalone working action classifier.

**Acceptance Scenarios**:

1. **Given** at least 100 labeled email feature vectors are stored, **When** the user initiates action model training, **Then** the system trains a multi-class model predicting one of four actions (Keep, Archive, Delete, Spam) and reports accuracy, precision, recall, and F1 scores
2. **Given** a training run completes, **When** the model is saved, **Then** it is versioned with a unique identifier, timestamp, and evaluation metrics so older versions remain accessible
3. **Given** a newly trained action model, **When** a batch of 100 emails is classified, **Then** each email receives exactly one action label and a confidence score, and the batch completes within acceptable time bounds
4. **Given** training data has class imbalance (e.g., far more "keep" than "delete" labels), **When** training runs, **Then** the pipeline applies balancing strategies so minority classes (Delete, Spam) are not systematically under-predicted
5. **Given** a trained action model that is later found to be lower quality, **When** the user requests rollback, **Then** the system restores the previous model version and the prior one becomes active for classification

---

### User Story 2 - Train Email Label Prediction Model (Priority: P2)

A user wants the system to automatically suggest which Gmail labels should be applied to incoming emails, based on how they historically labeled similar messages. They train a label prediction model that can recommend multiple labels per email with individual confidence scores.

**Why this priority**: Label prediction multiplies the utility of the system — it not only decides what to do with an email, but also organises it correctly. It is a distinct multi-label problem with its own training challenges (class imbalance, variable label counts) that must be solved independently.

**Independent Test**: Can be fully tested in isolation by providing feature vectors with multi-label ground truth, training the model, and verifying that held-out emails receive ranked label predictions with confidence scores. Delivers value as a standalone working label recommender.

**Acceptance Scenarios**:

1. **Given** labeled email feature vectors with associated Gmail label history, **When** the label model is trained, **Then** the system produces a multi-label classifier that can predict multiple labels per email, each with an individual confidence score
2. **Given** an email classified by the label model, **When** retrieving predictions, **Then** labels are returned in descending confidence order and the caller can request the top-k most likely labels (e.g., top 3)
3. **Given** some labels are rare in the training corpus, **When** the model trains, **Then** class imbalance handling ensures rare labels are still evaluated and not silently ignored
4. **Given** a minimum confidence threshold is configured per label, **When** the model makes predictions, **Then** only predictions meeting or exceeding each label's threshold are surfaced, preventing low-confidence noise
5. **Given** an email whose content resembles a label the model has never seen, **When** classification runs, **Then** the model gracefully returns no prediction for that label rather than a spurious assignment

---

### User Story 3 - View Model Training Progress and Metrics (Priority: P2)

A user initiating a training run wants real-time progress feedback in the console and, after training, a clear summary of model quality metrics so they can decide whether the model is ready to use.

**Why this priority**: Without progress visibility, users cannot tell if training is working or stuck. Without metrics, they cannot make informed decisions about model quality. Together they form an essential feedback loop, though secondary to the core training capability.

**Independent Test**: Can be tested independently by triggering a training run against a test dataset and verifying the console shows incremental progress updates and a final metrics report (accuracy, precision, recall, F1) is displayed on completion.

**Acceptance Scenarios**:

1. **Given** a training run is initiated, **When** the pipeline is executing, **Then** the console displays progress updates (e.g., data loaded, feature pipeline built, training in progress, evaluation complete) rather than appearing frozen
2. **Given** training completes, **When** the metrics report is displayed, **Then** it shows accuracy, precision, recall, and F1 score for each class (action model) or each label (label model), not just an overall aggregate
3. **Given** a training run that surfaces poor metrics (e.g., F1 < 0.70 overall), **When** the report is shown, **Then** the output clearly flags that the model may not meet the quality threshold and advises whether to use it or collect more data

---

### User Story 4 - Incrementally Update Models with New Corrections (Priority: P3)

After initial training, users continue correcting classifications over time. Rather than retraining from scratch each time, the system supports incremental updates that incorporate new corrections efficiently, keeping models current without requiring full retraining every session.

**Why this priority**: Incremental updates are critical for a continuously-learning experience, but the system is already valuable with periodic full retraining. This delivers improved efficiency as a follow-on capability.

**Independent Test**: Can be tested by training a baseline model, adding a batch of new labeled samples, triggering an incremental update, and verifying that model performance on the new samples improves without evaluating the full corpus again.

**Acceptance Scenarios**:

1. **Given** a trained model exists and 50+ new user corrections have accumulated since the last training, **When** an incremental update is triggered, **Then** the model is updated to reflect the new corrections without losing knowledge from prior training data
2. **Given** an incremental update completes, **When** the updated model is versioned, **Then** it receives a new version identifier and the previous version is retained for rollback
3. **Given** fewer than 50 new corrections have accumulated, **When** an incremental update is requested, **Then** the system notifies the user that insufficient new data is available and suggests waiting for more corrections

---

### User Story 5 - Store, Version, and Manage Trained Models (Priority: P3)

As the system trains and retrains models over time, users need a predictable versioning system so that previous models can be compared, restored, and eventually pruned without unexpected data loss.

**Why this priority**: Model management is essential for long-term system reliability and user trust, but it is a supporting capability that builds on the core training stories above.

**Independent Test**: Can be tested by training multiple model versions, querying the version list, rolling back to an earlier version, and verifying automatic pruning removes the oldest versions once the retention limit is exceeded.

**Acceptance Scenarios**:

1. **Given** multiple training runs have been executed, **When** the user queries model history, **Then** each version is listed with its training date, algorithm, evaluation metrics, and active status
2. **Given** more than 5 model versions exist for a model type, **When** a new version is saved, **Then** the oldest version beyond the retention limit is automatically deleted from storage (but its metadata audit record is preserved)
3. **Given** the active model is replaced by a newer version, **When** the prior model is still within the retention window, **Then** it can be restored as the active model at any time

---

### Edge Cases

- What happens when there is insufficient training data (fewer than 100 labeled emails)? The system must decline training with a clear message and indicate how many more labels are needed, falling back to rule-based classification.
- How does the system handle training data where all emails have the same action label (degenerate dataset)? Training is blocked with a diagnostic message indicating class diversity requirements.
- What if training is interrupted mid-run (crash, cancellation)? No partially-trained model should replace the current active model; the previous active model remains in service.
- How are emails with no associated action label handled during action model training? They are excluded from the training set with a logged warning; they do not cause a failure.
- What if the label model is asked to predict labels for an email from a source with no historical label patterns? The model returns an empty prediction list with a zero-confidence indicator rather than forcing a guess.
- How does the system behave if feature schema version in stored vectors does not match what the training pipeline expects? Training is blocked with a schema version mismatch error, and the user is directed to re-extract features.
- What happens when a model rollback is requested but no prior version exists? The system reports there is no prior version to restore and the current model remains active.
- How does the system handle a label model where the top-k value exceeds the number of known labels? It returns all known labels ranked by confidence without error.

## Requirements *(mandatory)*

### Functional Requirements

#### Action Classification Model

- **FR-001**: System MUST provide an action classification model that assigns exactly one action per email from the set: Keep, Archive, Delete, Spam
- **FR-002**: System MUST train the action model using supervised learning on stored email feature vectors with user-confirmed or folder-bootstrapped action labels
- **FR-003**: System MUST apply class balancing during action model training to prevent systematic under-prediction of minority action classes (Delete, Spam)
- **FR-004**: System MUST evaluate the action model and report per-class accuracy, precision, recall, and F1 score after every training run
- **FR-005**: System MUST produce a confidence score (0.0–1.0) alongside each action prediction

#### Label Classification Model

- **FR-006**: System MUST provide a label classification model that predicts zero or more Gmail labels per email, each with an individual confidence score
- **FR-007**: System MUST support configurable minimum confidence thresholds per label so low-confidence predictions are suppressed
- **FR-008**: System MUST support top-k label retrieval, returning only the k highest-confidence label predictions for a given email
- **FR-009**: System MUST handle class imbalance in label training data so rare labels are evaluated and not silently suppressed
- **FR-010**: System MUST gracefully return no prediction (empty set, not an error) for labels the model has not encountered in training

#### Shared Training Infrastructure

- **FR-011**: System MUST provide a unified feature extraction pipeline shared by both the action and label models, consuming stored email feature vectors as input
- **FR-012**: System MUST validate feature schema version compatibility before training; if stored feature vectors were extracted with an incompatible schema version, training MUST be blocked with a diagnostic error
- **FR-013**: System MUST display real-time training progress in the console, including distinct phases: loading data, building feature pipeline, training, and evaluating
- **FR-014**: System MUST support incremental model updates incorporating new user corrections without requiring full retraining from scratch
- **FR-015**: Users MUST be able to initiate training for either or both models independently from the console
- **FR-016**: System MUST decline training with a clear message when fewer than 100 labeled email feature vectors are available, indicating how many more are required

#### Model Storage and Versioning

- **FR-017**: System MUST version each trained model, storing training date, algorithm identifier, evaluation metrics, feature schema version, and active status for each version
- **FR-018**: System MUST maintain separate version histories for the action model and the label model
- **FR-019**: System MUST automatically prune model versions beyond the retention limit (default: 5 most recent per model type), deleting model files while preserving metadata audit records
- **FR-020**: System MUST support rollback to any retained prior model version, making it active for classification immediately upon rollback
- **FR-021**: System MUST protect the currently active model from deletion; it cannot be pruned until superseded by a newer active version

#### Quality and Reliability

- **FR-022**: System MUST surface a quality advisory when overall F1 score falls below 0.70 after training, recommending additional data collection
- **FR-023**: System MUST ensure that a training run interrupted by cancellation or failure does not activate a partial model; the prior active model MUST remain in service
- **FR-024**: System MUST log all training events (start, completion, evaluation results, version saved, rollback) as audit entries to support debugging and compliance

### Key Entities

- **IMLModelProvider**: Unified provider interface (following IProvider<TConfig>) that exposes classification for both model types, training, evaluation, versioning, and rollback operations — all returning Result<T>
- **ActionClassificationModel**: The trained multi-class model that assigns exactly one of Keep, Archive, Delete, Spam to an email, along with a confidence score
- **LabelClassificationModel**: The trained multi-label model that returns zero or more label predictions per email, each with an individual confidence score and rank
- **ModelTrainingPipeline**: Shared service responsible for loading feature vectors, building the data transformation pipeline, executing training, and producing an evaluated model artifact
- **ModelVersion**: Record capturing a single trained model's identity: version number, model type (Action or Label), training date, algorithm used, feature schema version, evaluation metrics, file path, and active flag
- **TrainingMetricsReport**: The output of model evaluation — per-class metrics (accuracy, precision, recall, F1) for action models, or per-label metrics for label models, plus an overall aggregate
- **IncrementalUpdateRequest**: Input to the incremental training operation that specifies which new labeled samples to incorporate and which base model version to update from

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With 500+ labeled emails, action model training completes in under 2 minutes and produces a model with overall F1 score ≥ 0.80 on held-out validation data
- **SC-002**: With 500+ labeled emails, label model training completes in under 2 minutes and produces predictions where top-1 label precision is ≥ 0.75 on held-out validation data
- **SC-003**: A single email is classified (action + top-3 labels) in under 10 milliseconds after models are loaded, enabling real-time triage without perceptible delay
- **SC-004**: A batch of 100 emails is classified in under 100 milliseconds, supporting efficient bulk archive triage
- **SC-005**: Incremental updates incorporating 50–200 new corrections complete in under 30 seconds, keeping models current without disruptive full retraining
- **SC-006**: Model rollback to the most recent prior version completes in under 5 seconds, ensuring rapid recovery from a poor training run
- **SC-007**: Training runs on 10,000 email feature vectors complete in under 2 minutes and on 100,000 vectors in under 5 minutes, supporting users with large mail archives
- **SC-008**: The system correctly suppresses low-confidence label predictions at configurable thresholds, meaning false positive label assignments are reduced by at least 30% compared to unconstrained prediction

## Assumptions

- Feature vectors are already extracted and stored (by #54/#55); this feature consumes them as-is and does not re-implement feature extraction
- The action label set is fixed at four values (Keep, Archive, Delete, Spam) for this iteration; adding new action types is out of scope
- Gmail label history is the initial source for label model training; IMAP/Outlook label sources follow the same `CanonicalEmailMetadata` contract and do not require label model changes
- Storage for trained model files uses the existing `data/models/` directory established in the architecture design (#54)
- The three-phase training mode (Cold Start, Hybrid, ML Primary) transitions are defined in the architecture (#54) and are enforced by the calling layer, not re-specified here
- An overall F1 threshold of 0.70 is the advisory quality floor; users are warned but not blocked from activating a model below this threshold

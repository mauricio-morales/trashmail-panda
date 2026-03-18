# Research: Gmail Provider Extension for Training Data

**Feature Branch**: `058-gmail-training-data`
**Date**: 2026-03-17
**Status**: Complete

---

## Research Topics

### 1. IsReplied / IsForwarded Detection — Local Back-Correction Strategy

**Question**: How can the system reliably detect whether the user has replied to or forwarded an email, minimizing additional Gmail API calls?

**Decision**: Local thread-based back-correction. Import all messages with `IsReplied=false` / `IsForwarded=false` initially. Store `ThreadId` on every training record. When any SENT-label message is encountered in the same thread (from the initial scan's SENT-folder pass or any future incremental scan), resolve and back-correct all training records sharing that `ThreadId` — entirely in local SQL, zero additional API calls.

**How it works**:

1. **During any scan**, each imported message records its `ThreadId` alongside its other fields.
2. **SENT messages are included in the scan** — the training data scan covers all folders including `SENT`. When a SENT-label message is stored, it also carries its `ThreadId`.
3. **In-process back-correction**: after each batch is written, a single SQL UPDATE resolves flags for all training records sharing a `ThreadId` with any newly discovered SENT message in that batch:
   ```sql
   -- Mark IsReplied on all emails in threads where we now have a SENT message
   UPDATE training_emails
   SET IsReplied = 1, UpdatedAt = :now
   WHERE ThreadId IN (
       SELECT DISTINCT ThreadId FROM training_emails
       WHERE GmailLabelIds LIKE '%"SENT"%'
   )
   AND GmailLabelIds NOT LIKE '%"SENT"%';  -- don't self-tag the SENT messages

   -- Mark IsForwarded similarly (SENT message subject starts with Fwd:/FW:/Fw:)
   UPDATE training_emails
   SET IsForwarded = 1, UpdatedAt = :now
   WHERE ThreadId IN (
       SELECT DISTINCT ThreadId FROM training_emails
       WHERE GmailLabelIds LIKE '%"SENT"%'
         AND SubjectPrefix IN ('Fwd:', 'FW:', 'Fw:')
   )
   AND GmailLabelIds NOT LIKE '%"SENT"%';
   ```
4. **Progressive improvement**: emails scanned before their thread's SENT message arrives start with `IsReplied=false`. As soon as the SENT message for that thread is encountered — whether in the same scan batch (later in the page ordering) or in a future incremental scan — all affected training records are back-corrected automatically.
5. **Signal re-evaluation**: after back-correction, `ClassificationSignal` and `IsValid` are re-derived from the updated flags per the signal rules table. Records that moved from `AutoDelete` to `LowConfidence` (Trash + engaged) or to `Excluded` (Archive + engaged) are updated atomically.

**What is stored for `SubjectPrefix`**: The first 10 characters of `Subject` are stored in a dedicated short column on `TrainingEmailEntity` (`SubjectPrefix`) solely for this matching. This avoids loading full subject strings in bulk back-correction queries.

**Why this is better than the previous thread.get approach**:
- Zero additional API calls per email or per thread — all resolution happens from data already imported.
- Quota savings: the previous approach would have added ~5 quota units per unique thread in the mailbox; for a mailbox with 10,000 threads that is 50,000 extra units.
- Progressive correctness: engagement flags converge toward accuracy as the scan progresses and as incremental scans add the user's SENT history.
- Simpler implementation: no deduplication caches, no parallel lookup coordination.

**Limitations and mitigations**:
- If the user never imports the SENT folder (e.g., chooses partial scan), engagement flags remain `false`. Mitigation: the SENT folder is always included in the scan by default; the spec requires scanning all folders.
- If SENT messages are fetched after the received messages in page order, back-correction runs at end-of-batch rather than immediately. This is acceptable — `false` defaults are conservative (never over-train on delete signals).
- For forwarded detection, the `SubjectPrefix` heuristic misses cases like "Fwd: Re: Re: original" — accepted as best-effort per FR-018.

**Alternatives Considered**:
- `threads.get` API call per unique thread: 50,000+ extra quota units for large mailboxes; adds latency per batch. Rejected.
- Per-message `In-Reply-To` / `References` header inspection: Requires fetching full RFC822 headers for every email individually (5 quota units each). More precise but more expensive. Rejected.
- Sent-folder correlation by subject hashing: Fragile against edits and Re:/Fwd: prefixing. Rejected.

---

### 2. Scan Resumability — Multi-Session, Crash Recovery, and Incremental Change Detection

**Question**: How should scan progress be tracked so that (a) an initial scan can span multiple app sessions and survive crashes, (b) a user with 10,000s of emails can kill and restart the app at any time without losing progress, and (c) subsequent incremental scans detect state changes without re-importing everything?

**Decision**: Per-folder cursor tracking in `scan_progress`, with a PageToken-expiry fallback, plus Gmail History API for incremental scans. Scan folders in signal-value order to deliver early usability.

---

#### 2a. Per-Folder Cursor (Crash Recovery and Multi-Session)

The single `PageToken` field is insufficient for a multi-folder scan — it cannot express which folder was in progress or what page within that folder was last committed. The `ScanProgressEntity` instead stores a `FolderProgressJson` blob that tracks the state of every folder independently:

```json
{
  "Spam":    { "status": "Completed",  "processedCount": 450,  "pageToken": null },
  "Trash":   { "status": "Completed",  "processedCount": 1200, "pageToken": null },
  "Sent":    { "status": "InProgress", "processedCount": 800,  "pageToken": "NEXT_TOKEN_ABC" },
  "Archive": { "status": "NotStarted", "processedCount": 0,    "pageToken": null },
  "Inbox":   { "status": "NotStarted", "processedCount": 0,    "pageToken": null }
}
```

**Checkpoint protocol** (per batch, within a SQLite transaction):
1. Fetch one page of emails from Gmail (100 messages).
2. Write all `TrainingEmailEntity` rows for that page (upsert).
3. Run back-correction SQL for engagement flags.
4. Update `FolderProgressJson` with the new `pageToken` and incremented `processedCount`.
5. Commit the transaction.

If the app crashes between steps 1 and 5, the transaction is rolled back and the `pageToken` for that folder is unchanged. On restart, the same page is re-fetched from Gmail and re-processed. Upsert semantics handle duplicates safely — no data integrity risk, only a small amount of redundant API work (one page = 100 emails).

**Scan ordering** (defined, not arbitrary) — designed to deliver classifier value as early as possible:
1. **Spam** — strong AutoDelete signals; small folder; fastest to complete
2. **Trash** — strong AutoDelete signals; usually smallish
3. **Sent** — no classification signals generated, but required for engagement back-correction; benefits Archive and Inbox signals imported later
4. **Archive** — largest folder; AutoArchive signals; where most storage reclamation value comes from
5. **Inbox** — LowConfidence signals only; processed last as least actionable

This means a user who restarts after Spam and Trash are complete has immediately usable delete signals and can run a partial classification pass on their existing data even before Archive finishes.

---

#### 2b. PageToken Expiry Fallback

Gmail `nextPageToken` values are session-scoped and expire after some hours (Google does not publish a precise TTL). If the app is restarted hours or days after a crash and the saved `pageToken` is stale, Gmail returns HTTP 400 or 410.

**Recovery procedure** when a saved `pageToken` fails:
1. Clear the `pageToken` for that folder in `FolderProgressJson` (set to `null`).
2. Reset that folder's `status` to `"Recovering"`.
3. Restart the folder scan from the beginning (no `pageToken`).
4. For each fetched email, attempt an EF Core upsert. Because `EmailId` is the primary key, previously stored emails update in place. This is safe and idempotent.
5. The folder's `processedCount` climbs back up through already-seen emails at minimal cost (mostly DB upserts, no new signal assignment needed for rows where nothing changed).
6. Once the folder completes, mark it `"Completed"` as normal.

This means the worst-case restart scenario for a stale token is re-scanning one folder from the beginning — not the entire mailbox. All other completed folders are untouched.

---

#### 2c. Storage Pressure Handling

A user with 50,000 emails could generate significant storage. The system must not silently fill the disk.

**Strategy**: At the start of each batch write, check `IEmailArchiveService.ShouldTriggerCleanupAsync()` (from spec #055). If storage is at or above 90% quota:
1. Pause the scan — mark folder status as `"PausedStorageFull"` in `FolderProgressJson`.
2. Display a Spectre.Console warning to the user:
   ```
   ⚠ Training scan paused — storage quota reached (X MB used of Y MB)
   → Run 'trashmail cleanup' to free space, then restart the scan
   ```
3. Save `ScanProgressEntity` and exit cleanly (not a crash — a controlled pause).
4. On next startup, the `PausedStorageFull` folder status is detected and the user is offered the option to resume or skip remaining folders.

This is not the definitive solution for progressive usability at scale (out of scope for this feature), but it prevents data loss and gives the user a clear path to resume.

---

#### 2d. Incremental Change Detection (Subsequent Scans)

At the conclusion of a successful full initial scan, the Gmail API's current `historyId` is fetched and saved to `ScanProgressEntity.HistoryId`. The next incremental scan calls `users.history.list(startHistoryId=savedHistoryId)` to receive only messages that changed (label changes, new messages, deletions) since the last scan.

If `historyId` is too old (Gmail returns `404 historyId invalid`):
- Fall back to a targeted re-scan: re-apply the full signal rule check for each email already in `training_emails`, using a `messages.get` call, processing in batches. More expensive but safe.
- The `FolderProgressJson` approach handles this the same way as PageToken expiry above.

**Alternatives Considered**:
- Full re-scan on every incremental run comparing all EmailIds: O(n) queries per scan; prohibitively expensive for large inboxes. Rejected.
- Timestamp-based incremental (`after:YYYY/MM/DD`): Misses label changes on old emails. Gmail History API is authoritative. Rejected.
- Single global `PageToken` field: Cannot express which folder is active or where within that folder the scan paused. Rejected (replaced by `FolderProgressJson`).

---

### 3. Classification Signal Assignment Rules

**Question**: What is the complete, unambiguous signal decision table given folder, read status, and engagement flags?

**Decision**: Priority-ordered rule chain with engagement flags as highest priority (except for Spam, where engagement is ignored).

**Signal Table** (evaluated top-down, first match wins):

| Priority | Folder | IsRead | IsReplied OR IsForwarded | Signal | Confidence |
|----------|--------|--------|--------------------------|--------|------------|
| 1 | Spam | any | any | AutoDelete | 0.95 |
| 2 | Archive | any | true | Excluded | N/A |
| 3 | Trash | any | true | LowConfidence | 0.30 |
| 4 | Trash | any | false | AutoDelete | 0.90 |
| 5 | Archive | false | false | AutoArchive | 0.85 |
| 6 | Archive | true | false | Excluded | N/A |
| 7 | Inbox | false | false | LowConfidence | 0.20 |
| 8 | Inbox | true | false | Excluded | N/A |

**Note on "Archive" definition**: An email is considered "in Archive" when its Gmail `labelIds` do NOT include `INBOX`, `SPAM`, or `TRASH`. Archive is not a real Gmail label — it is the absence of folder labels.

**Rationale**:
- Spam engagement override is intentional (per spec FR-004 and US-2 scenario 4): Replying to spam is typically accidental.
- Archive + engaged → Excluded rather than "keep" because the archive placement overrides simple folder-based inference; we exclude rather than confidently train on ambiguous data.
- The spec defines engagement as ⭐⭐⭐⭐⭐ strength (from `MODEL_TRAINING_PIPELINE.md`), which is why it overrides folder signals except for Spam.

**Alternatives Considered**:
- Treat Archive + engaged as a "keep" signal (AutoKeep variant): Rejected because "where the email is stored" is as important as engagement — archiving something you replied to is nuanced.
- Single confidence value per signal: Rejected in favor of signal type + numeric confidence, enabling downstream ML weighting.

---

### 4. Label Taxonomy: User vs. System Label Classification

**Question**: How to distinguish user-created labels from Gmail system labels, since the API returns both?

**Decision**: Match against the known set of Gmail system label IDs.

**Known Gmail System Label IDs**:
`INBOX`, `SENT`, `TRASH`, `SPAM`, `STARRED`, `IMPORTANT`, `UNREAD`, `DRAFT`,
`CATEGORY_PERSONAL`, `CATEGORY_SOCIAL`, `CATEGORY_PROMOTIONS`, `CATEGORY_UPDATES`, `CATEGORY_FORUMS`

Any label whose `id` does NOT appear in this set, and whose `type` field is `"user"` (per Gmail API response), is a user-created label.

**Rationale**: The Gmail API returns a `type` field with values `"system"` or `"user"` on label objects. This is directly usable without additional heuristics. The known-set approach provides a secondary defense in case the `type` field is missing.

**Per spec**:
- User labels → positive training signals (`IsTrainingSignal = true`)
- System labels → context features only (`IsContextFeature = true`)

**Alternatives Considered**:
- Name-based heuristics (uppercase = system): Fragile against future Gmail changes. Rejected.

---

### 5. Database Path Standardization (FR-019, FR-020)

**Question**: What is the correct OS-standard application data path for each platform, and how should it be integrated?

**Decision**: Use `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` combined with the app name subfolder `TrashMailPanda`.

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/TrashMailPanda/app.db` |
| Windows | `%LOCALAPPDATA%\TrashMailPanda\app.db` (C:\Users\user\AppData\Local) |
| Linux | `~/.local/share/TrashMailPanda/app.db` |

**Integration approach**:
1. Replace `StorageProviderConfig.DatabasePath` default from `"./data/app.db"` to a static method call: `DefaultDatabasePath()` that returns the OS-standard path.
2. At `SqliteStorageProvider.InitializeAsync`, check secure storage key `storage_database_path` (wizard-saved). If present and non-empty, use it. Otherwise, use `DefaultDatabasePath()`.
3. The `appsettings.json` entry for `DatabasePath` is removed entirely — the path is determined programmatically, not from config files (config files are committed to source control and therefore cannot safely encode per-user paths).
4. If the directory does not exist at startup, `Directory.CreateDirectory()` is called before the connection is opened (per FR-021).

**Alternatives Considered**:
- `Environment.SpecialFolder.ApplicationData` (macOS: `~/Library/Application Support`, Windows: `%APPDATA%`, Linux: `~/.config`): Functionally similar, but `LocalApplicationData` is the conventional choice for user-specific, non-roaming app data. Chosen `LocalApplicationData` for cross-platform consistency.
- Keep config file path: Config files are committed to source control and contain project-relative paths. Cannot be used for per-user paths. Rejected.

---

### 6. Gmail API Rate Limiting Strategy

**Question**: What rate limiting parameters should be used to safely scan large inboxes without quota violations?

**Decision**: Polly `WaitAndRetryAsync` with exponential backoff; page size 100; thread deduplication for engagement detection.

**Quota baseline**: 250 units/second/user. Each `messages.list` = 5 units; each `messages.get` = 5 units; each `threads.get` = 5 units.

**Strategy**:
- List pages: 100 messages/page. After each page, a 50ms delay yields ~20 list calls/second = 100 units/second (well within limits).
- Thread lookups: Deduplicated by `threadId`. Cache results in-memory for the duration of a scan session.
- On HTTP 429 / `GoogleApiException` with `Error.Code == 429` or `443`: Polly retries with exponential backoff starting at 2 seconds, max 5 attempts, with ±20% jitter.
- On `Retry-After` header present: Honor the specified delay.
- On non-transient errors (401, 403): Propagate as `Result.Failure(new AuthenticationError(...))` immediately.

**Polly policy**:
```csharp
Policy.Handle<GoogleApiException>(e => e.Error.Code is 429 or 503 or 500)
      .WaitAndRetryAsync(
          retryCount: 5,
          sleepDurationProvider: (attempt, outcome, _) =>
          {
              // Honor Retry-After if present
              var retryAfter = (outcome.Result as GoogleApiException)
                ?.Error?.Errors?.FirstOrDefault()?.Message;
              return retryAfter is not null
                 ? TimeSpan.TryParse(retryAfter, out var wait) ? wait : TimeSpan.FromSeconds(Math.Pow(2, attempt))
                 : TimeSpan.FromSeconds(Math.Pow(2, attempt) * (1.0 + Random.Shared.NextDouble() * 0.4 - 0.2));
          })
```

**Existing infrastructure**: `IGmailRateLimitHandler` already exists in the Email provider's Services/ folder. The training data service will use the same handler.

**Alternatives Considered**:
- Token bucket rate limiter: More sophisticated but overkill for single-user desktop app. Polly retry is sufficient. Rejected.
- Fixed delays between all calls: Too slow; exponential backoff on errors is the right pattern. Rejected.

---

### 7. New EF Core Entities and Migration Strategy

**Question**: What new EF Core entities are needed, and how should the migration be structured?

**Decision**: Four new entity classes + one migration modifying `email_features`.

**New entities**:
1. `TrainingEmailEntity` → table `training_emails`
2. `LabelTaxonomyEntity` → table `label_taxonomy`
3. `LabelAssociationEntity` → table `label_associations`
4. `ScanProgressEntity` → table `scan_progress`

**Modified entities**:
- `EmailFeatureVector` → Add `IsReplied` (INTEGER NOT NULL DEFAULT 0) and `IsForwarded` (INTEGER NOT NULL DEFAULT 0) columns via new EF migration.

**Migration approach**: Single EF Core migration named `AddGmailTrainingDataSchema` that:
1. Creates all four new tables with appropriate indexes.
2. Adds `IsReplied` and `IsForwarded` columns to `email_features` with `DEFAULT 0` so existing rows are not broken.
3. Migration is backward-safe: existing database can be upgraded with zero data loss.

**Alternatives Considered**:
- Separate migrations per entity: More granular but not necessary for co-delivered entities. Rejected.
- Raw SQL migration outside EF: Breaks EF migration history. Rejected.

---

### 8. Multi-Account Support and the Role of AccountId

**Question**: Do the new entities need to support multiple Gmail accounts, and does account identity affect training signals?

**Decision**: `AccountId` is an **operational field only** — used for import tracking, incremental scan management, and cursor state. It is **never a training feature**. Signals (AutoDelete, AutoArchive, etc.) are purely content-based and are account-agnostic by design.

**Why operational-only**:
- A spam email on account A that trains an `AutoDelete` signal should apply equally to account B if the same sender/pattern appears. The signal comes from the email's content, folder placement, and engagement — not from which account received it.
- Including `AccountId` as an ML feature would artificially silo training data per account, reducing the effective corpus size and preventing cross-account pattern learning.
- The classifier must never see `AccountId` as an input column.

**Why it is still stored**:
- **Scan management**: Each account has its own `scan_progress` row, its own per-folder page cursors, and its own `HistoryId` baseline. Without `AccountId`, there is no way to determine which scan state to resume on startup when multiple accounts are connected.
- **Incremental delta loading**: To know which emails are new since the last scan for a given account, the system must scope `training_emails` queries by `AccountId`. Without it, new emails from account B could be confused with already-imported emails from account A.
- **Avoids duplicate EmailIds**: Gmail `messageId` values are account-scoped — the same opaque ID could theoretically appear in two different accounts. The composite key `(AccountId, EmailId)` prevents collisions.

**Enforcement rule** (for implementation): `AccountId` is filtered in queries (`WHERE AccountId = :account`) but **must not appear as a column in any `EmailFeatureVector` or ML training input**. The feature extraction layer drops it before handing data to the classifier.

**Implementation**: `AccountId` is populated from `IEmailProvider.GetAuthenticatedUserAsync()` at scan start and stored on `training_emails`, `label_taxonomy`, `label_associations`, and `scan_progress`. No multi-account UI or switching logic is in scope for this feature.

---

## Summary of Resolved Unknowns

| Unknown | Resolution |
|---------|------------|
| IsReplied/IsForwarded detection | Local back-correction from SENT messages in same ThreadId — zero extra API calls |
| Scan resumability | Per-folder cursor in `FolderProgressJson`; checkpoint each batch atomically within SQLite transaction |
| PageToken expiry on restart | Clear stale token, set folder to `"Recovering"`, re-scan from folder start; upserts handle duplicates safely |
| Scan ordering for early value | Spam → Trash → Sent → Archive → Inbox |
| Storage pressure | Pause scan with `PausedStorageFull` status; user resumes after cleanup; controlled not a crash |
| Incremental change detection | Gmail History API (`users.history.list`) from saved `HistoryId` |
| Signal rules | Priority-ordered table with engagement precedence (Table above) |
| System vs. user label classification | Gmail API `type` field + known system ID set |
| Database default path | `Environment.SpecialFolder.LocalApplicationData` + `TrashMailPanda/app.db` |
| Rate limiting | Polly WaitAndRetryAsync, 5 retries, exponential backoff, page size 100 |
| New tables | 4 new entities + email_features extension via EF migration |
| Multi-account scope | `AccountId` column on all new entities, from `GetAuthenticatedUserAsync()` |

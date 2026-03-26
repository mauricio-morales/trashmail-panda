---
name: Coding Standards and Common Mistakes
description: Enforced coding patterns from the constitution and copilot instructions — what to always/never do
type: feedback
---

These rules are enforced by CI/CD and the project constitution.

**Never throw exceptions from providers.** Return `Result.Failure()` instead.
**Why:** Deterministic error handling without hidden control flow; every failure is explicit.
**How to apply:** Any method on a provider or service class must return `Result<T>` — never `throw`.

**One public type per file.**
**Why:** Clear file responsibility, easier navigation, fewer merge conflicts.
**How to apply:** If adding a second public class to a file, create a new file instead.

**No `object` catch-all types.** Use generics or specific types.
**Why:** Nullable reference types are enabled — `object` defeats the type safety.

**No secrets in committed files.**
**Why:** Security — tokens/API keys must go in OS keychain via `SecureStorageManager`.
**How to apply:** If touching credential storage, always use `SecureStorageManager`.

**No hardcoded colors in console output.** Use Spectre.Console semantic markup only.
**Why:** Consistency and maintainability of color semantics.

**Schema changes via migration system only** — never manual `ALTER TABLE`.
**Why:** Migrations ensure auto-upgrade on app start and data integrity.

**Integration tests that need real OAuth must have `Skip` attribute.**
**Why:** Prevents CI failures when credentials aren't available.
**How to apply:** `[Fact(Skip = "Requires OAuth - see CLAUDE.md for setup")]`

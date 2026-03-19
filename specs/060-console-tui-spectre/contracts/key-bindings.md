# Console TUI Key Binding Contract

**Feature**: Console TUI (#060) — Key Binding Schema  
**Date**: 2026-03-19  
**Type**: User-facing interaction contract

This document is the authoritative reference for all keyboard interactions in TrashMail Panda's
console TUI. Every key binding must be listed here and implemented consistently across all modes.

---

## Global Keys (Available in All Modes)

| Key | Action | Notes |
|-----|--------|-------|
| `?` or `F1` | Open help panel | Shows context-specific key bindings |
| `Ctrl+C` | Graceful shutdown | Cancels current operation without data loss |

---

## Main Menu

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate menu items |
| `Enter` | Select highlighted mode |
| `Esc` / `Q` | Exit application |

---

## Email Triage — Cold Start Labeling Mode

> Active when: no trained model exists (`GetActiveModelVersionAsync` returns failure).

| Key | Action | Training Signal |
|-----|--------|----------------|
| `K` | Keep — mark as read; leave in inbox | Stored: `UserCorrected = 0` |
| `A` | Archive — remove from inbox label | Stored: `UserCorrected = 0` |
| `D` | Delete — move to trash | Stored: `UserCorrected = 0` |
| `S` | Spam — report as spam | Stored: `UserCorrected = 0` |
| `Q` / `Esc` | Return to main menu (progress saved) | No signal stored for current email |

### Threshold Prompt (appears when `LabeledCount >= MinTrainingSamples`)

| Key | Action |
|-----|--------|
| `1` / `T` | Go to Training (exit triage, return to main menu) |
| `2` / `C` | Continue Labeling (dismiss prompt, show next email) |

---

## Email Triage — AI-Assisted Mode

> Active when: a trained model exists (`GetActiveModelVersionAsync` succeeds).

| Key | Action | Training Signal |
|-----|--------|----------------|
| `Enter` / `Y` | Accept AI recommendation | Stored: `UserCorrected = 0` |
| `K` | Override with Keep | Stored: `UserCorrected = 1` |
| `A` | Override with Archive | Stored: `UserCorrected = 1` |
| `D` | Override with Delete | Stored: `UserCorrected = 1` |
| `S` | Override with Spam | Stored: `UserCorrected = 1` |
| `Q` / `Esc` | Return to main menu (progress saved) | No signal stored for current email |

---

## Training Mode

| Key | Action |
|-----|--------|
| `Ctrl+C` | Cancel training in progress (graceful) |
| `Y` / `Enter` | Confirm save model (when prompted) |
| `N` / `Esc` | Decline save model (discard new version) |

---

## Bulk Operations Mode

### Step 1: Criteria Builder

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate filter fields |
| `Enter` | Edit selected field |
| `Esc` | Cancel / go back |
| `P` / `Space` | Preview matching emails (estimate) |
| `X` | Execute (after preview, shows confirmation) |

### Step 2: Confirmation

| Key | Action |
|-----|--------|
| `Y` / `Enter` | Confirm bulk action |
| `N` / `Esc` | Cancel |

---

## Provider Settings Mode

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate settings options |
| `Enter` | Select option |
| `Esc` / `B` | Back to main menu |

---

## Help Panel (Any Mode)

| Key | Action |
|-----|--------|
| `Esc` / `Q` / `Enter` / `Space` / `?` | Dismiss help panel |

---

## Key Binding Enforcement Rules

1. All keys in this contract MUST be backed by a `KeyBinding` record in the corresponding
   `HelpContext` static factory method (`HelpContext.ForEmailTriage`, `HelpContext.ForMainMenu`, etc.).
2. Keys are **case-insensitive** — `K` and `k` both trigger the `Keep` action.
3. When an action fails (network error), the key binding remains active so the user can retry
   or choose a different action. The email card stays on screen.
4. In cold-start mode, keys `Enter` and `Y` have no binding (no AI recommendation to accept).

---
name: Project Overview
description: Core identity, goals, and architectural direction of TrashMail Panda
type: project
---

TrashMail Panda is an AI-powered email triage assistant for Gmail users, built with .NET 9.0 / C# 12.

**Why:** Open-source, privacy-first personal email management — each user trains their own local ML model based on their unique email habits. No cloud services, no generic models.

**Key goals:**
- 100% local processing (emails/training data never leave the machine)
- Personal ML models trained on user's own archive/delete/label decisions
- Console-first TUI (Spectre.Console) — lightweight, scriptable, MCP-compatible
- MCP (Model Context Protocol) integration for AI assistant workflows

**Architecture evolution:** Avalonia desktop UI has been removed (feature 063-remove-avalonia-ui). The permanent UI is console-first via Spectre.Console + IApplicationOrchestrator. MVVM/CommunityToolkit.Mvvm is gone.

**Tech stack:**
- .NET 9.0 + C# 12, `<Nullable>enable</Nullable>`
- Spectre.Console — all terminal output
- Microsoft.Extensions.Hosting/DI/Logging/Configuration
- Microsoft.Data.Sqlite + SQLitePCLRaw.bundle_e_sqlcipher (encrypted DB)
- Google.Apis.Gmail.v1
- Polly — resilience/rate-limiting
- xUnit + Moq + coverlet — testing
- ML.NET — local classification (transitioning from OpenAI)

**How to apply:** Frame all suggestions around console-first design. Never suggest Avalonia UI patterns. OpenAI is optional/transitioning out.

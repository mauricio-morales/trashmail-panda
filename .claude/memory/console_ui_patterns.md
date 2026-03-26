---
name: Console UI Patterns
description: Spectre.Console color semantics, OAuth console flow, and TUI patterns for this project
type: project
---

All UI is console-first using Spectre.Console. No Avalonia, no raw ANSI codes.

## Semantic Color Scheme (MANDATORY)
- `[green]` / ✓ — Success, ready providers, successful operations
- `[bold red]` / ✗ — Errors, failed connections — MUST be highly visible. **Always bold red for connector/provider errors.**
- `[yellow]` / ⚠ — Warnings, optional configurations
- `[blue]` / ℹ — Information, status, help text
- `[cyan]` / → — Actions, highlights, email subjects
- `[magenta]` — ML model metrics and performance stats
- `[dim]` — Secondary/supporting text

Never use raw ANSI codes (`\u001b[32m...`). Never hardcode hex colors.

## Output Examples
```csharp
AnsiConsole.MarkupLine("[green]✓ Operation successful[/]");
AnsiConsole.MarkupLine("[bold red]✗ Error:[/] [red]{message}[/]");
AnsiConsole.MarkupLine("[yellow]⚠ Warning message[/]");
AnsiConsole.MarkupLine("[blue]ℹ Information[/]");
AnsiConsole.MarkupLine("[cyan]→ Processing...[/]");
```

## Spinner for long-running operations
```csharp
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("[cyan]Waiting for authorization...[/]", async ctx => { ... });
```

## Provider Status Display Pattern
```
  [green]✓[/] Storage Provider     [Ready]
  [green]✓[/] Gmail Provider       [Authenticated: user@gmail.com]
  [yellow]⚠[/] OpenAI Provider     [Not configured - optional]
  [bold red]✗ Gmail Provider[/] [red]Connection failed: Network timeout[/]
```

## OAuth Console Flow Pattern
1. `[yellow]Gmail authentication required[/]`
2. `[blue]Opening browser for Gmail authentication...[/]`
3. Spinner: `[cyan]Waiting for authorization...[/]`
4. `[green]✓ Gmail authenticated successfully[/]` OR `[bold red]✗ Gmail OAuth failed:[/] [red]{error}[/]`

## Runtime Mode UI (one email at a time)
`[cyan]Email 1/237:[/] [bold][Subject][/] from [dim][Sender][/]`
`[green]Action:[/] Archive ([yellow]85% confidence[/])`
`[green]Labels:[/] newsletters ([green]92%[/]), promotions ([yellow]78%[/])`

**How to apply:** Always use these color semantics for any new console output. Errors must be bold red.

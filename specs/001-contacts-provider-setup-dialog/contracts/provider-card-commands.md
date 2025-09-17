# Provider Card Button Integration

## Overview
Simple change: Wire the Contacts provider "Configure" button to show the existing Gmail OAuth dialog.

## Current Pattern (From Existing Providers)
Looking at how the 3 working providers handle "Configure" buttons:
- Gmail provider: Opens Gmail OAuth dialog
- OpenAI provider: Opens OpenAI configuration dialog
- Storage provider: Opens storage configuration dialog

## Required Change
**Contacts provider**: "Configure" button → Open Gmail OAuth dialog (same as Gmail)

## Implementation
Just wire the button click event to call the existing Gmail setup dialog:

```csharp
// In provider card component
private async Task OnContactsConfigureClicked()
{
    // Call existing Gmail setup - that's it!
    await _gmailSetupService.ShowSetupDialog();
}
```

## No Complex Command Patterns Needed
Follow the existing simple patterns used by the other 3 providers. No need for:
- Complex command interfaces
- Button text management
- Can-execute logic
- Command pattern abstractions

Just a simple button click that opens the existing Gmail dialog.
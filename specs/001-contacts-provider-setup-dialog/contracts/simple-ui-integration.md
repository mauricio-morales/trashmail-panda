# Simple UI Integration Contract

## Overview
Simple approach: Wire the Contacts provider "Configure" button to show the existing Gmail OAuth setup dialog.

## Required Changes

### 1. Provider Card Command Binding
**File**: Provider card component (where Contacts provider is rendered)
**Change**: Wire "Configure" button click to show Gmail OAuth dialog

```csharp
// Simple command binding - reuse existing Gmail setup command
private async Task OnContactsConfigureClicked()
{
    // Show the existing Gmail OAuth dialog
    await ShowGmailSetupDialog();
}
```

### 2. OAuth Dialog Scope Update
**File**: Existing Gmail OAuth dialog/service
**Change**: Include Contacts scopes when configuring from Contacts provider

```csharp
// Update existing Gmail OAuth to include Contacts scopes
private readonly string[] AllGoogleScopes = {
    "https://www.googleapis.com/auth/gmail.readonly",
    "https://www.googleapis.com/auth/gmail.modify",
    "https://www.googleapis.com/auth/contacts.readonly"
};
```

### 3. Provider Status Update
**File**: Existing provider status update logic
**Change**: Update both Gmail and Contacts provider status after OAuth completion

```csharp
// After successful OAuth, update both providers
private async Task OnOAuthCompleted()
{
    await UpdateProviderStatus("Gmail");
    await UpdateProviderStatus("Contacts");
}
```

### 4. Google Contacts Logo
**File**: Provider card resources/assets
**Change**: Add Google Contacts logo PNG file

- Download official Google Contacts logo
- Add to application resources
- Update provider metadata to reference logo path

## No Complex Abstractions Needed

- ❌ No new `IProviderConfigurationService`
- ❌ No new dialog contexts or abstractions
- ❌ No provider architecture changes
- ✅ Just wire existing UI to existing OAuth dialog
- ✅ Update scope list to include Contacts
- ✅ Update provider status after OAuth
- ✅ Add logo asset

## Implementation Contract

1. **Contacts Configure Button**: Click → Show existing Gmail OAuth dialog
2. **OAuth Scopes**: Include Contacts scopes in request
3. **Status Update**: Update both provider statuses after success
4. **Logo**: Add Google Contacts logo to assets

This is a simple UI wiring task, not an architecture change.
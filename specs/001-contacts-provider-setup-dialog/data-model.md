# Data Model: Contacts Provider Setup Dialog

## Overview
Simple approach: Reuse existing data models and UI patterns. No new entities needed - just minor updates to existing OAuth scope configuration.

## Required Changes

### 1. OAuth Scope Configuration
**Current**: Gmail OAuth uses Gmail-specific scopes
**Change**: Update scope list to include Google Contacts scopes

```csharp
// Current Gmail scopes
"https://www.googleapis.com/auth/gmail.readonly"
"https://www.googleapis.com/auth/gmail.modify"

// Add Contacts scope
"https://www.googleapis.com/auth/contacts.readonly"
```

### 2. Provider Logo Asset
**Current**: Gmail and OpenAI have logo PNG files
**Change**: Add Google Contacts logo PNG file to application resources

### 3. Provider Status Update
**Current**: OAuth completion updates Gmail provider status
**Change**: Update both Gmail and Contacts provider status after OAuth success

## No New Data Models Required

This is a simple UI wiring task that reuses all existing:
- Provider display models
- OAuth configuration models
- Provider status models
- MVVM command patterns
- Dialog display patterns

The only "new" data is the Contacts API scope string and logo asset file.
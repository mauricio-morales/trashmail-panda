# Quickstart: Contacts Provider Setup Dialog

## Overview
This quickstart guide provides step-by-step validation scenarios for the Contacts provider configuration feature, covering both fresh setup and scope expansion workflows.

## Prerequisites

### Development Environment
- .NET 9.0 SDK installed
- TrashMail Panda development environment set up
- Visual Studio Code or Visual Studio with C# support
- Git repository checked out to `001-contacts-provider-setup-dialog` branch

### Test Environment Setup
- Application compiled and running in development mode
- Provider dashboard accessible
- OAuth configuration available (either fresh or existing Gmail setup)

### Optional (For Integration Testing)
- Google Cloud Console project with OAuth 2.0 credentials
- Gmail API and Contacts API enabled
- Test Google account for authentication

## Scenario 1: Fresh Setup (No Existing Google Authentication)

### Initial State Validation
1. **Launch Application**
   ```bash
   dotnet run --project src/TrashMailPanda
   ```

2. **Verify Provider Dashboard State**
   - Navigate to provider dashboard
   - Confirm Contacts provider shows "Configure" button
   - Verify button is enabled and clickable
   - Confirm Google Contacts logo is displayed

3. **Verify No Existing Google Authentication**
   - Check that Gmail provider also shows "Configure" button
   - Confirm no Google credentials are stored

### Configuration Flow Execution
1. **Click Contacts Provider Configure Button**
   - Click "Configure" button on Contacts provider card
   - Verify OAuth dialog opens immediately
   - Confirm dialog displays Google Contacts branding

2. **Complete OAuth Flow**
   - Follow Google OAuth authentication steps
   - Grant permissions for both Gmail and Contacts scopes
   - Verify successful authentication message

3. **Validate Configuration Completion**
   - Confirm OAuth dialog closes automatically
   - Verify both Gmail and Contacts providers show "Configured" status
   - Check that both provider cards indicate healthy status
   - Confirm "Configure" buttons are no longer displayed

### Expected Results
- ✅ Both Gmail and Contacts providers configured from single OAuth flow
- ✅ Provider status updated to show healthy configuration
- ✅ No errors or warnings displayed to user
- ✅ Application remains responsive throughout process

## Scenario 2: Scope Expansion (Gmail Already Configured)

### Initial State Setup
1. **Configure Gmail First** (if not already done)
   - Click Gmail provider "Configure" button
   - Complete OAuth flow for Gmail-only scopes
   - Verify Gmail provider shows "Configured" status

2. **Verify Contacts Provider State**
   - Confirm Contacts provider still shows "Configure" button
   - Verify status message indicates scope expansion needed
   - Confirm Google Contacts logo is displayed

### Scope Expansion Flow Execution
1. **Click Contacts Provider Configure Button**
   - Click "Configure" button on Contacts provider card
   - Verify OAuth dialog opens for re-authentication
   - Confirm dialog explains scope expansion context

2. **Complete Re-authentication**
   - Follow Google OAuth re-authentication steps
   - Grant additional permissions for Contacts scopes
   - Verify existing Gmail permissions are preserved

3. **Validate Expansion Completion**
   - Confirm OAuth dialog closes automatically
   - Verify Contacts provider now shows "Configured" status
   - Check that Gmail provider remains "Configured"
   - Confirm both providers indicate healthy status

### Expected Results
- ✅ Contacts provider configured without affecting Gmail
- ✅ Gmail provider remains fully functional
- ✅ Both providers now have appropriate OAuth scopes
- ✅ No disruption to existing Gmail functionality

## Scenario 3: Error Handling and Edge Cases

### OAuth Cancellation Test
1. **Initiate Configuration**
   - Click Contacts provider "Configure" button
   - Wait for OAuth dialog to open

2. **Cancel OAuth Flow**
   - Cancel or close OAuth dialog before completion
   - Or deny permissions during OAuth flow

3. **Validate Error Handling**
   - Confirm user-friendly error message displayed
   - Verify existing provider configurations preserved
   - Check that "Configure" button remains available for retry

### Network Error Test
1. **Simulate Network Issues**
   - Disconnect internet connection or block Google OAuth endpoints
   - Click Contacts provider "Configure" button

2. **Validate Network Error Handling**
   - Confirm appropriate timeout and error messaging
   - Verify application remains stable
   - Check that retry is possible after network restoration

### Invalid Configuration Test
1. **Test with Invalid OAuth Credentials**
   - Modify OAuth configuration to use invalid client ID/secret
   - Attempt Contacts provider configuration

2. **Validate Configuration Error Handling**
   - Confirm clear error message about configuration issues
   - Verify guidance for fixing OAuth setup
   - Check that application doesn't crash or become unresponsive

## Scenario 4: Visual and Accessibility Validation

### Logo Display Test
1. **Verify Google Contacts Logo**
   - Confirm Google Contacts logo displays correctly
   - Check logo matches Google's official branding
   - Verify logo is appropriately sized and positioned

2. **Compare with Other Provider Logos**
   - Confirm visual consistency with Gmail and OpenAI logos
   - Verify logo quality and clarity at different display scales

### Accessibility Test
1. **Keyboard Navigation**
   - Navigate to Contacts provider card using only keyboard
   - Activate "Configure" button using Enter/Space keys
   - Navigate through OAuth dialog using keyboard only

2. **Screen Reader Compatibility**
   - Test with screen reader software (if available)
   - Verify appropriate ARIA labels and descriptions
   - Confirm logical tab order and focus management

## Performance Validation

### Response Time Test
1. **Measure UI Responsiveness**
   - Click "Configure" button and measure time to dialog open
   - Target: Dialog opens within 200ms
   - Verify no UI freezing during OAuth flow

2. **OAuth Flow Performance**
   - Measure complete OAuth flow duration
   - Target: Complete flow within 30 seconds
   - Monitor memory usage during authentication

### Concurrent Operation Test
1. **Test Multiple Provider Operations**
   - Attempt to configure multiple providers simultaneously
   - Verify appropriate queuing or error handling
   - Confirm no race conditions or data corruption

## Integration Test Scenarios (Requires Real Credentials)

> **Note**: These tests require actual Google OAuth credentials and should be marked as skipped in CI workflows.

### Real OAuth Flow Test
```csharp
[Fact(Skip = "Requires real Google OAuth credentials")]
public async Task ConfigureContactsProvider_WithRealCredentials_CompletesSuccessfully()
{
    // Test implementation with actual Google OAuth flow
    // Verify real API access and token storage
}
```

### Scope Validation Test
```csharp
[Fact(Skip = "Requires real Google OAuth credentials")]
public async Task ScopeExpansion_PreservesExistingGmailAccess_AddsContactsAccess()
{
    // Test implementation with real scope expansion
    // Verify both Gmail and Contacts API access work
}
```

## Troubleshooting Guide

### Common Issues
1. **"Configure" Button Not Working**
   - Verify button click handler is properly wired
   - Check for JavaScript errors in UI components
   - Confirm provider status service is available

2. **OAuth Dialog Not Opening**
   - Check OAuth service registration in dependency injection
   - Verify Google OAuth client configuration
   - Confirm UI thread availability for modal dialogs

3. **Provider Status Not Updating**
   - Verify provider health check is triggered after configuration
   - Check MVVM property change notifications
   - Confirm UI binding is properly established

### Debug Commands
```bash
# Run with detailed logging
dotnet run --project src/TrashMailPanda --verbosity detailed

# Run provider-specific tests
dotnet test --filter "FullyQualifiedName~ContactsProvider"

# Validate OAuth configuration
dotnet test --filter "Category=Integration" --logger console
```

## Success Criteria Checklist

### Functional Requirements
- [ ] Contacts provider "Configure" button opens OAuth dialog
- [ ] OAuth dialog reuses existing Gmail authentication components
- [ ] Fresh setup configures both Gmail and Contacts providers
- [ ] Scope expansion preserves existing Gmail configuration
- [ ] Google Contacts logo displays correctly
- [ ] Provider status updates reflect configuration changes
- [ ] Error handling provides clear user feedback

### Technical Requirements
- [ ] No new components created (reuses existing OAuth dialog)
- [ ] Result<T> pattern used for all async operations
- [ ] MVVM patterns followed throughout
- [ ] Proper dependency injection used
- [ ] Security requirements met (encrypted credential storage)
- [ ] Constitutional requirements satisfied

### Quality Requirements
- [ ] Unit tests achieve 90% coverage
- [ ] Integration tests created (marked as skipped for CI)
- [ ] UI responsiveness meets performance targets
- [ ] Accessibility requirements met
- [ ] Visual consistency maintained across providers
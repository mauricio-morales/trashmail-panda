# Research: Contacts Provider Setup Dialog Integration

## Overview
Research existing patterns for OAuth dialog reuse and provider configuration to enable Contacts provider "Configure" button functionality.

## Key Research Areas

### 1. Existing OAuth Dialog Architecture

**Decision**: Reuse `GmailSetupViewModel` and associated UI components for Contacts provider configuration

**Rationale**:
- Existing Gmail OAuth flow already implements Google OAuth2 with proper scope management
- User provided explicit guidance to reuse existing dialog rather than create new components
- Maintains consistency in user experience across Google-based providers
- Reduces code duplication and maintenance overhead

**Research Findings**:
- `GmailSetupViewModel` handles OAuth flow via `GmailOAuthService`
- Modal dialog pattern already established in the application
- Scope management already supports multiple Google API scopes in OAuth configuration

**Alternatives Considered**:
- Creating separate Contacts-specific dialog: Rejected due to unnecessary duplication
- Consolidating provider cards: Rejected as it misrepresents separate provider functions

### 2. Provider Configuration Integration

**Decision**: Extend existing provider card "Configure" button to invoke shared OAuth dialog

**Rationale**:
- Provider cards already have established UI patterns for configuration actions
- The "Configure" button exists but currently does nothing for Contacts provider
- Follows existing provider architecture where each provider manages its own configuration state

**Research Findings**:
- Provider cards are rendered with status and configuration controls
- Configuration state is managed through provider status service
- Button click handlers can be connected to existing OAuth service

**Alternatives Considered**:
- Creating entirely new configuration flow: Rejected for consistency reasons
- Removing separate provider identities: Rejected as Gmail and Contacts serve different functions

### 3. Scope Management for Fresh Setup vs Expansion

**Decision**: Detect authentication state and automatically handle both scenarios through the same OAuth flow

**Rationale**:
- OAuth2 flow naturally supports scope expansion through re-authentication
- Google APIs allow requesting additional scopes on existing tokens
- Simpler user experience with single authentication pattern

**Research Findings**:
- Google OAuth2 supports incremental authorization for additional scopes
- Existing `GmailOAuthService` can be extended to handle multiple provider scope requirements
- Current users may need scope expansion, but new users will get all scopes initially

**Alternatives Considered**:
- Separate flows for fresh vs expansion: Rejected for complexity and user confusion
- Different UI messaging: Deemed unnecessary per user guidance

### 4. Google Contacts Logo Integration

**Decision**: Download and integrate official Google Contacts logo following existing asset patterns

**Rationale**:
- Visual consistency with other provider logos (Gmail, OpenAI)
- Professional appearance matching Google's brand guidelines
- Maintains established pattern of provider-specific branding

**Research Findings**:
- Existing logos stored as PNG assets in application resources
- Provider cards display logos alongside status and configuration controls
- Logo sizing and placement patterns already established

**Alternatives Considered**:
- Using generic contact icon: Rejected for brand consistency
- Using Gmail logo for both: Rejected as it confuses distinct providers

### 5. Provider Status Update Patterns

**Decision**: Follow existing provider health check and status update patterns

**Rationale**:
- Established patterns for provider initialization and health monitoring
- Status updates trigger UI refresh automatically through MVVM binding
- Consistent with other provider configuration completion flows

**Research Findings**:
- Provider status managed through `IProviderStatusService`
- Health checks run automatically after configuration changes
- UI binding automatically reflects provider status changes

**Alternatives Considered**:
- Manual status updates: Rejected as it breaks established patterns
- Immediate status changes: Rejected due to async OAuth flow requirements

## Implementation Strategy Summary

1. **Reuse OAuth Dialog**: Extend `GmailSetupViewModel` to support Contacts provider configuration
2. **Connect Configure Button**: Wire Contacts provider "Configure" button to invoke shared OAuth dialog
3. **Scope Management**: Handle both fresh setup and scope expansion transparently
4. **Logo Integration**: Add Google Contacts logo following existing asset patterns
5. **Status Updates**: Follow established provider status update patterns

## Technical Dependencies

- **Existing Components**: `GmailSetupViewModel`, `GmailOAuthService`, Provider card UI
- **OAuth Scopes**: Google Contacts API scopes in addition to existing Gmail scopes
- **Logo Asset**: Official Google Contacts PNG logo
- **Provider Architecture**: Existing `IProvider<TConfig>` and status management patterns

## Testing Strategy

- **Unit Tests**: OAuth dialog component behavior, provider status updates
- **Integration Tests**: Full OAuth flow with actual Google credentials (marked as skipped for CI)
- **UI Tests**: Provider card button functionality, dialog display, status updates

## No Outstanding Questions

All research areas have clear decisions with sufficient context from existing codebase patterns and user guidance. Ready to proceed to Phase 1 design.
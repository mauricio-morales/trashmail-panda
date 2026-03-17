# Feature Specification: Console-based Gmail OAuth Flow

**Feature Branch**: `056-console-gmail-oauth`  
**Created**: March 16, 2026  
**Status**: Draft  
**Related Issues**: #53  
**Dependencies**: #55 (storage system for tokens)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - First-Time OAuth Setup (Priority: P1)

A developer runs the TrashMail Panda application for the first time without any Gmail credentials configured. The application detects the missing OAuth token, displays clear instructions in the console, and guides them through the authentication process using their web browser. After successful authentication, the refresh token is securely stored in the OS keychain, and the application confirms setup completion.

**Why this priority**: This is the critical path for all new users. Without successful OAuth setup, the application cannot access Gmail and provides no value. This represents the minimum viable authentication flow.

**Independent Test**: Can be fully tested by launching the application on a clean system (no stored credentials), completing the OAuth flow, and verifying the application can subsequently access Gmail APIs without re-authentication.

**Acceptance Scenarios**:

1. **Given** no Gmail OAuth token exists, **When** application starts, **Then** display yellow-colored message "Gmail authentication required" with clear next steps
2. **Given** user initiates OAuth flow, **When** browser opens to Google authorization page, **Then** user sees standard Gmail permission consent screen
3. **Given** user approves permissions in browser, **When** authorization completes, **Then** console displays green checkmark "✓ Gmail authentication successful" and token is stored in OS keychain
4. **Given** OAuth flow completes successfully, **When** application checks authentication status, **Then** subsequent startups do not require re-authentication

---

### User Story 2 - Token Validation and Auto-Refresh (Priority: P2)

A user with previously stored Gmail credentials launches the application. The system validates the existing access token, detects it has expired, and automatically refreshes it using the stored refresh token without requiring user intervention. The user sees a brief console message indicating token refresh is in progress, followed by confirmation of successful authentication.

**Why this priority**: Automatic token management provides seamless user experience and reduces authentication friction. This is essential for production use but can be tested independently from the initial setup flow.

**Independent Test**: Can be tested by simulating an expired access token scenario, launching the application, and verifying it successfully refreshes the token and proceeds without user interaction.

**Acceptance Scenarios**:

1. **Given** valid refresh token exists but access token expired, **When** application starts, **Then** automatically refresh access token without user prompt
2. **Given** token refresh in progress, **When** refresh completes successfully, **Then** display "Token refreshed" message and continue startup
3. **Given** refresh token is invalid/expired, **When** refresh fails, **Then** display yellow warning "Re-authentication required" and initiate new OAuth flow
4. **Given** token validation occurs, **When** token is still valid, **Then** skip refresh and proceed silently to main application

---

### User Story 3 - Error Recovery and User Guidance (Priority: P3)

A user encounters an error during the OAuth flow (network timeout, user denies permissions, or browser fails to open). The application displays a clear, bold red error message explaining what went wrong and provides actionable next steps. The user can retry the authentication or troubleshoot based on the specific error encountered.

**Why this priority**: Error handling is critical for production readiness but depends on the core OAuth flow (P1) being implemented. Enhanced error recovery improves user experience but the basic flow can function with minimal error handling initially.

**Independent Test**: Can be tested by simulating various failure scenarios (network disconnection, permission denial, timeout) and verifying appropriate error messages and recovery options are presented.

**Acceptance Scenarios**:

1. **Given** OAuth flow initiated, **When** user denies Gmail permissions, **Then** display bold red error "Authentication failed: Permissions denied" with retry option
2. **Given** browser launch attempted, **When** browser fails to open, **Then** display device code as fallback with instructions "Visit https://google.com/device and enter code: XXXX-XXXX"
3. **Given** waiting for OAuth callback, **When** timeout occurs (e.g., 5 minutes), **Then** display yellow warning "Authentication timed out. Please try again" and return to main menu
4. **Given** network error during token exchange, **When** error detected, **Then** display red error with technical details for troubleshooting and retry option

---

### Edge Cases

- What happens when the OS keychain is locked or inaccessible during token storage? We can have a GH issue backlog created for a future fallback plan, but no need to account for this now.
- How does the system handle multiple concurrent OAuth flows (e.g., user opens multiple terminal instances)? It should control only 1 instance created per storage/sqlite db. 
- What occurs if the Google OAuth service is temporarily unavailable? unlikely, no need to plan for this, just error out and kill the app. 
- How does the system behave when the stored refresh token format changes due to Google API updates? should behave as a brand new app, no token to recover and do 1st time setup.
- What happens when browser redirects to localhost callback URL but no listener is active? this sounds like a technical problem that would need fixing. 
- How does the system handle partial token storage (access token saved but refresh token fails)? this would be a bug, no need to recover from it, instead denote the error and fail out (as the devs need to fix whatever led to this). 

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST check for existing Gmail OAuth tokens on application startup before attempting any Gmail API calls
- **FR-002**: System MUST support browser-based OAuth 2.0 authorization code flow as the primary authentication method
- **FR-003**: System MUST support OAuth 2.0 device code flow as a fallback when browser launch fails or is unavailable
- **FR-004**: Users MUST be able to initiate OAuth flow from console with clear visual prompts (e.g., "Press Enter to authenticate with Gmail")
- **FR-005**: System MUST validate existing access tokens and automatically refresh expired tokens using stored refresh tokens without user interaction
- **FR-006**: System MUST securely store refresh tokens using the existing OS keychain infrastructure via `SecureStorageManager`
- **FR-007**: System MUST display console messages with color coding: yellow for warnings, green for success, red for errors, using ANSI color codes or equivalent console formatting library
- **FR-008**: System MUST display real-time status updates during OAuth flow including: "Opening browser...", "Waiting for authorization...", "Saving credentials..." with appropriate spinners or progress indicators
- **FR-009**: System MUST handle OAuth callback via temporary local HTTP listener on configurable port (default: localhost:8080)
- **FR-010**: System MUST display user-friendly error messages for common OAuth failure scenarios: permission denial, network errors, timeout, invalid tokens
- **FR-011**: System MUST provide retry mechanism for failed authentication attempts without requiring application restart
- **FR-012**: System MUST validate OAuth configuration (client ID, client secret) exists before initiating flow and display setup instructions if missing
- **FR-013**: System MUST log all OAuth operations (token refresh, validation, errors) to application log file without exposing sensitive token values
- **FR-014**: System MUST complete initial OAuth flow and token storage within 5 minutes or timeout with user-friendly error message
- **FR-015**: System MUST support graceful cancellation of OAuth flow via Ctrl+C or equivalent console interrupt signal

### Key Entities

- **OAuth Token**: Represents Gmail API access credentials including access token (short-lived), refresh token (long-lived), expiration timestamp, and token scopes
- **Console OAuth Handler**: Manages the console-based OAuth authentication workflow including browser launch coordination, callback server management, and user interaction prompts  
- **Token Validator**: Responsible for checking token validity, expiration status, and triggering refresh operations
- **Keychain Integration**: Interface to `SecureStorageManager` for persistent, encrypted storage of refresh tokens in OS-specific secure storage (Keychain on macOS, DPAPI on Windows, libsecret on Linux)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can complete first-time Gmail OAuth authentication from console in under 90 seconds from application launch to successful token storage
- **SC-002**: Automatic token refresh succeeds for 99% of cases when valid refresh token exists
- **SC-003**: Token validation and refresh operations complete in under 3 seconds, ensuring minimal application startup delay  
- **SC-004**: OAuth flow handles network errors gracefully with clear error messages in 100% of common failure scenarios (permission denial, timeout, network disconnect)
- **SC-005**: 95% of users successfully complete OAuth setup on first attempt without requiring documentation or support
- **SC-006**: Refresh tokens persist securely in OS keychain across application restarts with zero token loss incidents
- **SC-007**: Console messages are clearly readable with appropriate color coding on 100% of tested terminal emulators (Terminal.app, iTerm2, Windows Terminal, GNOME Terminal)

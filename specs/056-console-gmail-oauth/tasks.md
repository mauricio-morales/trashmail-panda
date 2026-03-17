# Tasks: Console-based Gmail OAuth Flow

**Input**: Design documents from `/specs/056-console-gmail-oauth/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/service-contracts.md

**Tests**: Integration tests included but marked with `[Fact(Skip = "...")]` per architectural guidelines

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and dependency setup

- [X] T001 Add Spectre.Console package reference (v0.48.0+) to src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj
- [X] T002 [P] Verify Google.Apis.Gmail.v1 (v1.67.0.3477+) and Google.Apis.Auth.OAuth2 (v1.67.0+) package references in src/TrashMailPanda/TrashMailPanda/TrashMailPanda.csproj
- [X] T003 [P] Create src/TrashMailPanda/TrashMailPanda/Services/ directory structure for OAuth services
- [X] T004 [P] Create src/TrashMailPanda/TrashMailPanda/Models/ directory for OAuth models

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data models and interfaces that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 [P] Create OAuthFlowResult record in src/TrashMailPanda/TrashMailPanda/Models/OAuthFlowResult.cs with AccessToken, RefreshToken, ExpiresInSeconds, IssuedUtc, Scopes, UserEmail properties and IsAccessTokenExpired() method
- [X] T006 [P] Create OAuthCallbackData record in src/TrashMailPanda/TrashMailPanda/Models/OAuthCallbackData.cs with Code, State, Error, ErrorDescription, ReceivedAt properties and IsError/IsValid methods
- [X] T007 [P] Create TokenValidationResult record in src/TrashMailPanda/TrashMailPanda/Models/TokenValidationResult.cs with TokensExist, IsAccessTokenExpired, HasRefreshToken, TimeUntilExpiry, Status, Message properties
- [X] T008 [P] Create TokenStatus enum in src/TrashMailPanda/TrashMailPanda/Models/TokenStatus.cs with Valid, ExpiredCanRefresh, RefreshTokenMissing, NotAuthenticated, RefreshTokenRevoked values
- [X] T009 [P] Create PKCEPair record in src/TrashMailPanda/TrashMailPanda/Models/PKCEPair.cs with CodeChallenge and CodeVerifier properties
- [X] T010 [P] Create OAuthConfiguration record in src/TrashMailPanda/TrashMailPanda/Models/OAuthConfiguration.cs with ClientId, ClientSecret, Scopes, RedirectUri, Timeout properties
- [X] T011 [P] Create IGoogleOAuthHandler interface in src/TrashMailPanda/TrashMailPanda/Services/IGoogleOAuthHandler.cs with AuthenticateAsync, RefreshTokenAsync, IsConfiguredAsync, ClearAuthenticationAsync methods
- [X] T012 [P] Create IGoogleTokenValidator interface in src/TrashMailPanda/TrashMailPanda/Services/IGoogleTokenValidator.cs with ValidateAsync, CanAutoRefreshAsync, LoadStoredTokensAsync methods
- [X] T013 [P] Create ILocalOAuthCallbackListener interface in src/TrashMailPanda/TrashMailPanda/Services/ILocalOAuthCallbackListener.cs with StartAsync, GetRedirectUri, WaitForCallbackAsync, StopAsync methods inheriting IAsyncDisposable
- [X] T014 Create PKCEGenerator utility class in src/TrashMailPanda/TrashMailPanda/Services/PKCEGenerator.cs with GeneratePKCEPair() method using SHA256 and Base64UrlEncode

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - First-Time OAuth Setup (Priority: P1) 🎯 MVP

**Goal**: Enable new users to authenticate with Gmail through browser-based OAuth flow with clear console feedback

**Independent Test**: Start application on clean system (no tokens), complete OAuth flow, verify tokens stored in OS keychain and app can access Gmail API

### Implementation for User Story 1

- [X] T015 [P] [US1] Create LocalOAuthCallbackListener implementation in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs implementing ILocalOAuthCallbackListener with StartAsync using System.Net.HttpListener on 127.0.0.1:0
- [X] T016 [P] [US1] Implement GetRedirectUri method in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs to return complete localhost callback URL with assigned port
- [X] T017 [US1] Implement WaitForCallbackAsync method in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs with timeout handling, state validation, query parameter parsing to return OAuthCallbackData
- [X] T018 [US1] Implement StopAsync and DisposeAsync methods in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs to clean up HttpListener resources
- [X] T019 [US1] Create GoogleOAuthHandler partial class in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs implementing IGoogleOAuthHandler with constructor injecting ISecureStorageManager, ILogger, ILocalOAuthCallbackListener factory
- [X] T020 [US1] Implement AuthenticateAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs: generate PKCE pair using PKCEGenerator
- [X] T021 [US1] Add browser launch logic to AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs using Process.Start with platform detection (Windows UseShellExecute, macOS 'open', Linux 'xdg-open')
- [X] T022 [US1] Add OAuth callback wait and authorization code exchange in AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs using Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow with PKCE verifier
- [X] T023 [US1] Implement token storage in AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs using SecureStorageManager with GmailStorageKeys constants (GMAIL_ACCESS_TOKEN, GMAIL_REFRESH_TOKEN, GMAIL_TOKEN_EXPIRY, GMAIL_TOKEN_ISSUED_UTC, GMAIL_USER_EMAIL)
- [X] T024 [US1] Add Spectre.Console colored output to AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs: blue for "Opening browser", cyan spinner for "Waiting for authorization", green for success, red for errors
- [X] T025 [US1] Implement IsConfiguredAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs to check GMAIL_CLIENT_ID and GMAIL_CLIENT_SECRET exist in SecureStorageManager
- [X] T026 [US1] Implement ClearAuthenticationAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs to delete all OAuth tokens from SecureStorageManager

### Integration Tests for User Story 1

- [ ] T027 [P] [US1] Create ConsoleOAuthHandlerTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ with unit tests for PKCE generation, configuration validation, token storage logic using Moq for ISecureStorageManager
- [ ] T028 [P] [US1] Create LocalOAuthCallbackListenerTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ with unit tests for HTTP listener startup, port allocation, query parameter parsing
- [ ] T029 [P] [US1] Create OAuthFlowIntegrationTests.cs in tests/TrashMailPanda.Tests/Integration/Console/ with [Fact(Skip = "Requires OAuth - manual test with real Gmail credentials")] for full OAuth flow end-to-end test

**Checkpoint**: At this point, User Story 1 should be fully functional - new users can authenticate and store tokens

---

## Phase 4: User Story 2 - Token Validation and Auto-Refresh (Priority: P2)

**Goal**: Enable automatic token refresh for returning users without requiring re-authentication

**Independent Test**: Start application with expired access token, verify it auto-refreshes using stored refresh token without user interaction

### Implementation for User Story 2

- [X] T030 [P] [US2] Create GoogleTokenValidator implementation in src/TrashMailPanda/TrashMailPanda/Services/GoogleTokenValidator.cs implementing IGoogleTokenValidator with constructor injecting ISecureStorageManager and ILogger
- [X] T031 [US2] Implement LoadStoredTokensAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleTokenValidator.cs to reconstruct OAuthFlowResult from SecureStorageManager (GMAIL_ACCESS_TOKEN, GMAIL_REFRESH_TOKEN, GMAIL_TOKEN_EXPIRY, GMAIL_TOKEN_ISSUED_UTC)
- [X] T032 [US2] Implement ValidateAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleTokenValidator.cs to check token existence, calculate expiry (IssuedUtc + ExpiresInSeconds < Now), determine TokenStatus and return TokenValidationResult
- [X] T033 [US2] Implement CanAutoRefreshAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleTokenValidator.cs to check if refresh token exists and tokens are stored
- [X] T034 [US2] Implement RefreshTokenAsync method in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs using Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.RefreshTokenAsync with stored refresh token
- [X] T035 [US2] Add refresh token storage logic to RefreshTokenAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs to update access token, expiry, and issued time in SecureStorageManager
- [X] T036 [US2] Add Spectre.Console status spinner to RefreshTokenAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs with cyan "Refreshing access token..." message and green success confirmation

### Integration Tests for User Story 2

- [ ] T037 [P] [US2] Create TokenValidatorTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ with unit tests for token expiry calculation, status determination, and LoadStoredTokensAsync logic using Moq
- [ ] T038 [P] [US2] Add refresh token tests to ConsoleOAuthHandlerTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ for RefreshTokenAsync with mock Google API responses (success, invalid_grant, network error)
- [ ] T039 [P] [US2] Create TokenRefreshIntegrationTests.cs in tests/TrashMailPanda.Tests/Integration/Console/ with [Fact(Skip = "Requires OAuth - manual test with expired token")] for auto-refresh scenario

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently - new users authenticate, returning users auto-refresh

---

## Phase 5: User Story 3 - Error Recovery and User Guidance (Priority: P3)

**Goal**: Provide clear, actionable error messages and recovery options for OAuth failures

**Independent Test**: Simulate various failure scenarios (permission denial, network timeout, browser launch failure) and verify appropriate error messages and recovery prompts

### Implementation for User Story 3

- [X] T040 [P] [US3] Create OAuthErrorHandler utility class in src/TrashMailPanda/TrashMailPanda/Services/OAuthErrorHandler.cs with DisplayError method accepting Exception and allowRetry bool
- [X] T041 [US3] Implement error mapping logic in OAuthErrorHandler.DisplayError in src/TrashMailPanda/TrashMailPanda/Services/OAuthErrorHandler.cs to map exceptions to (userMessage, technicalDetails, isRetryable) tuples
- [X] T042 [US3] Add Spectre.Console formatting to OAuthErrorHandler.DisplayError in src/TrashMailPanda/TrashMailPanda/Services/OAuthErrorHandler.cs with bold red for errors, dim red for details, cyan for retry prompts
- [X] T043 [US3] Integrate OAuthErrorHandler into GoogleOAuthHandler.AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs for browser launch failures, callback timeouts, token exchange errors
- [X] T044 [US3] Add fallback manual URL display to AuthenticateAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs when browser launch fails: display full authorization URL with yellow "Manual authentication required" message
- [X] T045 [US3] Implement invalid_grant detection in RefreshTokenAsync in src/TrashMailPanda/TrashMailPanda/Services/GoogleOAuthHandler.cs to detect refresh token revoked, clear tokens via ClearAuthenticationAsync, display red "Refresh token revoked - re-authentication required" message
- [X] T046 [US3] Add timeout error handling to WaitForCallbackAsync in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs with yellow warning "Authentication timed out after 5 minutes" and cleanup of HttpListener
- [X] T047 [US3] Add user denial detection to WaitForCallbackAsync in src/TrashMailPanda/TrashMailPanda/Services/LocalOAuthCallbackListener.cs parsing error=access_denied from callback, return OAuthCallbackData with error details

### Integration Tests for User Story 3

- [ ] T048 [P] [US3] Create OAuthErrorHandlerTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ with unit tests for error mapping logic (AuthenticationError, NetworkError, ConfigurationError, ProcessingError scenarios)
- [ ] T049 [P] [US3] Add error recovery tests to ConsoleOAuthHandlerTests.cs in tests/TrashMailPanda.Tests/Unit/Services/ for browser launch failure, timeout handling, invalid_grant scenario with mocks
- [ ] T050 [P] [US3] Create OAuthErrorRecoveryTests.cs in tests/TrashMailPanda.Tests/Integration/Console/ with [Fact(Skip = "Manual test - requires simulated failures")] for testing error messages and retry logic

**Checkpoint**: All user stories should now be independently functional with comprehensive error handling

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories and production readiness

- [X] T051 Register IGoogleOAuthHandler, IGoogleTokenValidator, ILocalOAuthCallbackListener in DI container in src/TrashMailPanda/TrashMailPanda/Services/ServiceCollectionExtensions.cs with appropriate lifetimes (Singleton/Transient)
- [ ] T052 [P] Add application startup OAuth check logic in src/TrashMailPanda/TrashMailPanda/Program.cs using ITokenValidator.ValidateAsync to determine authentication state before main workflow
- [ ] T053 [P] Update GmailEmailProvider health check in src/Providers/Email/TrashMailPanda.Providers.Email/GmailEmailProvider.cs to use ITokenValidator for token validation
- [ ] T054 [P] Add OAuth credential configuration prompt in src/TrashMailPanda/TrashMailPanda/Program.cs or startup flow when IsConfiguredAsync returns false, using Spectre.Console prompts for ClientId and ClientSecret
- [ ] T055 [P] Create OAuth setup documentation in docs/oauth/GMAIL_OAUTH_CONSOLE_SETUP.md with Google Cloud Console setup instructions, environment variable configuration, first-time setup walkthrough
- [ ] T056 [P] Update CLAUDE.md and .github/copilot-instructions.md with console OAuth patterns, IConsoleOAuthHandler usage examples, troubleshooting guide
- [ ] T057 Run quickstart.md validation scenarios manually: first-time setup, token refresh, error recovery, verify all console output matches expected colors and messages
- [ ] T058 Code cleanup and refactoring: ensure all OAuth services follow Result<T> pattern, no sensitive tokens in logs, proper null checks, consistent error handling
- [ ] T059 Security audit: verify PKCE implementation, state parameter validation, localhost-only callback, OS keychain storage, no plaintext credentials in database
- [ ] T060 Run full test suite with `dotnet test --configuration Release` to ensure 90%+ coverage for OAuth services (excluding integration tests marked with Skip)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - User stories can then proceed in priority order: P1 → P2 → P3
  - Or in parallel if multiple developers available
- **Polish (Phase 6)**: Depends on desired user stories being complete (minimum US1 for MVP)

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories - **THIS IS THE MVP**
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Extends US1 ConsoleOAuthHandler with RefreshTokenAsync - Should work independently from US3
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Adds error handling to US1 and US2 - Enhances but doesn't block core functionality

### Within Each User Story

**User Story 1 (First-Time OAuth Setup):**
- T015-T018 (LocalOAuthCallbackListener) can be developed in parallel with T019-T026 (ConsoleOAuthHandler)
- T019 (ConsoleOAuthHandler constructor) → T020 (PKCE) → T021 (browser launch) → T022 (callback wait) → T023 (token storage) → T024 (console UI) must be sequential
- T027-T029 (tests) can run in parallel after implementation complete

**User Story 2 (Token Validation and Auto-Refresh):**
- T030-T033 (TokenValidator) can be developed in parallel with T034-T036 (RefreshTokenAsync implementation)
- T037-T039 (tests) can run in parallel after implementation complete

**User Story 3 (Error Recovery):**
- T040-T042 (OAuthErrorHandler) can be developed first
- T043-T047 (error integration) depend on T040-T042 and previous user stories
- T048-T050 (tests) can run in parallel after implementation complete

### Parallel Opportunities

**Phase 1 (Setup):**
- T001, T002, T003, T004 can all run in parallel (different files, no dependencies)

**Phase 2 (Foundational):**
- T005-T010 (all models) can run in parallel
- T011-T013 (all interfaces) can run in parallel
- T014 (PKCEGenerator) can run in parallel with models and interfaces

**Phase 3 (US1):**
```bash
# Parallel batch 1: Infrastructure components
Task T015-T018: LocalOAuthCallbackListener implementation
Task T019: ConsoleOAuthHandler constructor and DI setup

# Sequential: T020 → T021 → T022 → T023 → T024 (OAuth flow logic)

# Parallel batch 2: Helper methods and tests
Task T025: IsConfiguredAsync implementation
Task T026: ClearAuthenticationAsync implementation
Task T027-T029: All unit/integration tests
```

**Phase 4 (US2):**
```bash
# Parallel batch 1: Core validation
Task T030-T033: TokenValidator implementation
Task T034-T036: RefreshTokenAsync implementation

# Parallel batch 2: Tests
Task T037-T039: All unit/integration tests
```

**Phase 5 (US3):**
```bash
# Sequential: T040-T042 → T043-T047 (error handler first, then integration)

# Parallel batch: Tests
Task T048-T050: All unit/integration tests
```

**Phase 6 (Polish):**
- T052, T053, T054, T055, T056 can run in parallel (different files)
- T051 (DI registration), T057 (quickstart validation), T058-T060 (cleanup/audit) are sequential final steps

---

## Implementation Strategy

### MVP First (User Story 1 Only)

**Goal**: Get first-time OAuth flow working as fast as possible

1. **Complete Phase 1: Setup** (~30 minutes)
   - Add dependencies, create directory structure

2. **Complete Phase 2: Foundational** (~2 hours)
   - Create all data models and interface contracts
   - Implement PKCEGenerator utility

3. **Complete Phase 3: User Story 1** (~6-8 hours)
   - Implement LocalOAuthCallbackListener (T015-T018)
   - Implement ConsoleOAuthHandler.AuthenticateAsync (T019-T024)
   - Add helper methods (T025-T026)
   - Write unit/integration tests (T027-T029)

4. **Minimal Polish** (~1 hour)
   - DI registration (T051)
   - Startup integration (T052)
   - Manual testing with quickstart.md scenarios

5. **STOP and VALIDATE**: Test first-time OAuth flow end-to-end
   - Deploy/demo if ready - this is the **Minimum Viable Product**

**Estimated MVP Time**: 10-12 hours total

---

### Incremental Delivery

**After MVP (User Story 1) is validated:**

1. **Add User Story 2: Auto-Refresh** (~4-6 hours)
   - Implement TokenValidator (T030-T033)
   - Implement RefreshTokenAsync (T034-T036)
   - Write tests (T037-T039)
   - **Test independently**: Returning users don't need re-auth

2. **Add User Story 3: Error Handling** (~3-4 hours)
   - Implement OAuthErrorHandler (T040-T042)
   - Integrate error handling (T043-T047)
   - Write tests (T048-T050)
   - **Test independently**: Verify error scenarios show proper messages

3. **Final Polish** (~2-3 hours)
   - Complete all Phase 6 tasks (T051-T060)
   - Documentation updates
   - Security audit
   - Full test suite validation

**Total Time for Complete Feature**: 20-25 hours

---

### Parallel Team Strategy

**If multiple developers available:**

**Week 1 - Foundation (Together):**
- ALL: Complete Phase 1 (Setup)
- ALL: Complete Phase 2 (Foundational) - pair programming on interfaces/models

**Week 2 - User Stories (Parallel):**
- **Developer A**: User Story 1 (T015-T029) - First-time OAuth flow
- **Developer B**: User Story 2 (T030-T039) - Token refresh (can start after Phase 2)
- **Developer C**: User Story 3 (T040-T050) - Error handling (can start after Phase 2)

**Week 3 - Integration & Polish:**
- ALL: Phase 6 (T051-T060) - DI registration, startup integration, testing
- Code review and merge

**Time Savings**: ~60% reduction (15 hours total vs 25 hours sequential)

---

## Notes

- **[P]** tasks = different files, no dependencies - safe to parallelize
- **[Story]** label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Integration tests use `[Fact(Skip = "...")]` pattern per constitution
- Commit after T014 (foundational complete), after each user story completion, and after polish
- Stop at any checkpoint to validate story independently
- **Security critical**: Tasks T023, T031, T035, T059 involve token storage - audit carefully
- **No sensitive data in logs**: Verify throughout T024, T036, T042, T058
- **Result<T> pattern**: All service methods must return Result<T>, never throw for business logic - validate in T058

# Tasks: Contacts Provider Setup Dialog

**Input**: Design documents from `/specs/001-contacts-provider-setup-dialog/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Extract: Avalonia UI 11, CommunityToolkit.Mvvm, Google.Apis.Gmail.v1
   → Structure: Single project desktop application
2. Load design documents:
   → data-model.md: No new entities - reuse OAuth models
   → contracts/: Simple UI integration - wire button to dialog
   → research.md: Reuse Gmail OAuth dialog, add Contacts scope
3. Generate tasks by category:
   → Setup: Asset acquisition, scope configuration
   → Tests: UI interaction tests, OAuth flow tests
   → Core: Button wiring, provider status updates
   → Integration: OAuth dialog reuse, status management
   → Polish: Unit tests, manual validation
4. Apply task rules:
   → Asset and test tasks marked [P] for parallel
   → UI wiring tasks sequential (same components)
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Focus: Simple UI integration, no new architecture
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Single project**: `src/`, `tests/` at repository root
- Desktop application with provider architecture

## Phase 3.1: Setup
- [ ] T001 [P] Download Google Contacts logo PNG from Google brand resources
  - **Hint**: Use MCP search tools to find official Google brand resources
  - **Hint**: Use `mcp__docker-mcp__search "Google Contacts logo official brand resources"` or `mcp__docker-mcp__fetch_content` from Google's brand guidelines
- [ ] T002 [P] Add Google Contacts logo to src/TrashMailPanda/TrashMailPanda/Assets/Logos/ directory
  - **Hint**: Use standard file operations (Write tool) to add the PNG asset
- [ ] T003 Update OAuth scope configuration to include https://www.googleapis.com/auth/contacts.readonly
  - **Hint**: Use Gemini-CLI with `mcp__gemini-cli__ask-gemini` to analyze OAuth scope patterns in existing Gmail service

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: These tests MUST be written and MUST FAIL before ANY implementation**
- [ ] T004 [P] Provider card button click test in src/Tests/TrashMailPanda.Tests/Unit/ViewModels/ContactsProviderButtonTests.cs
  - **Hint**: Use Gemini-CLI to analyze existing provider button test patterns: `mcp__gemini-cli__ask-gemini "@existing_test_file explain button test patterns"`
- [ ] T005 [P] OAuth dialog display test in src/Tests/TrashMailPanda.Tests/Unit/Services/ContactsConfigurationTests.cs
  - **Hint**: Use Gemini-CLI to study OAuth dialog testing: `mcp__gemini-cli__ask-gemini "@GmailOAuthService how are dialogs tested"`
- [ ] T006 [P] Provider status update test in src/Tests/TrashMailPanda.Tests/Unit/Services/ContactsProviderStatusTests.cs
  - **Hint**: Use Gemini-CLI to analyze provider status patterns: `mcp__gemini-cli__ask-gemini "@ProviderStatusService analyze status update testing"`
- [ ] T007 [P] OAuth scope expansion integration test in src/Tests/TrashMailPanda.Tests/Integration/ContactsOAuthFlowTests.cs (marked Skip for CI)
  - **Hint**: Use Gemini-CLI to understand OAuth flow testing: `mcp__gemini-cli__ask-gemini "@existing_oauth_tests analyze integration test patterns"`

## Phase 3.3: Core Implementation (ONLY after tests are failing)
- [ ] T008 Wire Contacts provider "Configure" button to Gmail OAuth dialog in src/TrashMailPanda/TrashMailPanda/ViewModels/MainWindowViewModel.cs
  - **Hint**: Use Gemini-CLI to analyze existing button wiring: `mcp__gemini-cli__ask-gemini "@MainWindowViewModel analyze provider configuration button patterns"`
- [ ] T009 Update GmailOAuthService to include Contacts scopes in src/TrashMailPanda/TrashMailPanda/Services/GmailOAuthService.cs
  - **Hint**: Use Gemini-CLI to study scope management: `mcp__gemini-cli__ask-gemini "@GmailOAuthService how to add additional OAuth scopes"`
- [ ] T010 Update provider status management for Contacts provider in src/TrashMailPanda/TrashMailPanda/Services/ProviderStatusService.cs
  - **Hint**: Use Gemini-CLI to understand status patterns: `mcp__gemini-cli__ask-gemini "@ProviderStatusService analyze provider status update patterns"`
- [ ] T011 Add Google Contacts logo reference to provider metadata in src/TrashMailPanda/TrashMailPanda/Models/ProviderDisplayInfo.cs
  - **Hint**: Use Gemini-CLI to study logo integration: `mcp__gemini-cli__ask-gemini "@ProviderDisplayInfo how are provider logos configured"`

## Phase 3.4: Integration
- [ ] T012 Update provider health checks to validate Contacts OAuth scope in src/Providers/Contacts/TrashMailPanda.Providers.Contacts/ContactsProvider.cs
  - **Hint**: Use Gemini-CLI to analyze health check patterns: `mcp__gemini-cli__ask-gemini "@ContactsProvider analyze health check implementation patterns"`
- [ ] T013 Connect OAuth completion to both Gmail and Contacts provider status updates in src/TrashMailPanda/TrashMailPanda/Services/StartupOrchestrator.cs
  - **Hint**: Use Gemini-CLI to study orchestration: `mcp__gemini-cli__ask-gemini "@StartupOrchestrator how are provider status updates coordinated"`
- [ ] T014 Update UI bindings for Contacts provider configuration state in src/TrashMailPanda/TrashMailPanda/Views/MainWindow.axaml
  - **Hint**: Use Gemini-CLI to understand XAML binding patterns: `mcp__gemini-cli__ask-gemini "@MainWindow.axaml analyze provider UI binding patterns"`

## Phase 3.5: Polish
- [ ] T015 [P] Unit tests for OAuth scope management in src/Tests/TrashMailPanda.Tests/Unit/Services/OAuthScopeTests.cs
  - **Hint**: Use Gemini-CLI to generate test cases: `mcp__gemini-cli__ask-gemini "generate comprehensive OAuth scope unit tests for C# xUnit"`
- [ ] T016 [P] Unit tests for provider status coordination in src/Tests/TrashMailPanda.Tests/Unit/Services/ProviderCoordinationTests.cs
  - **Hint**: Use Gemini-CLI to analyze coordination patterns: `mcp__gemini-cli__ask-gemini "@existing_provider_tests generate status coordination test patterns"`
- [ ] T017 [P] Accessibility tests for Contacts provider card in src/Tests/TrashMailPanda.Tests/Unit/UI/AccessibilityTests.cs
  - **Hint**: Use MCP search to find Avalonia accessibility best practices: `mcp__docker-mcp__search "Avalonia UI accessibility testing patterns C#"`
- [ ] T018 Execute manual validation scenarios from specs/001-contacts-provider-setup-dialog/quickstart.md
  - **Hint**: Use Gemini-CLI to create test automation scripts: `mcp__gemini-cli__ask-gemini "create automated test script for manual validation scenarios"`
- [ ] T019 Validate performance targets (UI responsiveness <200ms, OAuth flow <30s)
  - **Hint**: Use Gemini-CLI to implement performance benchmarks: `mcp__gemini-cli__ask-gemini "create C# performance benchmarks for UI responsiveness and OAuth timing"`

## Dependencies
- T001-T003 (setup) before all implementation
- Tests (T004-T007) before implementation (T008-T011)
- T008 (button wiring) blocks T012 (health checks)
- T009 (OAuth service) blocks T013 (status updates)
- Implementation (T008-T014) before polish (T015-T019)

## Parallel Example
```
# Launch asset tasks together (T001-T002):
Task: "Download Google Contacts logo PNG from Google brand resources"
Task: "Add Google Contacts logo to src/TrashMailPanda/TrashMailPanda/Assets/Logos/ directory"

# Launch test tasks together (T004-T007):
Task: "Provider card button click test in src/Tests/TrashMailPanda.Tests/Unit/ViewModels/ContactsProviderButtonTests.cs"
Task: "OAuth dialog display test in src/Tests/TrashMailPanda.Tests/Unit/Services/ContactsConfigurationTests.cs"
Task: "Provider status update test in src/Tests/TrashMailPanda.Tests/Unit/Services/ContactsProviderStatusTests.cs"
Task: "OAuth scope expansion integration test in src/Tests/TrashMailPanda.Tests/Integration/ContactsOAuthFlowTests.cs"

# Launch polish tests together (T015-T017):
Task: "Unit tests for OAuth scope management in src/Tests/TrashMailPanda.Tests/Unit/Services/OAuthScopeTests.cs"
Task: "Unit tests for provider status coordination in src/Tests/TrashMailPanda.Tests/Unit/Services/ProviderCoordinationTests.cs"
Task: "Accessibility tests for Contacts provider card in src/Tests/TrashMailPanda.Tests/Unit/UI/AccessibilityTests.cs"
```

## Notes
- [P] tasks = different files, no dependencies
- Verify tests fail before implementing
- Commit after each task
- Focus: Simple UI wiring, not architecture changes
- Reuse existing OAuth dialog and provider patterns
- Integration tests marked Skip for CI (require real Google OAuth credentials)

## Available AI Tools & Hints
**Leverage these MCP tools throughout task execution:**

### Gemini-CLI Analysis (`mcp__gemini-cli__ask-gemini`)
- **Code Analysis**: Use `@filename` to analyze specific files and understand patterns
- **Changemode**: Use `changeMode: true` for structured edit suggestions that Claude can apply
- **Brainstorming**: Use `mcp__gemini-cli__brainstorm` for creative problem-solving approaches

### Online Research (`mcp__docker-mcp__search`, `mcp__docker-mcp__fetch_content`)
- **Technical Documentation**: Search for official Google APIs, Avalonia UI docs, OAuth best practices
- **Brand Resources**: Find official Google logos and brand guidelines
- **Stack Overflow**: Search for specific technical solutions and patterns

### Library Documentation (`mcp__docker-mcp__resolve-library-id`, `mcp__docker-mcp__get-library-docs`)
- **Framework Docs**: Get up-to-date documentation for Avalonia UI, CommunityToolkit.Mvvm
- **API References**: Access Google.Apis.Gmail.v1 documentation and OAuth patterns

**Example Usage Patterns:**
```bash
# Analyze existing patterns before implementing
mcp__gemini-cli__ask-gemini "@GmailOAuthService analyze OAuth scope management patterns"

# Research best practices
mcp__docker-mcp__search "Avalonia UI MVVM button command binding patterns"

# Get official documentation
mcp__docker-mcp__resolve-library-id "Avalonia UI"
mcp__docker-mcp__get-library-docs "/avalonia/avalonia" topic:"MVVM patterns"

# Generate code with structured edits
mcp__gemini-cli__ask-gemini "generate OAuth scope tests" changeMode: true
```

## Task Generation Rules
*Applied during main() execution*

1. **From Contracts**:
   - simple-ui-integration.md → button wiring and OAuth dialog reuse
   - provider-card-commands.md → provider card button functionality

2. **From Data Model**:
   - No new entities → reuse existing OAuth and provider models
   - Scope configuration → OAuth service updates

3. **From Quickstart Scenarios**:
   - Fresh setup scenario → integration test with full OAuth flow
   - Scope expansion scenario → integration test for existing Gmail users
   - Error handling → unit tests for edge cases
   - Accessibility → dedicated accessibility tests

4. **Ordering**:
   - Assets → Tests → UI Wiring → Provider Updates → Integration → Polish
   - TDD enforced: failing tests before any implementation

## Validation Checklist
*GATE: Checked by main() before returning*

- [x] All contracts have corresponding implementation tasks
- [x] No new entities needed - reusing existing models
- [x] All tests come before implementation (T004-T007 before T008-T011)
- [x] Parallel tasks truly independent (different files)
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] Simple approach: reuse existing OAuth dialog, no new architecture
- [x] Constitutional compliance: uses Result<T> pattern, follows MVVM, TDD enforced
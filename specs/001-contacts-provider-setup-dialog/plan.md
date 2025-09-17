# Implementation Plan: Contacts Provider Setup Dialog

**Branch**: `001-contacts-provider-setup-dialog` | **Date**: 2025-09-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-contacts-provider-setup-dialog/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
4. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
5. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, or `GEMINI.md` for Gemini CLI).
6. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
7. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
8. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Enable the Contacts provider "Configure" button to reuse the existing Gmail OAuth dialog for Google credentials setup, supporting both fresh authentication and scope expansion scenarios. This involves connecting the Contacts provider card UI to the established Google credentials configuration flow and adding the Google Contacts logo to maintain visual consistency. User provided context: Reuse existing Gmail credential configuration dialog without creating new components. Focus on scope expansion for existing Gmail users, though new users will get all scopes configured from the start.

## Technical Context
**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: Avalonia UI 11, CommunityToolkit.Mvvm, Microsoft.Extensions.DI, Google.Apis.Gmail.v1
**Storage**: SQLite with SQLCipher encryption via Microsoft.Data.Sqlite
**Testing**: xUnit with 90% coverage target (95% for providers, 100% for security)
**Target Platform**: Cross-platform desktop (Windows, macOS, Linux)
**Project Type**: single - desktop application with provider-agnostic architecture
**Performance Goals**: UI responsiveness <200ms, OAuth flow completion <30s
**Constraints**: Local-first processing, encrypted credentials via OS keychain, reversible actions
**Scale/Scope**: Individual user desktop app, 4 providers (Email, LLM, Storage, Contacts), MVVM with Result<T> pattern

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Simplicity**:
- Projects: 1 (TrashMailPanda desktop app) ✅
- Using framework directly? Yes - Avalonia UI, CommunityToolkit.Mvvm ✅
- Single data model? Yes - reusing existing Provider, OAuth models ✅
- Avoiding patterns? Yes - reusing existing OAuth flow, no new Repository patterns ✅

**Architecture**:
- EVERY feature as library? N/A - This is UI integration within existing provider architecture ✅
- Libraries listed: Reusing existing Google.Apis.Gmail.v1, Avalonia.Controls for modals ✅
- CLI per library: N/A - Desktop UI feature ✅
- Library docs: N/A - UI feature, follows existing documentation patterns ✅

**Testing (NON-NEGOTIABLE)**:
- RED-GREEN-Refactor cycle enforced? Yes - will write failing tests first ✅
- Git commits show tests before implementation? Yes - planned TDD approach ✅
- Order: Contract→Integration→E2E→Unit strictly followed? Yes - UI contract tests → provider integration → unit tests ✅
- Real dependencies used? Yes - actual OAuth flows, real provider instances ✅
- Integration tests for: OAuth scope expansion, provider status updates ✅
- FORBIDDEN: Implementation before test, skipping RED phase ✅

**Observability**:
- Structured logging included? Yes - using existing Microsoft.Extensions.Logging ✅
- Frontend logs → backend? N/A - desktop app with unified logging ✅
- Error context sufficient? Yes - Result<T> pattern for error handling ✅

**Versioning**:
- Version number assigned? Current BUILD + 1 (enhancement) ✅
- BUILD increments on every change? Yes ✅
- Breaking changes handled? No breaking changes - additive feature ✅

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
# Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure]
```

**Structure Decision**: Option 1 (Single project) - Desktop application with existing provider architecture

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `/scripts/bash/update-agent-context.sh claude` for your AI assistant
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy** (Simplified for UI-only feature):
- Load `/templates/tasks-template.md` as base
- Generate minimal task set based on simple UI wiring approach
- Focus on TDD implementation of button click → dialog display
- Create integration tests for OAuth flow (marked as skipped for CI)
- Add logo asset acquisition and integration
- Update provider scope configuration

**Specific Task Categories**:
1. **Asset Tasks**: Download and integrate Google Contacts logo [P]
2. **Test Tasks**: Write failing tests for button click behavior [P]
3. **UI Wiring**: Connect Contacts "Configure" button to Gmail OAuth dialog
4. **Scope Update**: Add Contacts scope to OAuth configuration
5. **Status Update**: Update provider status after OAuth completion
6. **Integration Tests**: Create OAuth flow tests (skipped for CI) [P]

**Ordering Strategy**:
- Asset acquisition first (logo download) [P]
- TDD order: Tests before implementation
- UI wiring follows existing provider patterns
- Integration tests can run in parallel [P]

**Estimated Output**: 8-12 focused, ordered tasks in tasks.md

**Simplification Note**: Much smaller task set due to reusing existing OAuth dialog and following established patterns rather than creating new architecture.

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS (Simplified design improves compliance)
- [x] All NEEDS CLARIFICATION resolved (Technical Context complete)
- [x] Complexity deviations documented (None - simplified approach meets all requirements)

---
*Based on Constitution v2.1.1 - See `/memory/constitution.md`*
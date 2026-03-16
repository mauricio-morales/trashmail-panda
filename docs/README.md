# Documentation Guidelines

> **Last Updated**: March 16, 2025  
> **Purpose**: Establish semantic organization for technical documentation in TrashMail Panda

## Documentation Structure

Documentation is organized by **topic area** (not by spec/PRP number) to make it easy to find related content:

```
docs/
├── README.md (this file)
├── IDEAS.md (project-wide ideas and future work)
│
├── architecture/        # High-level architecture decisions
├── features/            # Feature-specific docs (date-based filenames)
├── ml/                  # Machine learning and classification
├── platform/            # Platform-specific testing and deployment
├── providers/           # Provider implementations and patterns
├── security/            # Security patterns and encryption
├── storage/             # Storage layer and database
└── ui/                  # UI/Avalonia patterns and theming
```

## When to Create Documentation

Create documentation for:

1. **Architecture Changes** → `/docs/architecture/`
   - Major design shifts (e.g., local ML vs cloud)
   - System-wide architectural decisions
   - Cross-cutting concerns

2. **Feature Implementations** → `/docs/features/`
   - Completed feature work spanning multiple PRs
   - Use date-based naming: `YYYY-MM-DD-feature-name.md`
   - Include PR links and spec references when applicable

3. **Pattern Documentation** → Topic-specific folders
   - Reusable patterns for providers, UI, storage, etc.
   - Implementation guides and best practices
   - Troubleshooting guides

4. **Migration Guides** → Relevant topic folder
   - Breaking changes requiring code updates
   - Version upgrade procedures

## Naming Conventions

### For Feature/Completion Docs (in `/features/`)
```
YYYY-MM-DD-feature-name.md
```
**Example**: `2025-03-16-ef-core-storage-refactoring.md`

### For Pattern/Guide Docs (in topic folders)
```
lowercase-with-hyphens.md
```
**Example**: `gmail-oauth-implementation.md`, `storage-migration-guide.md`

### Required Metadata (at top of doc)
```markdown
# Title

> **Created**: YYYY-MM-DD  
> **Related PR**: #XX (if applicable)  
> **Related Spec**: specs/XXX-name/ (if applicable)  
> **Status**: Active | Deprecated | Superseded by [link]
```

## Linking to Specs

When documentation relates to a specific spec (in `/specs/`):

```markdown
> **Related Spec**: [055-ml-data-storage](../specs/055-ml-data-storage/)
```

This maintains traceability without forcing organization by spec number.

## Migration from Flat Structure

Historical docs (created before March 2025) were moved from flat `/docs/*.md` to semantic folders. Some may lack date context - that's expected.

## Guidelines for AI Agents

When creating documentation:

1. **Check existing structure first**: `ls docs/*/`
2. **Use appropriate folder**: Match topic to folder structure above
3. **Include metadata**: Date, PR, spec reference when known
4. **Use semantic names**: Lowercase with hyphens, descriptive
5. **Link related docs**: Cross-reference other docs when relevant

## Examples

```
✅ Good:
docs/features/2025-03-16-ef-core-storage-refactoring.md
docs/storage/storage-migration-guide.md
docs/providers/gmail-oauth-implementation.md

❌ Avoid:
docs/PHASE_3_4_COMPLETION_SUMMARY.md (no context or date)
docs/NEW_FEATURE.md (flat structure, uppercase, vague)
docs/MY_DOC_2.md (numbered versions without semantic name)
```

## Questions?

See [CLAUDE.md](../CLAUDE.md) for general project guidelines or [PRPs/README.md](../PRPs/README.md) for the PRP methodology.

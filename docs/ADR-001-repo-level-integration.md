# ADR-001: Repo-Level Integration, Not Skill-Level

**Status:** Accepted  
**Date:** 2026-02-27  
**Decision makers:** Lyndon Swan

## Context

Total.Recall is an MCP server that provides persistent memory (type metadata, coverage gaps, gotchas, mock recipes, test inventory) for AI-driven coverage uplift work. It needs to integrate with the `coverage-uplift` Copilot skill, which orchestrates test generation across .NET repos.

The question: **where do we put the instructions that tell the AI agent to use MCP tools instead of reading source files?**

Two options were considered:

### Option A: Modify the coverage-uplift skill

Add MCP awareness directly to `SKILL.md`, `coverage-generate-tests.prompt.md`, and `coverage-bootstrap.prompt.md`. The skill would detect MCP tools and switch to an accelerated workflow.

### Option B: Repo-level configuration only

Leave the skill untouched. Instead, wire MCP guidance through two repo-level mechanisms that the skill already reads:
1. **`AGENTS.md`** — The skill reads this at the start of every workflow step. Add a `## Total.Recall MCP Integration` section with tool descriptions, workflow patterns, and fallback guidance.
2. **`.github/copilot-instructions.md`** — VS Code auto-injects this into every Copilot conversation. It provides the "MCP is available, prefer it" nudge before the skill even activates.

## Decision

**Option B — Repo-level only.** The coverage-uplift skill remains completely unmodified.

## Rationale

### 1. Skill portability

The coverage-uplift skill is portable across all .NET repos. Most repos won't have Total.Recall. If MCP detection logic is baked into the skill, every prompt file gains conditional branches (`if MCP available → do X, else → do Y`) that add complexity for the majority of users who will never see the benefit.

### 2. Separation of concerns

The skill defines **what to do** (type survey → generate tests → build → fix → commit). Total.Recall provides **a faster way to do it** (MCP tools vs file reading). These are orthogonal concerns. The skill's workflows don't change — only the data source does.

### 3. The architecture already supports it

The skill was explicitly designed with this layering:
- **Skill files** (`SKILL.md`, prompts, instructions) — portable, read-only, never modified per-repo
- **`AGENTS.md`** — repo-specific working memory, regenerated on bootstrap, read by every prompt
- **`.github/copilot-instructions.md`** — VS Code auto-injects into context

This is exactly the extension point pattern. AGENTS.md is documented as "Claude's working memory" that gets customized per repo. Adding MCP guidance there is the intended use.

### 4. No version coupling

If Total.Recall changes its tool signatures (adds parameters, renames tools, adds new tools), only the Linter repo's AGENTS.md and copilot-instructions.md need updating. The skill continues working everywhere else without a version bump.

### 5. Graceful degradation

If Total.Recall's MCP server isn't running, the agent sees no MCP tools. It reads AGENTS.md, finds the MCP section irrelevant, and falls back to the standard file-reading workflow that the skill defines. Zero breakage.

## Consequences

### Positive
- Coverage-uplift skill stays clean and portable
- Any repo can adopt Total.Recall by adding two files (`.vscode/mcp.json` + AGENTS.md section)
- Total.Recall can evolve independently
- Other MCP servers could be integrated the same way (no skill changes needed)

### Negative
- Each repo that uses Total.Recall needs its own AGENTS.md section and copilot-instructions (copy-paste, ~40 lines)
- The MCP guidance is less structured than if it were in the skill's prompt files (natural language in AGENTS.md vs explicit workflow steps)
- If the skill's type survey step changes, AGENTS.md's "use instead of" mapping may need updating

### Mitigations
- Total.Recall's `docs/QUICKSTART.md` includes the exact AGENTS.md section and copilot-instructions content to copy
- The AGENTS.md section includes explicit "when to fall back to file reading" guidance to prevent over-reliance on potentially stale MCP data

## Integration Points

```
┌─────────────────────────┐     ┌──────────────────────────┐
│  coverage-uplift skill  │     │    Total.Recall MCP       │
│  (UNCHANGED)            │     │    (separate repo)        │
│                         │     │                           │
│  SKILL.md               │     │  v2 Decision Engine:      │
│  prompts/               │     │    GetTestableTargets     │
│  instructions/          │     │    GetSourceSnippet       │
│                         │     │    GenerateTestScaffold   │
│  reads ↓                │     │    LogSession/GetSessions │
│  AGENTS.md              │     │                           │
│  (repo-specific)        │     │  v1 Lookup Index:         │
│                         │     │    GetContext/ResolveType  │
│                         │     │    GetCoverageGaps         │
│                         │     │    GetGotchas/AddGotcha   │
│                         │     │    GetMockRecipe           │
│                         │     │    GetTestInventory        │
│                         │     │    Add/GetAssessments     │
│                         │     │    GetMetrics              │
└────────────┬────────────┘     └────────────┬─────────────┘
             │                                │
             │    ┌─────────────────────┐     │
             └────│  Target Repo        │─────┘
                  │  (e.g. Linter)      │
                  │                     │
                  │  AGENTS.md          │ ← MCP section added here
                  │  .github/copilot-   │ ← auto-injected by VS Code
                  │    instructions.md  │
                  │  .vscode/mcp.json   │ ← wires MCP server (4 env vars)
                  └─────────────────────┘
```

The skill reads AGENTS.md → finds MCP guidance → uses MCP tools for type survey, target scoring, scaffold generation, and session logging. If MCP isn't there, the skill's standard file-reading workflow runs unchanged.

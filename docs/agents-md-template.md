<!-- Total.Recall — AGENTS.md template
     Add this section to your target repo's AGENTS.md (create the file if it doesn't exist).
     Read by Copilot agent mode at the start of every task.

     CUSTOMIZE: Replace placeholder paths/namespace/counts below before committing.
-->

## Total.Recall MCP Integration

This repo has a Total.Recall MCP server providing persistent memory for test generation
and coverage uplift. Configured in `.vscode/mcp.json`. All tools accept an optional `ns`
(namespace) parameter to target a specific dataset.

### What It Does

Total.Recall eliminates the 60–70% of agent context burned on re-discovering type metadata,
mock patterns, coverage gaps, and gotchas each session. Scanners extract data from your
assembly, coverage reports, and test project into JSONL files. 34 MCP tools expose this data
instantly — one tool call replaces 10–15 file reads.

### Tool Reference

#### v2 — Decision Engine

| Tool | Purpose | Key Params |
|------|---------|------------|
| `GetTestableTargets` | Pre-scored target list. **First call of every session.** | `top`, `maxCtorParams`, `maxTotalLines`, `excludeAssessed`, `requireZeroTests` |
| `GetSourceSnippet` | Actual C# source from target repo | `className`, `methodName`, `maxLines` |
| `GenerateTestScaffold` | Complete test class: usings, mocks, ctor, [Fact] stubs, gotcha comments | `className`, `methodNames` |
| `LogSession` | Write session outcomes. **Last call of every session.** | `model`, `promptTokens`, `completionTokens`, `classesAttempted`, `classesSucceeded`, `classesFailed`, `testsGenerated`, `coverageBefore`, `coverageAfter`, `coveredLines`, `gotchasDiscovered`, `assessmentsRecorded`, `notes` |
| `GetSessions` | Session history + aggregate analytics + plateau detection | `last` |
| `GetUncoveredMethods` | Method-level ROI targets when class-level is exhausted | `top`, `minUncoveredLines`, `onlyWithExistingTests`, `excludeBoilerplate` |
| `GetStubClasses` | Zero-coverage trivially-testable classes (POCOs, stubs, helpers) | `top`, `maxCoveragePercent`, `maxCtorParams`, `includeWithTests` |

#### v1 — Lookup Index

| Tool | Purpose | Key Params |
|------|---------|------------|
| `ResolveType` | Namespace + constructor + property lookup (O(1) exact match) | `typeName`, `namespacePart`, `filePath` |
| `GetContext` | Combined: type + gotchas + tests + mocks + assessments + coverage + sessions | `typeName` |
| `GetMockRecipe` | Pre-built Moq setup code for an interface | `interfaceName` |
| `GetCoverageGaps` | ROI-ranked uncovered classes + methods | `top`, `sortBy`, `skipUntestable` |
| `GetGotchas` | Known pitfalls for a type | `typeName` |
| `AddGotcha` | Record a new pitfall (append-only) | `typeName`, `category`, `gotcha` |
| `GetTestInventory` | Existing test methods per class | `className` |
| `AddAssessment` | Record testability verdict | `className`, `verdict`, `reasoning`, `deps`, `cluster` |
| `GetAssessments` | Previous verdicts (deduplicated — last wins) | `className`, `verdict` |
| `GetMetrics` | Server telemetry: tool calls, cache hits, uptime | (none) |

#### Static Analysis

| Tool | Purpose | Key Params |
|------|---------|------------|
| `GetClassMetrics` | Coupling (Ca/Ce), instability, archetype, dependency lists | `className` |
| `GetDependencyGraph` | Local subgraph: deps, consumers, Mermaid diagram | `className`, `depth` |
| `GetAnalysisSummary` | Architectural overview: hot interfaces, clusters, coupling | (none) |

#### Observability & Learning

| Tool | Purpose | Key Params |
|------|---------|------------|
| `LearnTestPatterns` | Analyze existing tests for naming, assertion, mock conventions | `maxFiles` |
| `GetGotchaInsights` | Cluster gotchas into patterns, generate Footguns docs | `minClusterSize`, `generateFootguns` |
| `RefreshCoverage` | Re-parse Cobertura XML mid-session without full rescan | `coveragePath` |

### Coverage Uplift Workflow

```
1. GetTestableTargets(top: 5)          ← pick targets by ROI score
2. For each target:
   a. GetSourceSnippet(className)      ← read the implementation
   b. GenerateTestScaffold(className)  ← get compilable test skeleton
   c. Fill in test logic using:
      - GetContext / GetMockRecipe / GetGotchas for type-specific data
      - AddGotcha for any new pitfalls discovered
      - AddAssessment if a class turns out to be untestable
3. LogSession(...)                     ← persist outcomes for next session
```

### Re-scan Commands

<!-- CUSTOMIZE: Replace paths with your actual locations -->

```bash
# Full scan (first time)
dotnet run --project C:\path\to\Total.Recall\src\Total.Recall\Total.Recall.csproj -- scan ^
  --assembly "C:\path\to\YourProject.dll" ^
  --coverage "C:\path\to\coverage.cobertura.xml" ^
  --tests "C:\path\to\YourProject.Tests" ^
  --source-root "C:\path\to\your-repo\src" ^
  --namespace your-namespace ^
  --enrich

# Watch mode: auto-rescan on file changes (recommended for active development)
dotnet run --project C:\path\to\Total.Recall\src\Total.Recall\Total.Recall.csproj -- scan ^
  --assembly "C:\path\to\YourProject.dll" ^
  --coverage "C:\path\to\coverage.cobertura.xml" ^
  --tests "C:\path\to\YourProject.Tests" ^
  --namespace your-namespace ^
  --enrich --analyze --watch

# Re-scan coverage after a test run (if not using --watch)
dotnet run --project C:\path\to\Total.Recall\src\Total.Recall\Total.Recall.csproj -- scan ^
  --coverage "TestResults\<guid>\coverage.cobertura.xml" ^
  --namespace your-namespace --enrich

# Just re-enrich existing data
dotnet run --project C:\path\to\Total.Recall\src\Total.Recall\Total.Recall.csproj -- scan ^
  --namespace your-namespace --enrich
```

### Data Sources

All data lives under `$TOTAL_RECALL_DATA/{namespace}/`:

| File | Updated By | Description |
|------|-----------|-------------|
| `type-registry.jsonl` | `--assembly` scan | Every public/internal type in the target assembly |
| `coverage-gaps.jsonl` | `--coverage` scan | Uncovered lines/methods per class |
| `test-inventory.jsonl` | `--tests` scan | Existing test methods per class |
| `gotchas.jsonl` | `AddGotcha` tool + manual seeding | Type-specific traps and workarounds (append-only) |
| `mock-recipes.jsonl` | Manual curation | Pre-built Moq setup code per interface |
| `assessments.jsonl` | `AddAssessment` tool | Testability verdicts (append-only, last wins) |
| `sessions.jsonl` | `LogSession` tool | Session outcomes for cross-session learning (append-only) |
| `config.json` | Scanner `--source-root` | Persisted scan config (source root, paths, timestamp) |

### Fallback

If the MCP server is unavailable (not running, wrong namespace, missing data), fall back to:
- `read_file` for source code
- Manual Cobertura XML parsing for coverage
- Reading test files directly for test inventory

No tool is a hard dependency. The standard file-reading workflow still works — MCP just makes it faster.

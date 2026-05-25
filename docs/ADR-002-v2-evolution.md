# ADR-002: Total.Recall v2 — From Lookup Index to Decision Engine

**Status:** Accepted  
**Date:** 2026-02-27  
**Decision makers:** Lyndon Swan

---

## Context

After using Total.Recall v1 for one full coverage-uplift session (97 tests, 21 types, coverage 25.66% → 54.1%), we conducted an honest retrospective on where MCP helped and where it didn't. The findings were blunt:

### What v1 did well
- **`GetCoverageGaps(sortBy: "roi")`** — gave method-level uncovered data ranked by ROI instantly, without parsing Cobertura XML manually. Genuinely useful for target selection.
- **`GetContext()`** — gave type metadata (constructors, interfaces, dependencies) that let us quickly reject complex classes (e.g. `EntryBuilder` → `TestHarnessBase`, `ConfigUpdater` → `OrchestratorService`, `DocumentLock` → `OrchestratorService`) without reading each source file.
- **`AddGotcha()`** — persists discoveries for future sessions. Cumulative value.

### Where v1 added zero value
- **Test writing (90% of the work)**: MCP gives signatures, not implementations. We read every source file anyway — all 8 files in cluster A, all 6 in cluster B, plus 6+ dependency files. The actual test authoring was pure file-reading + code generation.
- **`GetMockRecipe()` and `GetTestInventory()`** — never used this session.
- **Target filtering**: We needed a runSubagent search to find testable untested classes because MCP's coverage data didn't filter by DI complexity or testability.
- **No feedback loop**: Token usage, which classes worked, which failed, how many tests per class, coverage delta — all vanished when the session ended.

### Honest speed estimate
MCP saved ~15-20% of the target selection phase, which was ~15% of total session time. **Overall speedup: ~3-5%.** The 97 tests and coverage jump happened because of reading source and writing tests — not MCP.

### Root cause analysis
v1 is a **read-only lookup index**. It answers "what exists?" and "what's uncovered?" but the hard part is "how does this code actually behave?" and "what should I test next?" — questions that require implementation details and cross-data-source reasoning. v1 forces the agent to do all the joining, filtering, and scaffolding work manually.

---

## Decision

Evolve Total.Recall from a read-only lookup index into a **bidirectional decision engine** with four new capabilities:

1. **Actionable target selection** (`get_testable_targets`) — Cross-join all data sources into pre-filtered, pre-scored "here's your next N classes" output  
2. **Source access** (`get_source_snippet`) — Serve actual method implementations from the target repo, eliminating the "I have to read the file anyway" problem  
3. **Test scaffolding** (`generate_test_scaffold`) — Combine type metadata + mock recipes + coverage gaps into ready-to-fill test class skeletons  
4. **Session telemetry** (`log_session` / `get_sessions`) — Bidirectional data flow: agent writes session outcomes (tokens, tests, coverage delta, failures) back to MCP for cross-session learning  
5. **Scanner overhaul** — Replace clunky CLI arg parsing with a cleaner design; add `--source-root` to enable source snippet serving

---

## Rationale: Why each improvement, with evidence

### 1. `get_testable_targets` — because manual target filtering burned 15 minutes

**Evidence**: During the session, we called `GetContext()` on 8+ classes just to reject them for having complex DI. We also needed a `runSubagent` search to find candidates matching "0 tests, <350 lines, low DI complexity" because no single MCP tool could cross-join coverage + type registry + test inventory + assessments.

**Insight**: The data to make this decision already exists in 4 separate JSONL files. v1 forces the agent to query each one, load results into context, then manually correlate. That's ~4 tool calls + reasoning tokens when one tool could return a ranked list with DI complexity pre-computed.

**Scoring formula**:
```
score = uncoveredLines
      × testabilityMultiplier         (1.0 high, 0.7 medium, 0.3 low)
      × ctorSimplicityMultiplier      (1.0 for 0-2 params, 0.7 for 3-4, 0.3 for 5+)  
      × mockCoverageMultiplier        (1.0 if all interface params have recipes, 0.7 partial, 0.5 none)
      / (1 + existingTestCount)
      / (1 + gotchaCount × 0.1)       (more gotchas = more risk)
```

**Filtering** (pre-applied, not post-hoc):
- `maxCtorParams` — exclude classes with heavy DI (default: 5)
- `maxTotalLines` — exclude behemoths (default: 500)
- `excludeAbstract` — skip abstract classes by default
- `excludeAssessed` — skip classes with existing "skip" or "coupled" assessments
- `requireZeroTests` — only show classes with no existing tests (default: false)

### 2. `get_source_snippet` — because we read every source file anyway

**Evidence**: For every type we decided to test, we called `read_file` on the full `.cs` file. The type survey phase (~20 files across two clusters plus dependencies) was ALL done via `read_file`, not MCP. MCP gives signatures, not implementations.

**Insight**: CoverageGap records already contain relative file paths (e.g., `Parsing\Models\OrderSet\OrderSet.cs`) and uncovered method line ranges (e.g., `GetUrlsFromFile: L658-L790`). If we know the target repo's source root, we can serve the actual method body directly. The agent asks "show me the uncovered methods of OrderSet" and gets implementation code — no `read_file` needed.

**Design choices**:
- New env var: `TOTAL_RECALL_SOURCE_ROOT` — points to the target repo's source root (e.g., `C:\Repos\MyProject\src\LanguageServer\Server`)
- Returns up to 200 lines of source (configurable) centered on uncovered methods
- Falls back gracefully: if source root not set or file not found, returns clear error suggesting `read_file` instead
- Security: only serves files under the declared source root (no path traversal)

### 3. `generate_test_scaffold` — because test boilerplate is 100% deterministic

**Evidence**: Every test class we wrote this session followed the exact same pattern:
1. `using` statements derived from the type's namespace + its dependency namespaces
2. Class declaration inheriting nothing (or IDisposable for mocks)
3. Private field declarations for each constructor parameter (as `Mock<IFoo>` for interfaces, or concrete types)
4. Constructor setting up mocks and creating the SUT
5. Test methods — one per uncovered method — with `[Fact]` attribute, Arrange/Act/Assert skeleton

Steps 1-4 are **fully deterministic** from data we already have: TypeRecord (constructors, namespace), MockRecipe (setup code), CoverageGap (uncovered methods). Only step 5 requires reading the implementation. By generating the scaffold, we eliminate ~30-50 lines of boilerplate per test class and ensure correct `using` statements (a major source of build failures in v1).

**What the scaffold includes**:
- All required `using` statements (type namespace + dependency namespaces + Moq + Xunit)
- Mock field declarations for every interface constructor parameter
- Concrete default values for non-interface constructor parameters (string → `""`, int → `0`, bool → `false`, enum → first value)
- Test class with private fields + constructor that wires everything up
- One `[Fact]` method stub per uncovered method (name only, body is `// TODO: implement`)
- Gotcha comments — any known gotchas for this type injected as `// ⚠️ GOTCHA:` comments at the top
- Mock recipe `Setup()` calls embedded in the constructor

### 4. `log_session` / `get_sessions` — because Total.Recall must be bidirectional

**Evidence**: After the session, we had no record of: which model was used, how many tokens were burned, which classes we tested, which we rejected, the coverage delta achieved, or which gotchas we discovered. All of that signal evaporated. The next session starts from scratch with zero knowledge of what worked.

**Insight**: The agent already knows all this data during the session. It just has no place to put it. `AddGotcha()` and `AddAssessment()` are proof that write-back tools work and compound. Sessions should be first-class data too.

**Session record captures**:
- `sessionId` — auto-generated UUID
- `startedUtc` / `endedUtc` — session timestamps
- `model` — which LLM model was used (e.g., "claude-sonnet-4-20250514")
- `promptTokens` / `completionTokens` / `totalTokens` — token usage (agent-reported)
- `classesAttempted` — list of class names the agent tried to test
- `classesSucceeded` — list of class names that compiled + passed
- `classesFailed` — list of class names that failed with reasons
- `testsGenerated` — total test count
- `coverageBefore` / `coverageAfter` — line coverage percentages
- `coverageDelta` — computed delta
- `gotchasDiscovered` — count of new gotchas added this session
- `assessmentsRecorded` — count of new assessments
- `notes` — free-form session notes / learnings

**Future value**: With 5-10 sessions logged, patterns emerge: which class shapes succeed on first try, which models are most token-efficient, average tests-per-class, coverage gain per token spent. This data feeds back into `get_testable_targets` scoring (classes similar to past successes score higher).

### 5. Scanner overhaul — because the CLI is clunky

**Evidence**: The current scanner CLI uses manual `for (int i = 1; i < args.Length - 1; i++)` arg parsing with `switch` on `--assembly`, `--coverage`, `--tests`, `--output`, `--namespace`. Adding `--source-root` means another case. The pattern doesn't scale and has no validation, no help text, no error messages for missing arguments.

**Changes**:
- Proper `ScanOptions` record with validation
- Helpful error messages when required args are missing
- `--source-root` argument to configure source snippet serving
- Source root persisted to `{dataDir}/config.json` so MCP server reads it at startup
- `--enrich` flag to cross-reference coverage gaps with type registry and test inventory (fills in `existingTestCount` and `testability` fields)

---

## Architecture: v2 System Diagram

```
┌──────────────────────────────────────────────────────────────────┐
│ VS Code (Target Repo Workspace)                                  │
│                                                                  │
│  Agent ──── MCP stdio ────────────────────────────────────┐     │
│    │                                                       │     │
│    │  "Give me the next 5 testable targets"                │     │
│    │  "Show me the source of OrderSet.GetUrlsFromFile"      │     │
│    │  "Generate a test scaffold for OrderProcessor"        │     │
│    │  "Log this session: 97 tests, 25%→54% coverage"      │     │
│    │                                                       │     │
└────┼───────────────────────────────────────────────────────┼─────┘
     │                                                       │
     ▼                                                       ▼
┌──────────────────────────────────────────────────────────────────┐
│ Total.Recall MCP Server v2                                       │
│                                                                  │
│  ┌─── v1 TOOLS (unchanged) ────────────────────────────────────┐│
│  │ resolve_type │ get_context │ get_coverage_gaps │ get_gotchas ││
│  │ add_gotcha   │ get_mock_recipe │ get_test_inventory          ││
│  │ add_assessment │ get_assessments │ get_metrics               ││
│  └──────────────────────────────────────────────────────────────┘│
│                                                                  │
│  ┌─── v2 TOOLS (NEW) ─────────────────────────────────────────┐ │
│  │                                                             │ │
│  │  get_testable_targets                                       │ │
│  │    Cross-joins: coverage + types + tests + assessments      │ │
│  │    Pre-filters: maxCtorParams, maxLines, excludeAssessed    │ │
│  │    Pre-scores: ROI × ctor simplicity × mock coverage        │ │
│  │    Returns: ranked TestableTarget[] ready to act on         │ │
│  │                                                             │ │
│  │  get_source_snippet                                         │ │
│  │    Input: className + optional methodName                   │ │
│  │    Resolves: coverage file path → source root → real file   │ │
│  │    Returns: actual C# source code (up to 200 lines)        │ │
│  │                                                             │ │
│  │  generate_test_scaffold                                     │ │
│  │    Input: className                                         │ │
│  │    Combines: TypeRecord + MockRecipes + CoverageGaps + …    │ │
│  │    Returns: complete .cs test file skeleton                 │ │
│  │                                                             │ │
│  │  log_session                                                │ │
│  │    Input: model, tokens, classes, coverage delta             │ │
│  │    Writes: sessions.jsonl (append-only)                     │ │
│  │    Returns: confirmation + session ID                       │ │
│  │                                                             │ │
│  │  get_sessions                                               │ │
│  │    Input: optional filters (last N, date range)             │ │
│  │    Returns: session history + aggregate stats               │ │
│  │                                                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─── INFRASTRUCTURE ──────────────────────────────────────────┐ │
│  │ StoreRegistry (+ Sessions store, + NamespaceConfig)         │ │
│  │ JsonLineStore<T> (unchanged)                                │ │
│  │ RepoConfig (+ SOURCE_ROOT env var, + config.json reader)    │ │
│  │ Metrics (+ new counter names)                               │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌─── DATA LAYER ─────────────────────────────────────────────┐ │
│  │ data/{ns}/                                                  │ │
│  │   type-registry.jsonl     ← AssemblyScanner                 │ │
│  │   coverage-gaps.jsonl     ← CoberturaParser                 │ │
│  │   test-inventory.jsonl    ← TestProjectScanner              │ │
│  │   mock-recipes.jsonl      ← manually curated                │ │
│  │   gotchas.jsonl           ← add_gotcha (append-only)        │ │
│  │   assessments.jsonl       ← add_assessment (append-only)    │ │
│  │   sessions.jsonl          ← log_session (NEW, append-only)  │ │
│  │   config.json             ← scanner writes (NEW)            │ │
│  └─────────────────────────────────────────────────────────────┘ │
│         │                                                        │
│         │ get_source_snippet reads from:                         │
│         ▼                                                        │
│  ┌─────────────────────────────────────────┐                     │
│  │ Target Repo Source Root                  │                     │
│  │ (e.g. MyProject/src/LanguageServer/Server/) │                  │
│  │                                          │                     │
│  │ Resolved via: TOTAL_RECALL_SOURCE_ROOT   │                     │
│  │            or config.json.sourceRoot      │                     │
│  └─────────────────────────────────────────┘                     │
└──────────────────────────────────────────────────────────────────┘
```

---

## Data Models: New

### SessionRecord (sessions.jsonl)

```json
{
  "sessionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "startedUtc": "2026-02-27T10:30:00Z",
  "endedUtc": "2026-02-27T14:45:00Z",
  "model": "claude-sonnet-4-20250514",
  "promptTokens": 850000,
  "completionTokens": 320000,
  "totalTokens": 1170000,
  "classesAttempted": ["OrderProcessor", "InvoiceSchema", "DocumentSet", "ConfigUpdater"],
  "classesSucceeded": ["OrderProcessor", "InvoiceSchema", "DocumentSet"],
  "classesFailed": [
    { "class": "ConfigUpdater", "reason": "Heavy DI: OrchestratorService ctor requires 8 services" }
  ],
  "testsGenerated": 97,
  "coverageBefore": 25.66,
  "coverageAfter": 54.1,
  "coverageDelta": 28.44,
  "gotchasDiscovered": 4,
  "assessmentsRecorded": 12,
  "notes": "Schema cluster was high-yield. Cluster B (ConfigUpdater, DocumentLock) rejected for DI complexity."
}
```

### TestableTarget (output of get_testable_targets)

```json
{
  "class": "OrderProcessor",
  "namespace": "MyApp.Orders.Processing",
  "file": "Orders/Processing/OrderProcessor.cs",
  "totalLines": 412,
  "uncoveredLines": 256,
  "coveragePercent": 37.86,
  "uncoveredMethodCount": 5,
  "uncoveredMethods": ["SetParameters", "Build", "Parse", "GetRange", "Clone"],
  "existingTestCount": 18,
  "ctorParamCount": 2,
  "ctorParams": ["int index", "OrderContext context"],
  "mockableParamCount": 0,
  "recipeCoveredParams": 0,
  "baseType": "OrderProcessorBase",
  "isAbstract": false,
  "isStatic": false,
  "previousVerdict": null,
  "gotchaCount": 2,
  "score": 187.5,
  "reason": "256 uncovered lines, simple ctor (2 params, 0 interfaces), 5 untested methods, 2 known gotchas"
}
```

### NamespaceConfig (config.json)

```json
{
  "sourceRoot": "C:\\Repos\\MyProject\\src\\LanguageServer\\Server",
  "scannedUtc": "2026-02-27T10:00:00Z",
  "assemblyPath": "...\\Server.dll",
  "coveragePath": "...\\coverage.cobertura.xml",
  "testsPath": "...\\UnitTest"
}
```

---

## Tool Specifications: v2

### `get_testable_targets`

| Field | Value |
|-------|-------|
| Input | `top` (int, default: 10), `maxCtorParams` (int, default: 5), `maxTotalLines` (int, default: 500), `excludeAbstract` (bool, default: true), `excludeAssessed` (bool, default: true), `requireZeroTests` (bool, default: false), `ns` (string?) |
| Output | JSON array of `TestableTarget`, pre-scored and pre-filtered |
| Data Sources | coverage-gaps.jsonl + type-registry.jsonl + test-inventory.jsonl + assessments.jsonl + gotchas.jsonl + mock-recipes.jsonl |

**Scoring algorithm**: see Rationale §1 above.

### `get_source_snippet`

| Field | Value |
|-------|-------|
| Input | `className` (string, required), `methodName` (string, optional), `maxLines` (int, default: 200), `ns` (string?) |
| Output | JSON with `filePath`, `startLine`, `endLine`, `source` (string) |
| Resolution | className → coverage-gaps.jsonl (file path) → source root + relative path → read file lines |
| Fallback | If source root not configured or file not found, returns helpful error |

**Security**: Validates that the resolved path is under the declared source root. No `..` traversal.

### `generate_test_scaffold`

| Field | Value |
|-------|-------|
| Input | `className` (string, required), `ns` (string?) |
| Output | Complete C# test file as a string |
| Data Sources | type-registry.jsonl (constructors, properties, namespace), mock-recipes.jsonl (interface setup), coverage-gaps.jsonl (uncovered methods), gotchas.jsonl (warnings) |

### `log_session`

| Field | Value |
|-------|-------|
| Input | `model` (string), `promptTokens` (int), `completionTokens` (int), `classesAttempted` (string), `classesSucceeded` (string), `classesFailed` (string), `testsGenerated` (int), `coverageBefore` (double), `coverageAfter` (double), `notes` (string?), `ns` (string?) |
| Output | Confirmation + session ID |
| Data Source | Appends to sessions.jsonl |

Note: `classesAttempted`, `classesSucceeded`, `classesFailed` are comma-separated strings (MCP tool params are primitives). Parsed server-side.

### `get_sessions`

| Field | Value |
|-------|-------|
| Input | `last` (int, default: 5), `ns` (string?) |
| Output | JSON array of sessions + aggregate stats (total tests, total coverage delta, avg tokens per test) |

---

## Scanner Overhaul

### Before (v1)
```
Manual switch/case on args[i]:
  --assembly → assemblyPath
  --coverage → coveragePath
  --tests → testsPath
  --output → outputPath
  --namespace → namespaceName
No validation. No help text. No --source-root.
```

### After (v2)
```
ScanOptions record parsed from args:
  --assembly     Path to target .dll
  --coverage     Path to Cobertura XML
  --tests        Path to test project directory
  --source-root  Path to target source root (enables get_source_snippet)
  --output       Override data directory
  --namespace    Namespace subdirectory name
  --enrich       Cross-reference coverage with type registry + test inventory
  --help         Show usage

Validation:
  - At least one of --assembly, --coverage, --tests required
  - Paths must exist
  - --source-root must be a directory
  
Post-scan:
  - Writes config.json with sourceRoot, paths, timestamp
  - If --enrich: fills existingTestCount + testability on coverage gaps
```

---

## Environment Variables: v2

| Variable | Purpose | Default |
|----------|---------|---------|
| `TOTAL_RECALL_DATA` | Root data directory | `data` |
| `TOTAL_RECALL_NAMESPACE` | Default namespace subdirectory | `default` |
| `TOTAL_RECALL_SOURCE_ROOT` | Target repo source root (for `get_source_snippet`) | none (reads from config.json) |

---

## Consequences

### Positive
- **Target selection becomes one tool call** instead of 4+ calls + manual reasoning. Estimated savings: 10-15 minutes per session.
- **Source access eliminates read_file fallback** for the majority of "show me how this method works" queries. Estimated savings: 60-70% of file-reading tool calls.
- **Test scaffolds eliminate boilerplate errors**. First-build success rate should increase from ~30% to ~60% (correct `using` statements and mock wiring).
- **Session logging creates a learning feedback loop**. After 5+ sessions, we can measure tokens-per-test, identify which class shapes succeed, and tune scoring.
- **Scanner overhaul** makes the CLI usable and extensible for future flags.

### Negative
- More code to maintain (4 new tools, 2 new models, scanner refactor)
- `get_source_snippet` creates a dependency on the target repo's file layout being stable
- Session data is agent-reported (not verified) — agents could report inaccurate token counts
- `generate_test_scaffold` output may not match the agent's preferred test style

### Mitigations
- New tools follow the exact same patterns as v1 tools (static class, `[McpServerTool]`, StoreRegistry, SharedJsonOptions). Maintenance cost is linear, not exponential.
- Source snippet tool degrades gracefully — returns a clear "configure source root" message, not a crash.
- Session data accuracy improves over time as we compare logged expectations vs actual coverage report deltas.
- Scaffold tool generates idiomatic xUnit + Moq patterns that match the target project's existing test style.

---

## Migration Path

v2 is **additive only**. All v1 tools, models, data files, and behavior are unchanged. The upgrade is:

1. Add 4 new tools (new `.cs` files in `Tools/`)
2. Add 2 new models (`TestableTarget.cs`, `SessionRecord.cs`)
3. Add `config.json` reader to `RepoConfig`
4. Add `Sessions` store to `StoreRegistry` / `NamespaceStores`
5. Add new metric counter names to `Metrics`
6. Refactor `Program.cs` scanner section (cleaner arg parsing, `--source-root`, `--enrich`)
7. Update `AGENTS.md` and `docs/TOOL_REFERENCE.md`

Zero breaking changes to existing tools or data files.

---

## Implementation Order

```
Phase 1 (parallel, no dependencies):
  ├── SessionRecord model + SessionTool (log_session, get_sessions)
  ├── TestableTargetsTool (get_testable_targets)  
  └── SourceSnippetTool (get_source_snippet)

Phase 2 (depends on Phase 1):
  ├── TestScaffoldTool (combines type + mock + coverage data)
  └── Scanner overhaul (--source-root, --enrich, config.json)

Phase 3:
  ├── Infrastructure updates (StoreRegistry, Metrics, RepoConfig)
  ├── Build + validate
  └── AGENTS.md + docs update
```

Estimated effort: ~6-8 hours total.

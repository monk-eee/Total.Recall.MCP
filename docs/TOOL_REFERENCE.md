# Total.Recall — Tool Reference

Complete reference for all 34 MCP tools exposed by the server.

All tools accept an optional `ns` parameter to target a specific namespace dataset.

---

## v2 Tools (Decision Engine)

### get_testable_targets

**Purpose**: Pre-scored, pre-filtered list of "here's your next N classes to test." Cross-joins 6 data sources so you don't have to.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 10 | Max results to return |
| `maxCtorParams` | int | no | 5 | Max constructor params to include (lower = simpler DI) |
| `maxTotalLines` | int | no | 500 | Max total lines in class |
| `excludeAbstract` | bool | no | true | Exclude abstract classes |
| `excludeAssessed` | bool | no | true | Exclude classes with "skip"/"coupled" assessments |
| `requireZeroTests` | bool | no | false | Only show classes with zero existing tests |

**Scoring formula**:
```
base       = 10 * log₂(1 + uncoveredLines)     // log-scaled to prevent raw line count dominance

score = base
      × ctorMockability                         // all-interface=1.0×; per concrete param 0.3×
      × mockCoverage                             // 1.0 all mocked, 0.7 partial, 0.5 none
      × sizeSweetSpot                             // <50=0.7×, 50–99=0.9×, 100–400=1.3×, 400–800=1.0×, >800=0.8×
      × methodCountBoost                          // log₂(realMethods) mild bonus for logic-rich classes
      × hasTestFile ? 1.5 : 1.0                   // extending existing test file is cheaper
      / (1 + existingTestCount)                    // diminishing returns (cliff at 15: +0.3×)
      / (1 + gotchaCount × 0.1)
      × penaltyChain                              // gotcha→interface 0.7×, unmockable-interface 0.2×,
                                                   // base-type coupling 0.15×, external-dep 0.5×,
                                                   // namespace-cluster 0.85×, self-assessment 0.1×
```

**Returns**: Array of `TestableTarget` objects with class, score, reason, uncoveredMethods, ctorParams, and all cross-joined metadata.

**When to use**: **First tool call of every coverage session.** Replaces 4+ manual tool calls and manual target selection reasoning.

---

### get_source_snippet

**Purpose**: Serve actual C# source code from the target repo. Eliminates "I have to read_file anyway."

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class name to get source for |
| `methodName` | string | no | Specific method to extract (returns just that method) |
| `maxLines` | int | no | Max lines to return (default: 200) |

**Resolution**: className → `coverage-gaps.jsonl` (file path) → source root + relative path → read file.

**Requires**: `TOTAL_RECALL_SOURCE_ROOT` env var or scanner `--source-root` (persisted to `config.json`).

**Returns**: JSON with `filePath`, `startLine`, `endLine`, `source` (string), `totalLines`.

**When to use**: After picking a target — read the implementation before writing tests. Replaces `read_file` calls to the target repo.

---

### generate_test_scaffold

**Purpose**: Complete C# test class skeleton combining type metadata + mock recipes + coverage gaps + gotchas.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class to generate test scaffold for |

**Generates**:
- All required `using` statements (type namespace + dependencies + Moq + Xunit)
- `Mock<T>` field declarations for every interface constructor parameter
- Default values for concrete params (`string` → `""`, `int` → `0`, etc.)
- Test class with constructor that wires mocks → SUT
- One `[Fact]` method stub per uncovered method
- `// ⚠️ GOTCHA:` comments for known pitfalls
- Mock recipe `Setup()` calls in constructor

**Returns**: Complete `.cs` file content as a string.

**When to use**: After `get_testable_targets` + `get_source_snippet` — scaffold the test file, then fill in assertions.

---

### log_session

**Purpose**: Write session outcomes for cross-session learning. Bidirectional — the agent writes data *back* to Total.Recall.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `model` | string | yes | — | LLM model used (e.g., "claude-sonnet-4-20250514") |
| `promptTokens` | long | no | 0 | Approximate prompt tokens consumed |
| `completionTokens` | long | no | 0 | Approximate completion tokens consumed |
| `classesAttempted` | string | no | "" | Comma-separated class names attempted |
| `classesSucceeded` | string | no | "" | Comma-separated class names that compiled + passed |
| `classesFailed` | string | no | "" | Comma-separated "ClassName:reason" pairs |
| `testsGenerated` | int | no | 0 | Total test methods generated |
| `coverageBefore` | double | no | 0 | Line coverage % before session |
| `coverageAfter` | double | no | 0 | Line coverage % after session |
| `gotchasDiscovered` | int | no | 0 | New gotchas added this session |
| `assessmentsRecorded` | int | no | 0 | New assessments added this session |
| `notes` | string | no | null | Free-form session notes |

**Returns**: Confirmation + session ID.

**When to use**: **End of every coverage session.** Captures what happened for future analytics.

---

### get_sessions

**Purpose**: Session history + aggregate analytics.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `last` | int | no | 5 | Number of recent sessions to return |

**Returns**: JSON with recent sessions + aggregates: total tests generated, total coverage delta, average tokens per test, success rate, top patterns.

**When to use**: Start of a session to see what worked previously, or for ROI measurement.

---

## v1 Tools (Lookup Index)

### resolve_type

**Purpose**: Look up any .NET type from the scanned assembly by name.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Exact or partial type name |

**Search strategy** (in order):
1. Exact name match (case-sensitive) — **O(1) dictionary lookup**
2. Case-insensitive exact match — **O(1) dictionary lookup**
3. Partial match (contains, case-insensitive) — linear scan (fallback only)
4. Interface name match (searches interface lists) — linear scan (fallback only)
5. Namespace search — linear scan (fallback only)

**Returns**: Up to 5 matching `TypeRecord` objects as JSON.

**Example input**: `"OrderService"`

**Example output** (abbreviated):
```json
[
  {
    "name": "OrderService",
    "namespace": "MyApp.Services.Orders",
    "fullUsing": "using MyApp.Services.Orders;",
    "constructors": [
      { "params": [] },
      { "params": ["IOrderRepository repo", "ILogger<OrderService> logger", "IEventBus bus"] }
    ],
    "baseType": "ServiceBase",
    "interfaces": ["IOrderService"],
    "isAbstract": false,
    "isStatic": false,
    "isInternal": false,
    "isInterface": false,
    "isEnum": false,
    "properties": [
      { "name": "TotalProcessed", "clrType": "int", "hasSet": true, "hasInit": false },
      { "name": "LastOrderDate", "clrType": "DateTime?", "hasSet": true, "hasInit": false }
    ],
    "enumValues": null
  }
]
```

**When to use**: Every time you need a namespace, constructor signature, or property list for a type you're about to use in test code.

---

## get_mock_recipe

**Purpose**: Get pre-built Moq setup code for an interface.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `interfaceName` | string | yes | Interface name (with or without `I` prefix) |

**Returns**: `MockRecipe` objects with C# code, required usings, and known gotchas.

**Example input**: `"IOrderRepository"`

**Example output** (abbreviated):
```json
[
  {
    "interface": "IOrderRepository",
    "namespace": "MyApp.Data.Interfaces",
    "requiredUsings": [
      "using MyApp.Data.Interfaces;",
      "using MyApp.Models;",
      "using Moq;"
    ],
    "recipe": "var mockRepo = new Mock<IOrderRepository>();\nmockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(new Order());\n...",
    "gotchas": [
      "GetByIdAsync returns Task<Order?> — null check needed in assertions",
      "SaveAsync throws on duplicate OrderNumber — setup accordingly"
    ],
    "usedByClasses": ["OrderService", "OrderController", "OrderImporter"]
  }
]
```

**When to use**: Before writing any test that needs to mock an interface. Saves 3-5 tool calls of discovery.

---

## get_coverage_gaps

**Purpose**: Ranked list of classes with the most uncovered code.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 20 | Max results to return |
| `skipUntestable` | bool | no | true | Filter out classes with a skip reason |
| `sortBy` | string | no | `"roi"` | Sort order: `"roi"` (default, ROI score), `"uncovered"` (uncovered lines desc), `"coverage"` (coverage % asc) |

**ROI formula**: `uncoveredLines * testabilityMultiplier / (1 + existingTestCount)`. Higher = more value from writing tests.

**Returns**: Array of `CoverageGap` objects with `roiScore`, sorted by chosen order.

**Example input**: `top=5, skipUntestable=true`

**Example output** (abbreviated):
```json
[
  {
    "class": "PaymentGateway",
    "namespace": "MyApp.Services.Payments",
    "file": "Services/Payments/PaymentGateway.cs",
    "totalLines": 2588,
    "coveredLines": 0,
    "uncoveredLines": 2588,
    "coveragePercent": 0.0,
    "uncoveredMethods": [
      { "name": "ProcessPayment", "startLine": 45, "endLine": 120, "uncoveredLines": 75 }
    ],
    "existingTestCount": 0,
    "testability": "unknown",
    "skipReason": null
  }
]
```

**When to use**: Start of each test generation session to pick the highest-ROI target.

---

## get_gotchas

**Purpose**: Get all known pitfalls for a type before writing tests.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type name to look up |

**Returns**: Array of `Gotcha` objects with category, description, and discovery context.

**Example input**: `"ContentRange"`

**Example output**:
```json
[
  {
    "type": "ContentRange",
    "category": "constructor",
    "gotcha": "Parameterless ctor leaves StartLine/EndLine null — copy ctor NREs. Initialize with explicit values.",
    "discoveredInGen": 12,
    "date": "2026-02-28"
  },
  {
    "type": "ContentRange",
    "category": "equality",
    "gotcha": "partial record — auto-generates == value equality. Asserting reference inequality fails.",
    "discoveredInGen": 5,
    "date": "2026-02-24"
  }
]
```

**Categories**: `constructor`, `namespace`, `enum`, `equality`, `mock`, `unreachable`, `property`, `inheritance`, `bug`, `static`

**When to use**: Before writing ANY tests for a type. Check gotchas first to avoid known traps.

---

## add_gotcha

**Purpose**: Record a new pitfall discovered during test generation.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type the gotcha applies to |
| `category` | string | yes | One of: constructor, namespace, enum, equality, mock, unreachable, property, inheritance, bug, static |
| `gotcha` | string | yes | Description of the pitfall |

**Returns**: Confirmation message.

**Example input**: `typeName="PaymentGateway", category="constructor", gotcha="4-string ctor with empty filename throws ArgumentException from GetFileType"`

**When to use**: After discovering a new trap during test generation. Persists across sessions.

---

## get_test_inventory

**Purpose**: Check what tests already exist for a class.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class name to look up |

**Returns**: `TestInventoryEntry` with test files, method names, counts, and inferred coverage.

**Example input**: `"OrderService"`

**Example output** (abbreviated):
```json
[
  {
    "class": "OrderService",
    "testFiles": ["OrderServiceTests.cs"],
    "testMethods": [
      "ProcessOrder_ValidInput_ReturnsSuccess",
      "ProcessOrder_NullOrder_ThrowsArgumentException",
      "GetPending_ReturnsOnlyPendingOrders",
      "Cancel_AlreadyShipped_ThrowsInvalidOperation"
    ],
    "testCount": 55,
    "inferredCoveredMethods": [
      "ProcessOrder", "GetPending", "Cancel"
    ]
  }
]
```

**When to use**: Before writing new tests — avoid duplicating existing coverage.

---

## get_context

**Purpose**: Combined query — returns type metadata, gotchas, test inventory, and mock recipes in a single call.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type name to look up |

**Returns**: JSON object with `type` (TypeRecord), `gotchas` (Gotcha[]), `testInventory` (TestInventoryEntry), `mockRecipes` (MockRecipe[]), and `assessments` (Assessment[]).

**When to use**: When you need the full picture for a type before writing tests. Saves 4-5 individual tool calls.

---

## add_assessment

**Purpose**: Record a testability verdict for a class.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class being assessed |
| `verdict` | string | yes | One of: `testable`, `skip`, `coupled`, `deferred` |
| `reasoning` | string | yes | Why this verdict was given |
| `deps` | string | no | Comma-separated key dependencies |
| `cluster` | string | no | Related class cluster name |

**Returns**: Confirmation message.

**When to use**: After evaluating a class — persist the verdict so future sessions (and `get_testable_targets`) can skip already-assessed classes.

---

## get_assessments

**Purpose**: Retrieve previous testability assessments.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | no | Filter by class name (returns all if omitted) |
| `verdict` | string | no | Filter by verdict |

**Returns**: Array of Assessment objects. Deduplicates by class name (last assessment wins).

**When to use**: Check if a class was already assessed before spending time evaluating it.

---

## get_metrics

**Purpose**: Server telemetry — tool call counts, cache hit/miss rates, type index stats.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| (none) | — | — | — |

**Returns**: JSON object with all counter values.

**When to use**: Debugging server performance or verifying tool usage.

---

## v2 Extended Tools (Late-Stage Targeting)

### get_uncovered_methods

**Purpose**: Method-level ROI targets. Flattens class-level coverage gaps into individual method targets. Use when class-level targeting is exhausted or when extending existing test files for maximum ROI.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 20 | Max results to return |
| `minUncoveredLines` | int | no | 3 | Minimum uncovered lines per method (filters trivial one-liners) |
| `onlyWithExistingTests` | bool | no | false | Only show methods in classes that already have test files. Set `true` for "extend existing" strategy. |
| `excludeBoilerplate` | bool | no | true | Exclude `.ctor`, `.cctor`, `get_*`, `set_*` methods |

**Scoring**: `10 × log₂(1 + uncoveredLines) × (hasTestFile ? 2.0 : 0.5) × 1/(1 + existingTestCount × 0.05)`. Methods in classes with existing test files score 2× higher — extending is cheaper than creating.

**Returns**: JSON with `count`, `filters`, `stats` (methodsWithTestFile, methodsWithoutTestFile, avgUncoveredLines, distinctClasses), and `methods` array. Each entry: class, method, uncoveredLines, startLine, endLine, hasTestFile, testFiles, score, reason.

**When to use**: After `get_testable_targets` stops returning high-scoring classes. Pair with `generate_test_scaffold(className, methodNames: "...")` to generate stubs for specific methods.

---

### get_stub_classes

**Purpose**: Find zero-or-near-zero coverage classes that are trivially testable — POCOs, stubs, static helpers, and simple logic with no mocking complexity. These are the highest-ROI targets when all complex classes are exhausted.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 20 | Max results to return |
| `maxCoveragePercent` | double | no | 5.0 | Max coverage % to include (0 = only completely uncovered) |
| `maxCtorParams` | int | no | 2 | Max constructor params (stubs should have trivial constructors) |
| `includeWithTests` | bool | no | false | Include classes with existing tests |

**Categories**: `poco` (all boilerplate), `static-helpers` (static class), `simple-logic` (1–5 real methods), `logic-heavy` (6+ real methods), `unclassified`.

**Scoring**: Log-scaled base × ctor simplicity (parameterless=1.0×, mockable=0.8×ⁿ, concrete=0.6×ⁿ) × hasTestFile 1.5× × realMethods≥3 1.3× × size sweet spot (≤50=1.2×) × diminishing returns `1/(1+existingTestCount)`.

**Returns**: JSON with `count`, `filters`, `stats`, and `classes` array. Each entry includes `category`, `realMethodCount`, `boilerplateMethodCount`, `minCtorParams`, `allParamsMockable`, `score`, `reason`.

**When to use**: When `get_testable_targets` scores are all <5. Stub classes were the highest-ROI targets in late-stage coverage sessions.

---

## Static Analysis Tools

### get_class_metrics

**Purpose**: Static analysis metrics for a class — coupling (afferent/efferent), instability, archetype, dependency lists, and cluster membership. Understand a class's position in the dependency graph before writing tests.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class name to look up |

**Returns**: Markdown-formatted report with namespace, archetype, cluster membership, coupling metrics table (Ca, Ce, instability, ctor params, properties, interfaces, inheritance depth, total lines), and dependency lists (depends on / depended on by). Fuzzy-matches class name if exact match not found.

**When to use**: Before writing tests for a class you're unfamiliar with — understand its coupling before investing time.

---

### get_dependency_graph

**Purpose**: Dependency graph neighborhood for a class — direct dependencies, direct dependents, and a Mermaid diagram of the local subgraph.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `className` | string | yes | — | Class name to center the graph on |
| `depth` | int | no | 1 | Graph depth: 1 = direct deps, 2 = include transitive (max: 3) |

**Returns**: Markdown with dependency list (outgoing), consumer list (incoming), and a Mermaid `flowchart LR` diagram. Edge types are styled: `ctor-interface` (dashed, inject), `ctor-concrete` (solid), `base-type` (thick, inherits), `implements` (dashed). Center node highlighted. Max 30 nodes.

**When to use**: Visualize coupling before deciding whether to test a class. High fan-in classes may be coupled; high fan-out classes need many mocks.

---

### get_analysis_summary

**Purpose**: Architectural overview of the entire scanned assembly — hot interfaces, most coupled classes, dependency clusters, and isolated classes.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| (none) | — | — | — |

**Returns**: Markdown summary with metrics table (total types, edges, clusters, isolated classes), hot interfaces table (interface name + consumer count), most coupled classes, cluster breakdown, and archetype distribution.

**Requires**: `--enrich` flag during scan.

**When to use**: Start of a session for architectural awareness — identify clusters of coupled classes to avoid, and isolated classes that are easy wins.

---

## Observability & Learning Tools

### learn_test_patterns

**Purpose**: Analyze existing test files to learn project-level conventions — naming patterns, assertion styles, mock strategies, helper methods, common usings. Results inform `generate_test_scaffold` output.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `maxFiles` | int | no | 20 | Maximum test files to analyze |

**Returns**: JSON with `patterns` (assertionStyle, namingPattern, usesConstructorSetup, usesDisposable, mockPattern, avgTestsPerClass, commonUsings, helperMethods) and `summary` (filesAnalyzed, totalTestMethods, namingBreakdown).

**Detected patterns**: Assertion styles (xUnit.Assert, FluentAssertions, NUnit.Assert, MSTest.Assert). Naming (MethodName_Scenario_Expected, ShouldVerb_WhenCondition, GivenX_WhenY_ThenZ). Mock patterns (field-level `Mock<T>` vs local). Helper methods reported when found in 2+ files.

**When to use**: Once per namespace, before generating scaffolds. Ensures generated test code matches existing project conventions.

---

### get_gotcha_insights

**Purpose**: Analyze gotchas across all types to find recurring patterns and clusters. Generates paste-ready AGENTS.md "Footguns" documentation sections.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `minClusterSize` | int | no | 2 | Minimum gotchas per cluster to report |
| `generateFootguns` | bool | no | true | Generate AGENTS.md Footguns markdown section |

**Returns**: JSON with `totalGotchas`, `clusters` (each with title, count, affectedTypes, canonicalFix, instances), `categoryDistribution`, `hotTypes` (types with 2+ gotchas), `unclusteredGotchas`, and `footgunsMarkdown`.

**Cluster patterns**: Moq expression tree limitations, enum gotchas, constructor traps, mock setup complexity, namespace resolution, record semantics, property accessor quirks, dead code, ICU/culture issues, static state.

**When to use**: After accumulating 10+ gotchas — identify systemic issues worth documenting in AGENTS.md.

---

### refresh_coverage

**Purpose**: Re-parse a Cobertura XML coverage report and update coverage-gaps.jsonl mid-session — much faster than a full scanner run. Also auto-discovers the newest Cobertura XML in TestResults/ if the configured path is stale.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `coveragePath` | string | no | from config.json | Path to new Cobertura XML file |

**Returns**: JSON with before/after comparison: `lineRateChange`, `newLinesHit`, `newlyCovered` (classes that went from <1% to ≥1%), `topImprovements` (top 5 classes by coverage delta).

**When to use**: After running `dotnet test --collect:"XPlat Code Coverage"` mid-session — refresh coverage data without restarting the server or running the full scanner. If using `--watch` mode, coverage data updates automatically when new XML files appear.

---

## Telemetry & Eval Tools (Cuts 1–6)

Every public MCP tool is wrapped in `Telemetry.Track`, which appends a `ToolCallRecord` to `tool-calls.jsonl` (toolName, ns, sessionId, taskId, params summary, latency, response bytes) whenever `TOTAL_RECALL_MODE != "off"`. The detector also runs `CycleDetector.Observe` after each call to spot behaviour anti-patterns.

**Modes** (`TOTAL_RECALL_MODE` env var):
- `off` — zero-overhead pass-through, no recording
- `passive` (default) — record every tool call + cycles
- `active-eval` — passive + agent can request challenges

### start_task

**Purpose**: Begin a bracketed task. Sets `Telemetry.ActiveTaskId` so subsequent tool calls are attributed to this task. Calling `start_task` again before `end_task` auto-abandons the prior task (writes outcome `abandon` with note `"superseded by new start_task"`).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `intent` | string | yes | Narrative description of what the agent is about to do |

**Returns**: `{ taskId, startedAt }` JSON.

**When to use**: At the beginning of any non-trivial multi-step operation. Pair with `end_task` to get duration + outcome attribution in `tasks.jsonl`.

---

### end_task

**Purpose**: End the active task. Persists `(taskId, intent, outcome, startedAt, endedAt, durationMs, notes)` to `tasks.jsonl`.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `outcome` | string | yes | `success` or `abandon` |
| `notes` | string | no | Free-text notes describing what happened |

**Returns**: Confirmation + duration.

---

### log_task

**Purpose**: One-shot convenience: start + immediately end a task. Use when the operation is atomic and you don't need bracketing.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `intent` | string | yes | What was attempted |
| `outcome` | string | yes | `success` or `abandon` |
| `notes` | string | no | Free-text notes |

---

### get_cycles

**Purpose**: Return recent detected behaviour cycles from `cycles.jsonl` for self-diagnosis.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `last` | int | no | 20 | Most recent N cycles to return |
| `pattern` | string | no | (all) | Filter by pattern: `re-query`, `context-loss`, `oscillation` |

**Returns**: Cycle[] JSON. Each cycle: pattern, sessionId, dedupeKey, detectedAt, evidence (tool calls involved).

**Detected patterns**:
- **re-query** — ≥3 identical `(toolName, paramHash)` calls within session
- **context-loss** — ≥2 lookup calls (`resolve_type`/`get_context`/`get_source_snippet`) with no intervening write
- **oscillation** — ≥3 distinct `get_source_snippet` targets in a 5-call window with no `add_assessment` between

Each cycle fires once per session (deduplicated via `s_fired` HashSet).

**When to use**: When you suspect you're thrashing — call to confirm and adjust strategy.

---

### get_tool_call_stats

**Purpose**: Per-tool call counts, p50/p95 latency, and average response bytes computed from `tool-calls.jsonl`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `last` | int | no | 500 | Most recent N tool calls to aggregate |

**Returns**: Per-tool histogram JSON.

---

### get_efficiency_report

**Purpose**: Sessions × cycles × tasks summary: tokens-per-task, redundant-call rate, plateau warnings.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `last` | int | no | 10 | Most recent N sessions to aggregate |

**Returns**: JSON with totals and ratios across sessions, tasks, and cycles.

---

### get_model_scorecard

**Purpose**: Per-model aggregated metrics from sessions + tasks + evals — cross-model comparison.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| (none) | — | — | — |

**Returns**: Per-model rows: sessions, tasks, successRate, avgLinesPerTest, evalsPassed, evalsFailed, avgEvalScore.

---

### get_next_challenge

**Purpose**: Pull a graded eval challenge problem from `challenges.jsonl` for the agent to attempt. Available only when `TOTAL_RECALL_MODE=active-eval`.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `difficulty` | string | no | (any) | Filter by difficulty: `easy`, `medium`, `hard` |

**Returns**: Challenge JSON: `challengeId`, `prompt`, `requiredTools`, `toolBudget`, `expectedKeyPhrases`.

---

### submit_challenge

**Purpose**: Submit a challenge attempt. Graded deterministically by `ChallengeGrader` and persisted to `evals.jsonl`.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `challengeId` | string | yes | The challenge being answered |
| `response` | string | yes | The agent's final answer |
| `toolsUsed` | string | yes | Comma-separated list of tool names called during the attempt |

**Returns**: `EvalResult` JSON: `passed` (score ≥ 0.7), `score`, `breakdown` (`requiredToolsCalled`, `stayedUnderBudget`, `outputCorrectness`), `feedback`.

**Rubric**: 0.4 × fraction of required tools called + 0.2 × stayed under budget + 0.4 × output correctness (key-phrase substring match). Pure deterministic — no model calls, reproducible.

---

### get_eval_leaderboard

**Purpose**: Aggregated eval pass/fail rates across models from `evals.jsonl`.

**Returns**: Model rankings JSON.

---

### report_context_reset

**Purpose**: Agent self-reports a compaction or context-window reset. Records a marker entry to `sessions.jsonl`, rotates `Telemetry.SessionId` (so post-reset behaviour is attributed correctly for cycle detection and scorecards), and clears `Telemetry.ActiveTaskId`. No-op when `TOTAL_RECALL_MODE=off`.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `reason` | string | no | Free-text reason for the reset (e.g. `"context window exceeded"`, `"manual compaction"`) |

**Returns**: Confirmation.

**When to use**: Immediately after a context window reset / summary so subsequent cycle detection doesn't compare against the pre-reset session.

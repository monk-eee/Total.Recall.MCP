# AGENTS.MD — Total.Recall

## Repo Identity

| Key | Value |
|-----|-------|
| Solution | `Total.Recall.sln` |
| Version | 2.2.0 |
| SDK | 8.0.400 (`rollForward: latestFeature`, actual: 8.0.418) |
| dotnet | `C:\Program Files\dotnet\dotnet.exe` |
| sourceRoot | `src/Total.Recall/` |
| testRoot | `tests/Total.Recall.Tests/` |
| targetFramework | net8.0 |
| transport | stdio (VS Code spawns process) |
| dataFormat | JSONL (one JSON object per line) |
| tests | 992 passing |

## Purpose

**Total.Recall is a persistent memory server for AI agents doing .NET code coverage work.**

When an AI agent writes tests for a large .NET codebase, it burns 60–70% of its context re-discovering the same information every session: type constructors, mock patterns, coverage gaps, known pitfalls, and what's already tested. Over 30 sessions to reach a coverage target, this wastes ~450K tokens and ~22 hours of wall-clock time.

Total.Recall eliminates this waste by converting ephemeral agent knowledge into durable, queryable data. Scanners extract type metadata, coverage gaps, and test inventories into JSONL files. An MCP server exposes 23 tools that let agents query this data instantly — one tool call replaces 10–15 file reads. Agents also write data back (gotchas, assessments, session logs), creating a feedback loop that makes each session smarter than the last.

## Design Principles

These principles guide every implementation decision. When in doubt, choose the option that best satisfies them:

1. **Simplicity over cleverness** — JSONL files, no databases, no complex joins. Every tool is a single query against in-memory data. The entire data set is <2MB and loads in <1s. If a feature requires a query planner, it's too complex.

2. **Read-heavy, append-only** — Most operations are reads from pre-warmed in-memory caches. Writes are always appends to JSONL files (gotchas, assessments, sessions). No updates, no deletes. This makes the data git-friendly, grep-friendly, and corruption-resistant.

3. **Zero-config for agents** — Tools are auto-discovered via MCP protocol. Rich `[Description]` attributes on each tool tell the agent what it does, what parameters it takes, and when to use it. Agents don't need to be taught about Total.Recall — they find it.

4. **Graceful degradation** — If Total.Recall isn't running (or data is missing), agents fall back to standard file-reading workflows. No tool should be a hard dependency. MCP guidance lives in the consuming repo's AGENTS.md and copilot-instructions.md, not in skills.

5. **Namespace isolation** — Multiple repos share one Total.Recall server with different namespace subdirectories. Data never cross-contaminates. The `ns` parameter on every tool allows explicit targeting.

6. **Performance by default** — In-memory `StoreRegistry` singletons with file-change detection. Pre-built `Dictionary<string, TypeRecord>` for O(1) type lookups. `SharedJsonOptions` (3 static instances) for serialization cache reuse. Startup pre-warm so the first tool call hits memory, not disk.

7. **Three-layer observability** — Logs (stderr, configurable level, gone on restart), Metrics (in-memory counters, tool call and cache stats, gone on restart), Sessions (persistent JSONL, cross-session learning).

## Build & Run Commands

```bash
# Build
dotnet build src/Total.Recall/Total.Recall.csproj

# Run as MCP server (stdio mode — VS Code launches this)
dotnet run --project src/Total.Recall/Total.Recall.csproj

# Run scanner CLI (with --output)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan \
  --assembly "C:\path\to\YourProject.dll" \
  --coverage "C:\path\to\coverage.cobertura.xml" \
  --tests "C:\path\to\YourProject.Tests" \
  --output "C:\path\to\Total.Recall\data\myproject"

# Run scanner CLI (with --namespace)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan \
  --assembly "...\YourProject.dll" \
  --coverage "...\coverage.cobertura.xml" \
  --tests "...\YourProject.Tests" \
  --namespace myproject

# Watch mode: auto-rescan on file changes (Ctrl+C to stop)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan \
  --assembly "...\YourProject.dll" \
  --coverage "...\coverage.cobertura.xml" \
  --tests "...\YourProject.Tests" \
  --namespace myproject \
  --enrich --analyze --watch

# Run tests (when test project exists)
dotnet test tests/Total.Recall.Tests/Total.Recall.Tests.csproj
```

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| ModelContextProtocol | 0.3.0-preview.1 | C# MCP SDK — stdio server, tool registration |
| Microsoft.Extensions.Hosting | 8.0.1 | Generic Host for DI + lifecycle |
| System.Reflection.MetadataLoadContext | 8.0.1 | Safe reflection-only assembly loading |

## Assembly Inventory

### Source Assembly

| Assembly | TFM | csproj Path |
|----------|-----|-------------|
| Total.Recall | net8.0 | `src/Total.Recall/Total.Recall.csproj` |

### Test Assembly

| Assembly | TFM | csproj Path | Status |
|----------|-----|-------------|--------|
| Total.Recall.Tests | net8.0 | `tests/Total.Recall.Tests/Total.Recall.Tests.csproj` | 992 tests |

## Data Files

All located under `$TOTAL_RECALL_DATA/{namespace}/`:

| File | Generated By | Description |
|------|-------------|-------------|
| `type-registry.jsonl` | AssemblyScanner | Every public/internal type in the target assembly |
| `mock-recipes.jsonl` | Manual curation | Pre-built Moq setup code per interface |
| `coverage-gaps.jsonl` | CoberturaParser | Uncovered lines/methods per class |
| `gotchas.jsonl` | Seeded + `add_gotcha` tool | Type-specific traps and workarounds (append-only) |
| `test-inventory.jsonl` | TestProjectScanner | Existing test methods per class |
| `assessments.jsonl` | `add_assessment` tool | Testability verdicts from agent analysis (append-only) |
| `sessions.jsonl` | `log_session` tool | Session outcomes for cross-session learning (append-only) |
| `config.json` | Scanner `--source-root` | Per-namespace scan config (source root, paths, timestamp) |

## MCP Tools

23 tools. All accept an optional `ns` (namespace) parameter to target a specific dataset.

### v2 Tools (Decision Engine)

| Tool Name | Input | Output | Purpose |
|-----------|-------|--------|---------|
| `get_testable_targets` | top, maxCtorParams, maxTotalLines, excludeAbstract, excludeAssessed, requireZeroTests, ns? | TestableTarget[] JSON (includes hasTestFile, testFiles) | Pre-scored, pre-filtered target list. Cross-joins 6 data sources. 1.5x bias for classes with existing test files. **First call of every session.** |
| `get_source_snippet` | className, methodName?, maxLines?, ns? | Source code JSON | Actual C# source from target repo. Requires `TOTAL_RECALL_SOURCE_ROOT` or `config.json`. |
| `generate_test_scaffold` | className, methodNames?, ns? | Complete .cs file string or incremental stubs JSON | Full test class skeleton (default) or incremental [Fact] stubs for specific methods when `methodNames` is provided. |
| `log_session` | model, promptTokens, completionTokens, classesAttempted, classesSucceeded, classesFailed, testsGenerated, coverageBefore, coverageAfter, gotchasDiscovered, assessmentsRecorded, coveredLines?, notes?, ns? | Confirmation + session ID | Write session outcomes. **Last call of every session.** |
| `get_sessions` | last?, ns? | Sessions + aggregates + plateauWarning JSON | Session history, success rates, token efficiency, coverage deltas, lines/test ROI, plateau detection. |
| `get_uncovered_methods` | top?, minUncoveredLines?, onlyWithExistingTests?, excludeBoilerplate?, ns? | UncoveredMethodTarget[] JSON | Method-level coverage targeting. Flattens per-class gaps into per-method targets, scored by ROI + hasTestFile bias. **Use when class-level targeting is exhausted.** |
| `get_stub_classes` | top?, maxCoveragePercent?, maxCtorParams?, includeWithTests?, ns? | StubClassTarget[] JSON | Zero/near-zero coverage trivially-testable classes: POCOs, stubs, static helpers, simple logic. Categorized and scored by simplicity. **Use when testable_targets scores are all <5.** |

### v1 Tools (Lookup Index)

| Tool Name | Input | Output | Purpose |
|-----------|-------|--------|---------|
| `resolve_type` | typeName, namespacePart?, filePath?, ns? | TypeRecord JSON | Namespace + constructor + property lookup. O(1) exact, fallback to contains/interface/namespace. |
| `get_context` | typeName, ns? | Combined JSON | Type + gotchas + tests + mocks + assessments + coverage gap + session history in one call. |
| `get_mock_recipe` | interfaceName, ns? | MockRecipe JSON | Pre-built Moq setup code for an interface. |
| `get_coverage_gaps` | top?, skipUntestable?, sortBy?, ns? | CoverageGap[] JSON | ROI-ranked uncovered classes + methods. |
| `get_gotchas` | typeName, ns? | Gotcha[] JSON | Known pitfalls for a type. |
| `add_gotcha` | typeName, category, gotcha, ns? | Confirmation | Append a new pitfall to gotchas.jsonl. |
| `get_test_inventory` | className, ns? | TestInventoryEntry JSON | Existing test methods per class. |
| `add_assessment` | className, verdict, reasoning, deps?, cluster?, ns? | Confirmation | Record testability verdict. Verdicts: `testable`, `coupled`, `skip`, `deferred`. |
| `get_assessments` | className?, verdict?, ns? | Assessment[] JSON | Previous testability assessments. Last verdict per class wins. |
| `get_metrics` | (none) | Telemetry JSON | Tool call counts, cache hit rates, type index stats, uptime. |

### Static Analysis Tools

| Tool Name | Input | Output | Purpose |
|-----------|-------|--------|---------||
| `get_class_metrics` | className, ns? | Markdown report | Coupling (Ca/Ce), instability, archetype, cluster, dependency lists for a class. |
| `get_dependency_graph` | className, depth?, ns? | Markdown + Mermaid | Local subgraph: deps, consumers, Mermaid diagram. Max depth 3, max 30 nodes. |
| `get_analysis_summary` | ns? | Markdown report | Architectural overview: hot interfaces, most coupled classes, clusters, archetype distribution. Requires `--enrich`. |

### Observability & Learning Tools

| Tool Name | Input | Output | Purpose |
|-----------|-------|--------|---------||
| `learn_test_patterns` | maxFiles?, ns? | TestPatterns JSON | Analyze existing test files for naming, assertion, mock, and helper conventions. Results inform `generate_test_scaffold`. |
| `get_gotcha_insights` | minClusterSize?, generateFootguns?, ns? | Insights JSON + Footguns markdown | Cluster gotchas by pattern (10 well-known patterns), identify systemic issues, generate AGENTS.md Footguns section. |
| `refresh_coverage` | coveragePath?, ns? | Before/after comparison JSON | Re-parse Cobertura XML mid-session. Auto-discovers newest XML in TestResults/ if configured path is stale. |

## Scanners

| Scanner | Input | Output | Key Library |
|---------|-------|--------|-------------|
| AssemblyScanner | .dll path | type-registry.jsonl | MetadataLoadContext |
| CoberturaParser | coverage.cobertura.xml | coverage-gaps.jsonl | System.Xml.Linq |
| TestProjectScanner | test project directory | test-inventory.jsonl | Regex on .cs files |
| ScannerWatcher | `--watch` flag | Re-runs scanners on file changes | FileSystemWatcher |

## Architecture Decisions

1. **JSONL over SQLite**: JSONL is grep-friendly, git-friendly, append-friendly. The data volumes (~1,176 types, ~539 classes, ~70 gotchas) are trivially small — full load into memory is <2MB.

2. **MetadataLoadContext over Assembly.LoadFrom**: Target assemblies may have heavy dependency graphs. MetadataLoadContext does reflection-only — no DLL loading, no dependency resolution failures.

3. **Dual-mode entry point**: Single executable serves as both MCP server (default) and CLI scanner (`scan` subcommand). Avoids maintaining two projects.

4. **stdio transport only**: No HTTP, no SSE. VS Code spawns the process directly. Simplest possible deployment.

5. **System.Text.Json**: All serialization uses `SharedJsonOptions` (3 static instances: CamelCase, CamelCaseIndented, Indented). STJ caches reflection metadata inside options objects, so reusing gives ~3x speedup.

6. **StoreRegistry namespace-keyed pattern**: `StoreRegistry.ForNamespace(ns)` returns a `NamespaceStores` instance holding all 7 `JsonLineStore<T>` singletons (TypeRegistry, CoverageGaps, TestInventory, Gotchas, MockRecipes, Assessments, Sessions) + type index for that namespace. Stores are cached by resolved data directory path. Default namespace shortcuts (`StoreRegistry.TypeRegistry`, etc.) delegate to `ForNamespace(null)`. Startup pre-warms the default namespace.

7. **O(1) type name index**: `NamespaceStores.GetTypeIndex()` builds `Dictionary<string, TypeRecord>` (exact + case-insensitive) per namespace on first call. Eliminates 5 sequential O(n) linear scans per `resolve_type` / `get_context` call.

8. **Namespace resolution**: `TOTAL_RECALL_DATA` = root dir. `TOTAL_RECALL_NAMESPACE` = default namespace subdirectory. If namespace env not set and no .jsonl files in root, uses root directly (single-namespace backward compat). If .jsonl files exist in root, legacy mode (root = data dir). Explicit `--namespace` flag creates `{root}/{name}/` subdirectory.

9. **In-memory telemetry**: `Metrics` static class tracks tool calls, cache hits/misses, type index hits/rebuilds, and lookup strategy distribution via `ConcurrentDictionary<string, long>`. Zero-allocation `Interlocked.Add` increments. Resets on server restart (no persistence).

10. **Assessment deduplication**: `assessments.jsonl` is append-only. `get_assessments` deduplicates by class name — last assessment wins (Dictionary keyed by class, iterate in order).

11. **Configurable logging**: `Log` static class writes to stderr (never stdout — would corrupt JSON-RPC). Level set via `TOTAL_RECALL_LOG_LEVEL` env var (`debug|info|warn|error|quiet`, default: `info`). `Debug` level logs every tool invocation with parameters, record counts, lookup strategies, cache hits/misses, and store loads. Use `debug` when troubleshooting, `quiet` for clean CI runs.

12. **Mockability-aware scoring**: `get_testable_targets` scores by constructor param mockability, not just count. All-interface ctors get full bonus regardless of param count. Each concrete (non-interface) param applies a 0.3x penalty (`Math.Pow(0.3, n)`). Concrete params that are skip/coupled in assessments get an additional 0.15x penalty (`Math.Pow(0.15, n)`). This prevents classes with unmockable concrete dependencies (e.g., `LinterExtension`) from scoring high.

13. **SourceSnippet name collision resolution**: When multiple classes share a name (e.g., `ZonePivot` as both a POCO and a ContentBlocks implementation), `get_source_snippet` prefers the class with the most uncovered lines, then most total lines. Includes an `ambiguityNote` field in the response when disambiguation occurred.

14. **LogSession input validation**: `log_session` detects when agents pass integer counts (e.g., `classesAttempted: "3"`) instead of comma-separated class names. Returns a warning in the response without blocking the write. Also handles JSON array input (`["ClassA", "ClassB"]`) — `ParseCsv` and `ParseFailures` detect JSON arrays (starts with `[`) and deserialize before falling back to CSV splitting.

15. **Stub/empty-body detection**: `get_testable_targets` skips classes with 0 uncovered methods (auto-property boilerplate only) and classes where ALL uncovered methods are boilerplate (`IsBoilerplateMethod`: `.ctor`, `.cctor`, `get_*`, `set_*`). This prevents POCOs, data transfer objects, and constructor-only classes from appearing as high-scoring targets.

16. **Nested class name normalization**: Coverage gaps from Cobertura use `Parent/Nested` for nested classes. All cross-reference lookups (assessments, test inventory, gotchas, sessions, type registry) try the full name first, then the bare name after the last `/`. This ensures `ForegroundThreadManager/ForegroundTaskScheduler` matches assessments recorded as `ForegroundTaskScheduler`.

17. **Fuzzy test inventory matching**: When exact class name match fails against test inventory, tries: (1) stripping common suffixes (`Base`, `Impl`, `Default`), (2) prefix matching in either direction. Prevents false "no tests" reports for classes like `WriteOperationConfigurationBase` when tests exist for `WriteOperationConfiguration`.

18. **Namespace-qualified deduplication**: `CoberturaParser` deduplicates by `{Namespace}.{Class}` instead of just `Class`. This prevents classes with the same short name in different namespaces (e.g., `ZonePivot` in Models vs ContentBlocks) from being incorrectly merged. Partial classes in the same namespace still merge correctly.

19. **Executable-line weighting**: `CalculateScore` applies a mild log2-scaled boost based on real (non-accessor) method count. Classes with 10 testable methods score ~23% higher than single-method classes with the same uncovered lines, preventing POCOs with many property lines from outscoring complex logic classes.

20. **Medium-complexity class boost**: `CalculateScore` applies a totalLines-based multiplier to favor "sweet spot" classes: <50 lines → 0.7x, 50–99 → 0.9x, 100–400 → 1.3x (★ sweet-spot), 400–800 → 1.0x, >800 → 0.8x. This directs agents toward classes with enough logic to test meaningfully but not so large they consume an entire session.

21. **Namespace cluster coupling penalty**: Before scoring, `get_testable_targets` pre-computes per-namespace coupled/skip assessment counts. When ≥3 classes in a namespace are assessed as `coupled` or `skip`, all classes in that namespace receive a 0.85x penalty. This prevents agents from repeatedly attempting classes in heavily-coupled namespaces.

22. **Gotcha→interface scoring propagation**: During constructor param analysis, `get_testable_targets` checks gotchas for each interface parameter. Gotchas with `mock`, `CS0854`, `self-referencing`, or `circular` categories indicate the interface is difficult to mock. Each such interface applies a 0.7x penalty (`Math.Pow(0.7, n)`). This surfaces mock problems before the agent wastes tokens attempting the class.

23. **LogSession auto-aggregation**: When `log_session` receives `gotchasDiscovered=0` or `assessmentsRecorded=0`, it auto-counts records from the gotchas/assessments stores that were created after the last session's end time. This compensates for agents that forget to count their own tool calls. The summary includes "(auto-counted from store)" when auto-aggregation fires.

24. **Source root resolution caching**: `ResolveSourceRoot` caches its result per data directory in a static `Dictionary<string, string?>`. This eliminates repeated env var lookups, config file reads, and log messages on every `get_source_snippet` / `get_testable_targets` call. `ResetSourceRootCache()` is exposed for testing.

25. **Self-assessment penalty**: When `excludeAssessed=false`, classes with their own `coupled` or `skip` assessment receive a 0.1x score penalty. They still appear in results (preserving visibility) but ranked very low. This prevents agents from re-attempting known-untestable classes when browsing the full target list.

26. **Unmockable interface penalty**: During constructor param analysis, `get_testable_targets` checks each interface parameter against the assessment store. Interfaces assessed as `skip` or `coupled` (e.g., `ILanguageServerClient` with extension-method-blocked mocking) apply a 0.2x penalty per interface (`Math.Pow(0.2, n)`). Stronger than the gotcha penalty (0.7x) because assessments are confirmed blockers, not just warnings.

27. **Base type coupling propagation**: If a class's `BaseType` (from type registry) is assessed as `coupled` or `skip`, the class receives a 0.15x penalty. Inheriting from a coupled base (e.g., `WriteOperationBase`) propagates the untestability. Also checks the transitive dependency set.

28. **Transitive dependency detection**: Before scoring, `get_testable_targets` builds a `knownBadDeps` set from the `Dependencies` field of all `coupled`/`skip` assessments. When any constructor parameter (concrete or interface) matches a known-bad dependency, coupling penalties apply even if the parameter type itself has no direct assessment. Example: if `ClassX` is assessed as coupled with `dependencies: ["SchemaTestHarnessBase"]`, any class depending on `SchemaTestHarnessBase` gets penalized.

29. **testsGenerated auto-estimation**: When `log_session` receives `testsGenerated=0` and `classesSucceeded` is non-empty, estimates the count from recent session averages (last 3 sessions with real test data only, to avoid inflation from early high-ROI sessions). The summary includes "(estimated from past session avg)" when auto-estimation fires. `EstimateTestsGenerated` is exposed as a testable internal helper.

30. **Log-scaled uncoveredLines base**: `CalculateScore` uses `10 * Math.Log2(1 + uncoveredLines)` instead of raw uncoveredLines. This compresses the range (20→43, 50→57, 100→67, 200→77) so testability multipliers dominate over raw line count. Prevents coupled-but-large classes (200 lines) from outscoring pure-but-small classes (30 lines). With linear base, 200/20 = 10x advantage; with log, ~1.8x.

31. **External service dependency penalty**: `ParamHelper.IsExternalDependency` heuristic detects constructor params that smell like file system, HTTP, database, stream, socket, process, registry, or environment access (case-insensitive substring match). Each external dep applies a 0.5x penalty (`Math.Pow(0.5, n)`) in `CalculateScore`. Reported in BuildReason as "⚠ N external service dep(s)".

32. **Deferred verdict as assessment blocker**: `excludeAssessed` now filters `deferred` alongside `skip` and `coupled`. Self-assessment penalty (0.1x) also applies to `deferred`. This prevents agents from re-attempting classes they explicitly deferred to a future session.

33. **Heavily-tested class cliff penalty**: When `existingTestCount >= 15`, an additional 0.3x multiplier is applied on top of the standard `1/(1+existingTestCount)` diminishing returns. At 15 tests, score becomes `1/16 * 0.3 ≈ 0.019x` — effectively invisible. This prevents agents from re-testing classes that have already been thoroughly covered, where remaining uncovered lines are likely coupled/untestable. BuildReason shows "⚠ N existing tests (heavily tested — diminishing ROI)" for 15+ test classes.

34. **Session ROI tracking via coveredLines**: `log_session` accepts an optional `coveredLines` parameter (actual lines of new code covered this session). `SessionRecord.CoveredLines` is persisted to JSONL. `get_sessions` aggregates now include `totalCoveredLines` and `avgLinesPerTest` (= totalCoveredLines / totalTests). Summary output shows "Covered lines: N (X.Y lines/test)" when coveredLines > 0. This enables precise ROI measurement: when linesPerTest drops below ~0.5, coverage uplift sessions are no longer cost-effective.

35. **Method-level coverage targeting**: `get_uncovered_methods` flattens per-class coverage gaps into per-method targets. Each method is scored with `CalculateMethodScore(uncoveredLines, hasTestFile, existingTestCount)`: log-scaled base `10 * Math.Log2(1 + uncoveredLines)`, `hasTestFile ? 2.0x : 0.5x` multiplier, mild diminishing returns `1/(1 + existingTestCount * 0.05)`. Skips classes assessed as `coupled`/`skip`, classes with `skipReason`, and zero-uncovered methods. Uses `IsBoilerplateMethod` filtering (`.ctor`, `.cctor`, `get_*`, `set_*`) by default. Returns stats (methodsWithTestFile, methodsWithoutTestFile, avgUncoveredLines, distinctClasses). **Use when class-level targeting is exhausted and linesPerTest ROI is declining.**

36. **HasTestFile scoring bias**: `get_testable_targets` cross-references test inventory `TestFiles` to detect whether a class already has a test file. Classes with existing test files receive a 1.5x score multiplier — extending an existing file is cheaper than creating new test infrastructure (usings, mocks, constructor setup). `TestableTarget` output now includes `hasTestFile` (bool) and `testFiles` (list). `BuildReason` shows "★ test file exists (extend)" when true.

37. **Coverage plateau detection**: `get_sessions` includes a `plateauWarning` field powered by `DetectPlateau`. Two signals: (1) **Absolute threshold** — if `avgLinesPerTest < 0.5` across the last 3 sessions with coverage data, returns "⚠ Coverage plateau detected". (2) **Declining trend** — if recent 3-session average drops below 50% of the prior 3-session average, returns "⚠ ROI declining". Requires 3+ sessions with `coveredLines > 0 && testsGenerated > 0`. Returns `null` when ROI is healthy.

38. **Incremental scaffold mode**: `generate_test_scaffold` accepts an optional `methodNames` parameter (CSV). When provided, generates only `[Fact]` method stubs instead of a full test class skeleton. Matches requested methods against coverage data for line range annotations. Synthetic entries are created for methods not found in coverage. Gotcha warnings are included as comments. Returns JSON with `mode: "incremental"`, stub count, and distinction between coverage-matched and synthetic stubs. **Use when extending an existing test file rather than creating a new one.**

39. **Watch mode (--watch)**: `ScannerWatcher` uses `FileSystemWatcher` to monitor assembly (.dll), coverage (.xml in TestResults hierarchy), and test (.cs) files. On change, debounces 1.5 seconds to coalesce rapid events (build output), then re-runs only the affected scanners plus optional `--enrich` and `--analyze`. Coverage watcher auto-discovers the newest `coverage.cobertura.xml` in the TestResults/{guid}/ hierarchy. Ignores changes in obj/bin subdirectories for test watchers. Enrichment and analysis functions are passed as delegates from Program.cs since the scanner entry point uses top-level statements (local functions can't be accessed from external classes). Runs until Ctrl+C with clean `CancellationToken`-based shutdown.

40. **Stub class discovery**: `get_stub_classes` identifies zero-or-near-zero coverage classes that are trivially testable: POCOs, stubs, static helpers, and simple logic classes with no mocking complexity. Cross-references type registry for constructor complexity, test inventory for existing tests, and assessments for skip/coupled exclusion. Each class is categorized (`poco`, `static-helpers`, `simple-logic`, `logic-heavy`, `unclassified`) and scored by: log-scaled uncoveredLines base, constructor simplicity bonus (parameterless=1.0x, mockable params=0.8x^n, concrete=0.6x^n), hasTestFile 1.5x, real method count bonus (3+=1.3x), and class size sweet spot (<=50 lines=1.2x). Filters: `maxCoveragePercent` (default 5.0), `maxCtorParams` (default 2), `includeWithTests` (default false). These targets were consistently the highest ROI in late-stage coverage sessions — discovered in session 9 where stub classes at 0% yielded 146 lines with zero mocking complexity.

41. **Overload disambiguation in scaffold**: `generate_test_scaffold` detects overloaded methods (same sanitized name, different signatures) and disambiguates test method names using Cobertura `signature` attribute. `UncoveredMethod.Signature` stores the CLR signature (e.g., `"(System.Object)System.Boolean"`). `BuildDisambiguatedNames` groups by sanitized name, extracts short param types via `ExtractParamSuffix` (e.g., `"Object"`, `"String_Int32"`), and falls back to numeric suffixes when param types still collide. Single methods keep simple names; overloads get `MethodName_ParamType` format.

42. **Configurable ROI threshold + session trend**: `get_testable_targets` accepts `roiThreshold` parameter (default 5.0) replacing the hardcoded 3.0 threshold. When top score is below threshold, a detailed ROI warning with strategy suggestions is emitted. Also adds `sessionROITrend` field cross-referencing session data: computes `currentTopScore`, last session's coverage delta and lines/test, and a trend indicator (`stable`, `declining`, `steep decline`, `insufficient data`) by comparing recent vs older session averages.

43. **Paginated assessment queries**: `get_assessments` supports `top` (default 20) and `skip` (default 0) parameters. Returns an envelope `{totalCount, returned, skip, top, hasMore, assessments}` instead of a flat array. `top=0` means unlimited. Pagination applies after filtering and deduplication (last-wins).

44. **Auto-computed coverage deltas**: `log_session` detects when both `coverageBefore` and `coverageAfter` are 0 (common when agents forget to pass values). Auto-computes `coverageAfter` from the current coverage-gaps store via `ComputeOverallCoverage` (sum of coveredLines / totalLines × 100), and `coverageBefore` from the previous session's `coverageAfter`. Summary output includes "(auto-computed from coverage data)" flag.

45. **Negative feedback loop**: After `log_session` persists a session record, `ApplyNegativeFeedback` processes each failed class: (1) appends a gotcha with category `session-failure` documenting the failure reason, (2) if the class's latest assessment is `testable`, downgrades it to `deferred` by appending a new assessment. This prevents future sessions from repeatedly attempting problematic classes. Actions are shown in the session summary under "🔄 FEEDBACK LOOP:".

46. **Mock recipe usage examples enrichment**: Scanner `--enrich` flag triggers `EnrichMockRecipeUsageExamples` after auto-generating mock recipes. Scans test files for `Mock<InterfaceName>` patterns and extracts 1–2 real usage snippets (creation + nearby Setup/Verify lines) per interface. `MockRecipe.UsageExamples` (List<string>) stores these snippets. Rewrites the mock-recipes.jsonl file with enriched data to avoid growing the file with duplicates.

47. **Multi-method source snippets**: `get_source_snippet` accepts comma-separated method names (e.g., `"Validate,Process,Execute"`). When multiple methods are requested, returns an envelope `{className, requestedMethods, returnedMethods, notFound, methods[]}` with each method's source extracted independently. Per-method `maxLines` is auto-scaled (`maxLines / methodCount`, min 50). Single-method requests preserve the original response format for backward compatibility.

## Footguns

1. **MetadataLoadContext assembly resolution**: The `PathAssemblyResolver` needs paths to ALL referenced assemblies (target dir + runtime libs). Missing a dependency → `FileNotFoundException` on type resolution. Solution: glob `*.dll` from both the target's output dir and runtime dir.

2. **MCP SDK version**: Using `0.3.0-preview.1` which is pre-release. API surface may change. The `[McpServerToolType]` / `[McpServerTool]` attribute pattern is stable in preview.

3. **Property detection**: `PropertyInfo.SetMethod` returns null for init-only props. Detect with `ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))` — but this type may not be in MetadataLoadContext. Fallback: mark `hasInit: false` for reflection-loaded types.

4. **Enum value extraction**: `Type.GetFields(BindingFlags.Public | BindingFlags.Static)` returns enum members. Filter out `value__` (the underlying value field). In MetadataLoadContext, call `GetFields()` directly (BindingFlags may behave differently).

5. **Cobertura class names**: The `name` attribute uses fully qualified names with dots (e.g., `Server.Common.Extensions.StringExtensions`). Must match against type registry which stores `Name` (short) and `Namespace` separately.

6. **JSONL encoding**: One record per line, no trailing newline after last record. Use `JsonSerializer.Serialize` on each record + `Environment.NewLine` for append.

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `TOTAL_RECALL_DATA` | `"data"` | Root data directory containing namespace subdirectories |
| `TOTAL_RECALL_NAMESPACE` | `"default"` | Default namespace subdirectory under data root |
| `TOTAL_RECALL_LOG_LEVEL` | `"info"` | Log verbosity: `debug`, `info`, `warn`, `error`, `quiet` (also accepts aliases: `verbose`, `trace`, `silent`, `none`, `warning`, `err`, `information`) |
| `TOTAL_RECALL_SOURCE_ROOT` | (none) | Override source root for `get_source_snippet` tool. Can also be set via scanner `--source-root` flag (persisted to `config.json`). |

## VS Code MCP Configuration

Add to the target workspace's `.vscode/mcp.json`:

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\Total.Recall\\src\\Total.Recall\\Total.Recall.csproj"
      ],
      "env": {
        "TOTAL_RECALL_DATA": "C:\\path\\to\\Total.Recall\\data",
        "TOTAL_RECALL_NAMESPACE": "your-namespace",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_SOURCE_ROOT": "C:\\path\\to\\target-repo\\src"
      }
    }
  }
}
```

## Commit Convention

```
feat(total-recall): <description>
```

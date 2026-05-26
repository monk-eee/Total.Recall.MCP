# AGENTS.MD — Total.Recall

## Repo Identity

| Key | Value |
|-----|-------|
| Solution | `Total.Recall.sln` |
| Version | 2.5.0-preview.1 |
| SDK | 8.0.400 (`rollForward: latestFeature`, actual: 8.0.418) |
| dotnet | `C:\Program Files\dotnet\dotnet.exe` |
| sourceRoot | `src/Total.Recall/` |
| testRoot | `tests/Total.Recall.Tests/` |
| targetFramework | net8.0 |
| transport | stdio (VS Code spawns process) |
| dataFormat | JSONL (one JSON object per line) |
| tests | 1098 main + 8 analyzer passing |

## Purpose

**Total.Recall is a persistent memory server for AI agents doing .NET code coverage work.**

When an AI agent writes tests for a large .NET codebase, it burns 60–70% of its context re-discovering the same information every session: type constructors, mock patterns, coverage gaps, known pitfalls, and what's already tested. Over 30 sessions to reach a coverage target, this wastes ~450K tokens and ~22 hours of wall-clock time.

Total.Recall eliminates this waste by converting ephemeral agent knowledge into durable, queryable data. Scanners extract type metadata, coverage gaps, and test inventories into JSONL files. An MCP server exposes 37 tools that let agents query this data instantly — one tool call replaces 10–15 file reads. Agents also write data back (gotchas, assessments, bug reports, session logs), creating a feedback loop that makes each session smarter than the last.

## Design Principles

These principles guide every implementation decision. When in doubt, choose the option that best satisfies them:

1. **Simplicity over cleverness** — JSONL files, no databases, no complex joins. Every tool is a single query against in-memory data. The entire data set is <2MB and loads in <1s. If a feature requires a query planner, it's too complex.

2. **Read-heavy, append-only** — Most operations are reads from pre-warmed in-memory caches. Writes are always appends to JSONL files (gotchas, assessments, sessions). No updates, no deletes. This makes the data git-friendly, grep-friendly, and corruption-resistant.

3. **Zero-config for agents** — Tools are auto-discovered via MCP protocol. Rich `[Description]` attributes on each tool tell the agent what it does, what parameters it takes, and when to use it. Agents don't need to be taught about Total.Recall — they find it.

4. **Graceful degradation** — If Total.Recall isn't running (or data is missing), agents fall back to standard file-reading workflows. No tool should be a hard dependency. MCP guidance lives in the consuming repo's AGENTS.md and copilot-instructions.md, not in skills.

5. **Namespace isolation** — Multiple repos share one Total.Recall server with different namespace subdirectories. Data never cross-contaminates. The `ns` parameter on every tool allows explicit targeting.

6. **Performance by default** — In-memory `StoreRegistry` singletons with file-change detection. Pre-built `Dictionary<string, TypeRecord>` for O(1) type lookups. `SharedJsonOptions` (3 static instances) for serialization cache reuse. Startup pre-warm so the first tool call hits memory, not disk.

7. **Three-layer observability** — Logs (stderr, configurable level, gone on restart), Metrics (in-memory counters, tool call and cache stats, gone on restart), Sessions (persistent JSONL, cross-session learning).

## Agent Working Rules (Mandatory)

### Prime directive (READ FIRST, OVERRIDES EVERYTHING BELOW)
**Do the right thing, not the expedient thing.** When a clean design and a quick hack both reach green tests, pick the clean design. When fixing one test the right way would require updating fifteen other tests, update the fifteen tests — do not add a back-compat shim, a transitional bridge, a "for now" indirection, or a `Current*()` helper that hides the legacy pattern. Those shortcuts calcify. They get committed with `// TODO` comments that never get resolved.

Concrete tells that you are about to take the expedient path:
- "I'll add a fallback so legacy callers keep working" — no, migrate the legacy callers.
- "Tests reference the old constant; I'll make the new code read both" — no, update the tests.
- "This is a bridge until the wider refactor lands" — the bridge becomes permanent. Land the refactor now or do not introduce the new abstraction yet.
- "Touching 15 files for one design change is too much" — if the design is right, touching 15 files is what it costs. Pay it.
- A `// TODO: remove once X migrates` comment in a commit that does not also do X.

If the right thing is genuinely too large for one commit, **stop and say so** — do not ship the expedient half. Either reduce scope or split into a sequence of right-shaped commits.

### Core coding philosophy

> "The code you write makes you a programmer.
> The code you delete makes you a good one.
> The code you don't have to write makes you a great one."
> — Mario Fusco

Lines of code are a cost, not an asset. The best contribution is often a smaller diff than you arrived expecting to write — a delete, a consolidation, or a one-line addition to an existing helper instead of a new sibling.

#### Before-you-write checklist (NON-NEGOTIABLE)

Run these four checks before writing any helper, utility, extension method, or "small" function:

1. **Search `src/Total.Recall/Infrastructure/` for the verb.** Use `grep_search` for the operation name (e.g. `Resolve`, `Load`, `Parse`, `Classify`). If anything already does this, use it. If a near-miss exists, extend it — do not fork. Infrastructure is the canonical home for shared helpers; if the helper belongs there and isn't there yet, add it there *first*, then call it.
2. **Search the whole repo for the method name you're about to type.** `grep_search` for `\b<MethodName>\s*\(` (regex). If it exists outside `tests/`, that's the existing implementation. Call it, or — if both are local — extract both to `Infrastructure/`.
3. **Check the red-flag name shapes.** If your method name starts with one of these, stop and look harder:
   - `Try*`, `Safe*`, `Resolve*`, `Load*OrDefault`, `Get*OrDefault`, `Ensure*`, `Retry*`, `Walk*`, `Atomic*`, `Canonical*`, `Normalize*`, `Sanitize*`, `Parse*`, `Build*`, `Format*`
   - Wrapper class suffixes: `*Helper`, `*Utils`, `*Utility`, `*Manager`, `*Service` (when the type holds no state and just groups static methods)
   - These are the exact shapes that get forked across `Tools/` and `Scanners/`. Check `Infrastructure/` again, harder. If you're tempted to add `Foo2Helper` because `FooHelper` doesn't quite fit, the answer is to fix `FooHelper`.
4. **Look at the call site for the second occurrence rule.** If you find yourself about to write the same helper twice in one session, the second occurrence is the signal to extract to `Infrastructure/` immediately. Do not "do it once more and clean up later". Later does not arrive.

#### The cheapest line is the line you didn't write

- Prefer extending an existing type over creating a sibling. `MethodNaming` already has 4 methods — adding a 5th is cheaper than `MethodNaming2` or `MethodNameDisambiguator`.
- Prefer adding a row to a data table over adding a branch to an if-chain. `AssertionRules.s_prefixHints` has 15 rows; the 16th is one line of data, not 30 lines of code.
- Prefer deleting a helper that has one caller and inlining it over preserving "for future reuse". Future reuse rarely arrives; the indirection always costs.
- Prefer collapsing two near-identical methods into one parameterised method over keeping both "for clarity". Two methods that differ by one literal are one method with one parameter.
- Prefer a `record` with init properties over a class with a constructor and five private setters. Less code, more guarantees.

#### Mechanical enforcement

Duplicate non-public `static` helpers (both `internal static` and `private static`) across `Tools/` and `Scanners/` are flagged at build time by the `Total.Recall.Analyzers` Roslyn analyzer (diagnostic `TR0001`, severity `Warning`). The analyzer ships in `src/Total.Recall.Analyzers/` and is wired into `src/Total.Recall/Total.Recall.csproj` as an analyzer reference. When `TR0001` fires the fix is normally extraction to `Infrastructure/`; if the two methods genuinely do different work and only collide on name + signature, rename one. **Do not silence the diagnostic with an allowlist or a `#pragma warning disable`.** If during a session you spot a duplicate that escaped review (e.g. predates the analyzer or lives in an analyzer-blind spot — e.g. `public static` helpers, which the analyzer deliberately skips), add it to the `Known duplicates` section of `docs/TODO.md` per the "Code reuse" rule below — silently leaving duplication for the next agent is a violation of this philosophy.

### File discipline
- **Keep files focused.** When a `.cs` file exceeds ~500 lines or holds more than one clear responsibility (e.g. a tool class plus three helpers plus a model), split it. The `Tools/`, `Scanners/`, `Models/`, `Infrastructure/` folder split is the model — keep adding folders rather than letting one file grow.
- **One public type per file** where practical. Nested helpers are fine; siblings belong in their own file.

### Test-driven workflow (NON-NEGOTIABLE)
- **Tests are the only way we ship.** Every tool, scanner, store, and infrastructure helper needs xUnit tests under `tests/Total.Recall.Tests/` mirroring the source folder.
- **Run `dotnet test Total.Recall.sln` after every change.** All tests must pass before commit. CI runs the same command on Ubuntu and Windows.
- **Never skip, `[Fact(Skip=...)]`, or comment out a test to make CI pass.** Fix the code, not the test.
- **Bug fixes require a regression test. No exceptions.**
  - Every fixed bug gets a `[Fact]` (or `[Theory]`) in `<Module>RegressionTests.cs` alongside the existing tests for that module, with an XML doc comment describing: what went wrong, what the impact was, what the fix is.
  - The test must FAIL against the un-fixed code and PASS against the fix. Confirm both directions before committing (revert the fix locally, watch it fail, restore the fix). **Do not use `git stash` for this** — temporarily edit the fix back out, verify, then restore.
  - Do not delete regression tests during refactors. They pin subtle behaviour ([`docs/DECISIONS.md`](docs/DECISIONS.md) documents those behaviours — most are regression-tested).
- **Always report bugs and failures, even ones you do not fix in this run.** If you notice a bug, a flaky test, suspicious behaviour, or a latent footgun while doing other work, add an entry to a `## Known bugs` section of a `docs/TODO.md` (create it if absent) before you finish the turn. We never silently drop bugs. Each entry: (1) what you observed, (2) where (file + symbol or test name), (3) impact, (4) whether fixed in this run or left for later.

### Build & test discipline
- **`dotnet build` must succeed with zero warnings** on the changed projects before commit. Nullable warnings, unused-using warnings, and analyzer warnings all count.
- **Use the workspace SDK** (`global.json` pins 8.0.400, rollForward `latestFeature`). Do not introduce target-framework changes (`net9.0`, etc.) without an ADR.
- **NuGet additions go through `dotnet add package`** at the csproj level. Pin to explicit versions in the `.csproj`. Update the "NuGet Dependencies" table in this file in the same commit.

### Git discipline
- **One commit = one meaningful unit of work.** Scoped, validated, tested.
- **Review every diff (`git diff --cached`) before commit.** Do not blindly accept generated code.
- **Commit message convention:** `feat(total-recall): <description>` / `fix(total-recall): <description>` / `test(total-recall): <description>` / `refactor(total-recall): <description>` / `docs(total-recall): <description>`.

### Doc discipline (NON-NEGOTIABLE)
Every shipped feature must update the agent-facing surfaces in the same commit. Doc drift here is treated like a failing test.
- **`AGENTS.md`** (this file) — if the change adds/removes a tool, scanner, env var, or introduces a new architectural behaviour, update the MCP Tools / Scanners / Environment Variables tables here. Append the numbered entry describing the new behaviour to [`docs/DECISIONS.md`](docs/DECISIONS.md). Both files in the same commit. The decisions list is load-bearing — keep it dense and append at the end.
- **`SPEC.md` / `README.md`** — user-visible behaviour changes (new tool, new CLI flag, new env var) go here too.
- **`docs/TOOL_REFERENCE.md`** — keep tool input/output schemas in sync when tool signatures change.
- **Per-tool `[Description]` attributes** — when a tool's behaviour changes, update its `[Description]` so MCP-protocol consumers see the new contract without reading docs.

Internal-only refactors (helper extraction, file split) only need a one-line note in AGENTS.md if at all.

### Multi-agent collaboration (READ THIS FIRST)
Multiple AI agents may operate concurrently in this worktree. Plan for it.

- **Never `git stash`.** Stash interacts catastrophically with concurrent edits and Windows file locks. To verify a regression test fails against un-fixed code, temporarily edit the fix back out in the file (and restore it). To park dirty files, commit a WIP commit (`git commit -m "wip"`) and amend later — WIP commits survive every failure mode that destroys a stash.
- **Always `git status --short` before staging.** If files you did not touch are already staged, a sibling agent left WIP there — `git restore --staged <file>` before you commit. Hijacking another agent's WIP into your commit is the worst failure mode.
- **Stage explicitly with named paths.** Never `git add -A` / `git add .`. Always `git add <specific-files-you-touched>`.
- **Verify the staged set immediately before every commit.** `git diff --cached --name-only` MUST list ONLY files you authored this turn. If it doesn't match, `git restore --staged <unexpected>` before `git commit`.
- **Stage as late as possible.** Edit, test, then `git add` + `git diff --cached --name-only` + `git commit` as a tight three-step block.
- **Do not run mass refactors** (`dotnet format` across the whole solution, sweeping renames via Roslyn) while another agent is active. Schedule them for a quiet window.
- **Treat `data/<namespace>/*.jsonl` as shared mutable state.** Two concurrent scanner runs against the same namespace will race on writes. Check for active scans before running `dotnet run -- scan`.
- **Read commits that landed during your turn.** `git log --oneline -5` at the start of any non-trivial action.
- Read-only audits (searches, `dotnet test`, `dotnet build`) are always safe in parallel. Writes are not.

### Code reuse (NON-NEGOTIABLE)
- **Always check `src/Total.Recall/Infrastructure/` before writing a new helper.** JSON serialization, store access, logging, metrics, repo config, param classification — these belong in `Infrastructure/`, not duplicated across `Tools/` and `Scanners/`. If the helper you want doesn't exist, **add it to `Infrastructure/` first** and then call it.
- **Existing infrastructure you will likely reach for (do not reinvent):**
  - [`SharedJsonOptions`](src/Total.Recall/Infrastructure/SharedJsonOptions.cs) — three static `JsonSerializerOptions` (CamelCase, CamelCaseIndented, Indented). Do NOT new up `JsonSerializerOptions` per call — STJ reflection cache is per-options-instance.
  - [`StoreRegistry`](src/Total.Recall/Infrastructure/StoreRegistry.cs) / `NamespaceStores` — singleton access to all 7 JSONL stores. Do NOT instantiate `JsonLineStore<T>` directly in a tool.
  - [`JsonLineStore<T>`](src/Total.Recall/Infrastructure/JsonLineStore.cs) — the only sanctioned JSONL reader/writer. Handles file-change detection and append semantics.
  - [`Log`](src/Total.Recall/Infrastructure/Log.cs) — stderr-only logger with configurable level. NEVER `Console.WriteLine` to stdout (would corrupt JSON-RPC).
  - [`Metrics`](src/Total.Recall/Infrastructure/Metrics.cs) — `Interlocked.Add`-based counters. Use `Metrics.RecordToolCall(...)` for new tools.
  - [`RepoConfig`](src/Total.Recall/Infrastructure/RepoConfig.cs) — env var + `config.json` resolution. Use `RepoConfig.GetNamespacePath(ns, outputPath)` for all data-dir resolution.
  - [`ParamHelper`](src/Total.Recall/Infrastructure/ParamHelper.cs) — constructor-param classification (interface vs concrete, external dependency heuristics).
- **When you spot a duplicate during unrelated work, file it.** Add a `Known duplicates` entry to `docs/TODO.md` describing the pattern, the files, and the proposed `Infrastructure/` home. Don't silently leave duplication for the next agent.

### Code design discipline (NON-NEGOTIABLE)
Idiomatic, testable C# by default.

- **Prefer instance classes over static mutable state when state has identity.** Data paths, file stores, caches — these belong on a class that takes the root in its constructor and exposes methods that close over it. Tests instantiate their own with a `tmp_path` and pass it via DI. The `StoreRegistry`/`NamespaceStores` split is the canonical example: paths are resolved per-namespace, not via a module-level mutable.
- **Dependency injection over static patching.** Tools that read a store / clock / config take it as a parameter (defaulted to the singleton). If you find yourself writing test setup that mutates a `static` field, the production code has a missing seam — fix the seam.
- **One seam per thing.** If both a path and a store wrapping that path are static, tests have to keep them in sync and silent drift is guaranteed. Pick one (the path) and construct the store on demand.
- **`record` types for value objects.** All models in `Models/` should be `record` or `record class` with init-only properties unless mutation is genuinely required. Frozen-by-default is the rule.
- **Interfaces at public seams** so contracts are checkable. Scanners and tool handlers should be reachable through an interface where multiple implementations exist or are likely.
- **`using` / `IDisposable` for resource lifecycles.** File handles, `MetadataLoadContext`, `FileSystemWatcher` — never leak.
- **Class only when state + behaviour bind.** A class wrapping one pure static method is anti-idiomatic; a class with shared state across methods is correct. If each method is independent, keep it a `static` helper.
- **Anti-patterns to refuse:**
  - Module-level `static Dictionary<string,T> _cache` + `static T Get(...)` + `static void ClearCache()`. That's a class without the class — write the class.
  - Two helpers that differ only by which directory they walk. That's one class with two instances.
  - "Helper" that takes the same first three args at every call site. Those are constructor params.
  - Test that mutates a production `static` field. Production code has a missing seam.

### Refactor & split discipline (NON-NEGOTIABLE)
**When a file grows past the ~500-line cap, do not reach for `partial class`. That is hiding the line count, not solving it.** A file that big is doing too many things. The job is to find what it's actually doing, name those things, and give each one a home.

The workflow, in order. Do not skip steps.

1. **Audit duplication inside the file first.** Read every method. Write down (in your turn, out loud) every block of code that resembles another block. Two `foreach` loops over the same collection with branchy bodies are one loop building one model used three ways. Two switch statements over the same set of type names are one catalog. Five `if (name.StartsWith(...))` chains are a data table. Find these before you split anything.

2. **Then audit duplication ACROSS files.** Before extracting an internal helper, look for the same pattern in sibling files (`Tools/*`, `Scanners/*`). If three tools repeat the same `Telemetry.Track + Metrics + Log + try/catch + JSON envelope` boilerplate, the right move is one new `Infrastructure/` helper that all three call — not three private helpers in three files. Shared infrastructure ships in its own commit, BEFORE the file split that consumes it.

3. **Separate "decide what" from "do it".** Files that mix data-gathering with output-rendering (compute a plan, then write a string; query stores, then serialize JSON; analyze a class, then emit a markdown report) get long because each side accretes independently. Split them: a plain `record` capturing the decisions, a planner that produces the record from stores, a renderer that takes the record and emits output. Each side is then independently testable and each file stays small.

4. **Data-driven over imperative.** A 15-branch `if (name.StartsWith("Get")) return "..." else if (name.StartsWith("Find")) ...` chain is data pretending to be code. Convert to `static readonly (string[] Prefixes, string Hint)[] s_rules` and a single walk. Same for keyword lists, type-name catalogs, archetype rules. The file shrinks 5x and the rules become trivially editable.

5. **One public type per file, named for what it does.** After the split, every new file's name should be a noun that describes its single responsibility (`TestScaffoldPlanner`, `AssertionRules`, `CtorParamPlan`). If you can't name a file that way, the split is wrong — you've sliced arbitrarily, not by responsibility.

6. **Validate after each extraction.** Run `dotnet test` after every move, not just at the end. If all tests still pass without modification, the refactor is behaviour-preserving (which it must be — no behaviour change in a refactor commit). If a test breaks, the split sliced through a hidden contract; back out and re-plan.

7. **Refuse `partial class` as a sizing tool.** `partial class` is for source generators and for legitimately disjoint concerns (designer-vs-code, generated-vs-hand-written). It is not for "this file is too long, let me cut it in half." Splitting a 1000-line class into two 500-line partials produces ONE 1000-line class spread across two files — the cognitive load is the same. Do the real work.

**Tell yourself the truth.** If a "split" leaves the same total amount of logic in the same shape just spread across more files, you did not refactor — you reorganized. The line count is a symptom; the disease is mixed responsibilities and duplicated patterns. Treat the disease.

### Logging standard (NON-NEGOTIABLE)
- **Use the `Log` class.** Never `Console.WriteLine` (stdout corrupts JSON-RPC) and never `Console.Error.WriteLine` directly (bypasses level filtering).
- **Log every tool invocation at `Debug` level** with parameters, lookup strategies, cache hits/misses, record counts. Volume is fine — the reader is an agent, and `TOTAL_RECALL_LOG_LEVEL=quiet` exists for CI.
- **Log every scanner step at `Info`.** What was loaded, what was written, how many records.

### Safety and secrets
- **Never log paths or content from `TOTAL_RECALL_SOURCE_ROOT` that may contain credentials** (e.g. `appsettings.*.json`, `.env`). The source-root snippet tool is best-effort and trusts the consuming repo to gitignore secrets.
- **No secrets in `data/*.jsonl`.** Gotchas, assessments, sessions are checked-in or shared — keep them descriptive without leaking keys.

### MCP-specific rules
- **stdout is reserved for JSON-RPC.** Any `Console.WriteLine`, unhandled exception with a stack trace going to stdout, or rogue `Trace.Write` will corrupt the protocol and break the agent's MCP session silently. All diagnostic output goes through `Log` to stderr.
- **Tool `[Description]` attributes are the agent's only contract.** When you change a tool's behaviour, update the description in the same commit. Agents do not read `AGENTS.md` — they read the descriptions through MCP discovery.
- **Backward compatibility on tool output schemas.** Agents may have cached field names. Add fields freely; do not rename or remove fields without bumping a major version note in `SPEC.md`.

## Build & Run Commands

```bash
# Build
dotnet build src/Total.Recall/Total.Recall.csproj

# Run as MCP server (stdio mode — VS Code launches this)
# When invoked from a terminal (TTY) with no args, prints usage instead of
# silently waiting on stdin. VS Code pipes stdin so still enters server mode.
dotnet run --project src/Total.Recall/Total.Recall.csproj

# One-shot bootstrap for a target repo (writes config.json + prints mcp.json)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- init "C:\path\to\target-repo"
dotnet run --project src/Total.Recall/Total.Recall.csproj -- init . --namespace myproject

# Health check: env vars, data root, per-namespace file counts, config validity
dotnet run --project src/Total.Recall/Total.Recall.csproj -- doctor
dotnet run --project src/Total.Recall/Total.Recall.csproj -- doctor --ns myproject

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

# Report CLI (read telemetry without spinning up the MCP server)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- report tool-stats --ns myproject
dotnet run --project src/Total.Recall/Total.Recall.csproj -- report cycles --pattern re-query --last 50
dotnet run --project src/Total.Recall/Total.Recall.csproj -- report scorecard --ns myproject
# Sub-commands: tool-stats | efficiency | scorecard | cycles | sessions | leaderboard
# All output is JSON — pipe through ConvertFrom-Json or jq for tables.
# Or pass --format table for a built-in text table (no piping required).

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
| Total.Recall.Analyzers | netstandard2.0 | `src/Total.Recall.Analyzers/Total.Recall.Analyzers.csproj` |

### Test Assembly

| Assembly | TFM | csproj Path | Status |
|----------|-----|-------------|--------|
| Total.Recall.Tests | net8.0 | `tests/Total.Recall.Tests/Total.Recall.Tests.csproj` | 1098 tests |
| Total.Recall.Analyzers.Tests | net8.0 | `tests/Total.Recall.Analyzers.Tests/Total.Recall.Analyzers.Tests.csproj` | 8 tests |

### Sibling Scanners (separate distribution, NOT in `Total.Recall.sln`)

These live as sibling folders under `src/` but are **independent projects** shipped on their language's native package manager. They are NOT referenced by `Total.Recall.csproj` and NOT added to `Total.Recall.sln` — the .NET SDK pack globs are scoped to `src/Total.Recall/` and will not pick them up. Each writes the canonical JSONL schema in [`docs/SCANNER_SCHEMA.md`](docs/SCANNER_SCHEMA.md).

| Scanner | Language | Path | Package | Entry point | Status |
|---------|----------|------|---------|-------------|--------|
| Total.Recall.Scanners.Python | Python 3.10+ | `src/Total.Recall.Scanners.Python/` | PyPI `total-recall-scan-py` | `total-recall-py` | scaffolded (feat/scanners) |
| Total.Recall.Scanners.TypeScript | Node 18+ / TS 5+ | `src/Total.Recall.Scanners.TypeScript/` | npm `@total-recall/scan` | `total-recall-ts` | planned (feat/scanners) |

**Rules for sibling-scanner work:**
- Never modify `src/Total.Recall/Total.Recall.csproj` to reference these dirs. Verify after each scanner addition: `dotnet pack src/Total.Recall/Total.Recall.csproj -c Release` and confirm the `.nupkg` contains NO `.py`, `.ts`, `node_modules`, or `__pycache__` entries.
- Conformance tests live under `tests/conformance/fixtures/{dotnet,python,typescript}-sample/` with golden JSONL snapshots — all three must produce identical schema-shaped output for analogous source.
- Real-world integration tests are gated by env vars (`TOTAL_RECALL_PY_INTEGRATION_REPO`, `TOTAL_RECALL_TS_INTEGRATION_REPO`); they skip if unset so CI without those repos still passes.
- Root `.gitignore` covers the sibling ecosystems: `__pycache__/`, `*.pyc`, `.venv/`, `node_modules/`, `dist/`, `*.tsbuildinfo`, `.pytest_cache/`, `.mypy_cache/`.

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
| `bugs.jsonl` | `report_bug` / `update_bug_status` tools | Class-scoped bug reports (append-only, latest record per id wins) |
| `sessions.jsonl` | `log_session` tool | Session outcomes for cross-session learning (append-only) |
| `tool-calls.jsonl` | `Telemetry.Track` (auto on every tool call) | Every MCP tool call: name, ns, sessionId, taskId, params summary, latency, response bytes (append-only) |
| `tasks.jsonl` | `start_task` / `end_task` | Agent task bracketing — start/end, success/abandon, intent narrative (append-only) |
| `cycles.jsonl` | `CycleDetector` (auto) | Detected behaviour cycles: re-query, context-loss, oscillation patterns (append-only) |
| `challenges.jsonl` | `get_next_challenge` / `submit_challenge` | Eval challenge problems offered to agents (append-only) |
| `evals.jsonl` | `submit_challenge` (graded by `ChallengeGrader`) | Eval scoring outcomes: pass/fail, score, breakdown (append-only) |
| `config.json` | Scanner `--source-root` | Per-namespace scan config (source root, paths, timestamp) |

## MCP Tools

37 tools. All accept an optional `ns` (namespace) parameter to target a specific dataset. **Every tool call is intercepted by `Telemetry.Track` and appended to `tool-calls.jsonl` when `TOTAL_RECALL_MODE` ≠ `off`.**

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
| `report_bug` | className, severity, description, methodName?, repro?, foundInTestName?, model?, ns? | JSON {ok, id, ...} | File a class-scoped bug report. Severity: `low`\|`medium`\|`high`\|`critical`. Returns stable `bug-{12-hex}` id. |
| `get_bugs` | className?, severity?, status? (default `open`), top?, ns? | JSON {totalCount, bugs[]} | Query bugs. Latest record per id wins; sorted critical-first. |
| `update_bug_status` | bugId, status, notes?, ns? | JSON {ok, previousStatus, newStatus, …} | Transition a bug. Appends a new record with the same id (append-only history). |
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

### Telemetry & Eval Tools (Cuts 1–6)

| Tool Name | Input | Output | Purpose |
|-----------|-------|--------|---------|
| `start_task` | intent, ns? | taskId | Begin a bracketed task. Sets `Telemetry.ActiveTaskId`. Calling twice auto-abandons the prior task. |
| `end_task` | outcome (`success`/`abandon`), notes?, ns? | Confirmation + duration | End the active task. Persists to `tasks.jsonl`. |
| `log_task` | intent, outcome, notes?, ns? | Confirmation | Convenience: start + immediately end a task (for one-shot operations). |
| `get_cycles` | last?, pattern?, ns? | Cycle[] JSON | Recent detected cycles (re-query / context-loss / oscillation). Useful for self-diagnosis. |
| `get_tool_call_stats` | last?, ns? | Per-tool call counts, p50/p95 latency, avg response bytes | Tool usage histogram from `tool-calls.jsonl`. |
| `get_efficiency_report` | last?, ns? | Sessions × cycles × tasks summary | Tokens-per-task, redundant-call rate, plateau warnings. |
| `get_model_scorecard` | ns? | Per-model aggregated metrics | Cross-model comparison from sessions + tasks + evals. |
| `get_next_challenge` | difficulty?, ns? | Challenge JSON (prompt, required tools, budget) | Pull a graded eval problem for the agent to attempt. |
| `submit_challenge` | challengeId, response, toolsUsed (csv), ns? | EvalResult JSON (score, pass/fail, breakdown) | Submit a challenge attempt. Graded by `ChallengeGrader` (40% required-tools, 20% budget, 40% correctness). Pass threshold 0.7. |
| `get_eval_leaderboard` | ns? | Model rankings JSON | Aggregated eval pass/fail rates across models. |
| `report_context_reset` | reason?, ns? | Confirmation | Agent self-reports a compaction/context-window reset. Recorded as a session marker for downstream attribution. |

## Scanners

| Scanner | Input | Output | Key Library |
|---------|-------|--------|-------------|
| AssemblyScanner | .dll path | type-registry.jsonl | MetadataLoadContext |
| CoberturaParser | coverage.cobertura.xml | coverage-gaps.jsonl | System.Xml.Linq |
| TestProjectScanner | test project directory | test-inventory.jsonl | Regex on .cs files |
| ScannerWatcher | `--watch` flag | Re-runs scanners on file changes | FileSystemWatcher |

## Architecture Decisions

Moved to [`docs/DECISIONS.md`](docs/DECISIONS.md). 62 numbered entries
documenting every load-bearing design choice. Append-only - later
entries supersede earlier ones; earlier entries are not edited unless
the behaviour was removed. Most are pinned by regression tests.

When you add a tool, scanner, env var, or architectural behaviour,
append a new numbered entry to `docs/DECISIONS.md` in the same commit
as the code change. The list is load-bearing - keep it dense.

## Footguns

1. **MetadataLoadContext assembly resolution**: The `PathAssemblyResolver` needs paths to ALL referenced assemblies (target dir + runtime libs). Missing a dependency → `FileNotFoundException` on type resolution. Solution: glob `*.dll` from both the target's output dir and runtime dir, **deduplicate by `AssemblyName.Name` (NOT file path)** — publish-style targets that ship their own `mscorlib.dll` / `System.Private.CoreLib.dll` next to the host runtime dir's copy will otherwise throw `FileLoadException: Assembly with same name is already loaded` on core-assembly probe. See `AssemblyScanner.BuildResolverPaths`.

2. **MCP SDK version**: Using `0.3.0-preview.1` which is pre-release. API surface may change. The `[McpServerToolType]` / `[McpServerTool]` attribute pattern is stable in preview.

3. **Property detection**: `PropertyInfo.SetMethod` returns null for init-only props. Detect with `ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))` — but this type may not be in MetadataLoadContext. Fallback: mark `hasInit: false` for reflection-loaded types.

4. **Enum value extraction**: `Type.GetFields(BindingFlags.Public | BindingFlags.Static)` returns enum members. Filter out `value__` (the underlying value field). In MetadataLoadContext, call `GetFields()` directly (BindingFlags may behave differently).

5. **Cobertura class names**: The `name` attribute uses fully qualified names with dots (e.g., `MyApp.Common.Extensions.StringExtensions`). Must match against type registry which stores `Name` (short) and `Namespace` separately.

6. **JSONL encoding**: One record per line, no trailing newline after last record. Use `JsonSerializer.Serialize` on each record + `Environment.NewLine` for append.

## Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `TOTAL_RECALL_DATA` | `"data"` | Root data directory containing namespace subdirectories |
| `TOTAL_RECALL_NAMESPACE` | `"default"` | Default namespace subdirectory under data root |
| `TOTAL_RECALL_LOG_LEVEL` | `"info"` | Log verbosity: `debug`, `info`, `warn`, `error`, `quiet` (also accepts aliases: `verbose`, `trace`, `silent`, `none`, `warning`, `err`, `information`) |
| `TOTAL_RECALL_SOURCE_ROOT` | (none) | Override source root for `get_source_snippet` tool. Can also be set via scanner `--source-root` flag (persisted to `config.json`). |
| `TOTAL_RECALL_MODE` | `"passive"` | Telemetry mode: `off` (no recording), `passive` (record tool calls + cycles, no challenge serving), `active-eval` (passive + serve challenges via `get_next_challenge`). |

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

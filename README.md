# Total.Recall

**An MCP server that gives AI coding agents persistent memory — and measures their behaviour.**

## In one paragraph

Total.Recall is a side-car process that watches your .NET repo and tells AI coding agents *which classes are worth testing next, why, and what mistakes other agents have already made on them.* A scanner extracts type metadata, coverage gaps, test inventories, and architectural metrics into small JSONL files. An MCP server then exposes 34 tools over stdio so the agent can query that data in one call instead of reading ten files — and write back what it learns (gotchas, testability verdicts, session outcomes) so the next session starts smarter. Every tool call is recorded, behavioural anti-patterns (re-query loops, context-loss after compaction, oscillation between targets) are auto-detected, and a deterministic grader scores agents on reproducible eval challenges. No database, no LLM-as-judge, no long-running service — just flat files, ~2MB in memory, boots in under a second.

## A bit more detail

Total.Recall is two things in one process:

1. **A behaviour observatory + eval harness** — every tool call is recorded, behavioural anti-patterns (re-query loops, context-loss after compaction, oscillation between targets) are auto-detected, agent work is bracketed into named tasks, and a deterministic grader runs reproducible challenges to score models against each other without an LLM-as-judge. This is the unusual half — most "agent tooling" repos give you a tool; this one also instruments whether the tool is actually working.
2. **A persistent memory store** — the agent's notes, gotchas, testability verdicts, mock recipes, and prior-session outcomes, all queryable through 34 MCP tools. One tool call replaces 10–15 file reads.

The included scanners target .NET (Cobertura coverage, `MetadataLoadContext` reflection), which is the only public MCP server I'm aware of in this specific niche — AI-assisted test coverage uplift on a large C# codebase. The memory + telemetry + eval substrate is designed to be language-agnostic, but .NET is the only scanner today; a Python or TypeScript scanner is left as an exercise.

## Why it exists

When an AI agent writes tests for a large codebase, it burns 60–70% of its context re-discovering the same things every session: type constructors, mock patterns, coverage gaps, known pitfalls, and what's already tested. Over 30 sessions to reach a coverage target, that's ~450K tokens and ~22 hours of wall-clock time wasted in re-discovery and build-fail-fix cycles.

And nobody can tell whether the agent is actually getting better, worse, or just thrashing — because there's no record of what it tried, what it learned, or which model handled it best.

## What it does

- **Scanners** extract type metadata, coverage gaps, and test inventories into append-only JSONL files
- **34 MCP tools** let agents query this data instantly — one tool call replaces 10–15 file reads
- **Agents write back** gotchas, assessments, and session logs, creating a feedback loop that makes each session smarter than the last
- **Telemetry + eval harness** (v2.4) records every tool call, detects behaviour anti-patterns (re-query, context-loss, oscillation), brackets work into tasks, and runs deterministic grader challenges for cross-model scoring

## Install

Total.Recall ships as a [.NET global tool on NuGet](https://www.nuget.org/packages/TotalRecall.Mcp):

```bash
dotnet tool install -g TotalRecall.Mcp --version 2.4.0-preview.1
```

This exposes a single command, `total-recall`, on your `PATH`. The same
binary is the MCP server (default), the scanner (`total-recall scan`),
and the report reader (`total-recall report`).

To update, uninstall, or build from source:

```bash
dotnet tool update    -g TotalRecall.Mcp
dotnet tool uninstall -g TotalRecall.Mcp
```

```bash
# Or build from source
git clone https://github.com/monk-eee/Total.Recall.MCP
cd Total.Recall.MCP
dotnet build src/Total.Recall/Total.Recall.csproj
```

## Quickstart

```bash
# 1. Scan your target repo
total-recall scan \
  --assembly path/to/YourProject.dll \
  --coverage path/to/coverage.cobertura.xml \
  --tests    path/to/YourProject.Tests \
  --namespace myproject --enrich

# 2. Add Total.Recall to your target workspace's .vscode/mcp.json (see below)

# 3. In Copilot chat: "get testable targets, top 5"
```

See [docs/QUICKSTART.md](docs/QUICKSTART.md) for the full walkthrough and [docs/DEMO.md](docs/DEMO.md) for a 90-second end-to-end story.

## Seeing it in action

The CLI has three modes: MCP server (default), `scan` (extract data), and `report` (read telemetry). The blocks below are real terminal output, not mockups — `scan` was run against Total.Recall's own assembly.

**Scanning a target assembly** — populates JSONL stores under `data/<namespace>/`:

```text
$ total-recall scan \
    --assembly src/Total.Recall/bin/Debug/net8.0/Total.Recall.dll \
    --tests    tests/Total.Recall.Tests \
    --namespace self --enrich

Total.Recall Scanner v2.4.0-preview.1 — output: ...\data\self
  Scanning assembly... ✓ type-registry.jsonl — 101 types
  Scanning tests...    ✓ test-inventory.jsonl — 48 test files
  Enriching coverage data... ✓ 0 classes enriched with test counts + testability
  Auto-generating mock recipes... ✓ no new recipes needed
  ✓ config.json updated

  ── Data Summary ──
    type-registry      101 records
    coverage-gaps        — (not found)
    test-inventory      48 records
    mock-recipes         — (not found)
    gotchas              — (not found)
    assessments          — (not found)
    sessions             — (not found)

Done. [types:101, test-files:48, enriched:0]
```

**Reading telemetry** — same binary, `report` sub-command. JSON by default, `--format table` for a fixed-width view; pipe JSON through `ConvertFrom-Json | Format-Table` or `jq` for custom shapes:

```text
$ total-recall report

Usage: total-recall report <sub-command> [options]

Sub-commands:
  tool-stats     Per-tool call counts, p50/p95 latency, response bytes
  efficiency     Session-level tokens / bytes / cycles / dedupe report
  scorecard      Per-model aggregated metrics across sessions+tasks+evals
  cycles         Recent detected behaviour cycles (re-query, context-loss, oscillation)
  sessions       Session history + plateau warning + lines-per-test ROI
  leaderboard    Per-model eval pass rate / avg score

Options:
  --ns <name>            Namespace to query (default: TOTAL_RECALL_NAMESPACE)
  --last <N>             Limit results (cycles default 20, sessions default 5)
  --pattern <name>       Filter cycles by pattern: re-query | context-loss | oscillation
  --format <json|table>  Output format (default: json)
```

**MCP server mode** — run `total-recall` with no arguments. The process speaks JSON-RPC over stdio and is launched by VS Code via `.vscode/mcp.json` (see [VS Code MCP setup](docs/QUICKSTART.md)). All 34 tools are auto-discovered via MCP protocol; the agent does not need configuration.

## Design Principles

1. **Simplicity over cleverness** — JSONL files, no databases, no complex joins. Every tool is a single query against in-memory data. The entire data set is <2MB and loads in <1s.
2. **Read-heavy, append-only** — Most operations are reads from pre-warmed in-memory caches. Writes are always appends (gotchas, assessments, sessions). No updates, no deletes. Git-friendly, grep-friendly, corruption-resistant.
3. **Zero-config for agents** — Tools are auto-discovered via MCP protocol. Rich `[Description]` attributes guide usage. Agents don't need to be taught about Total.Recall.
4. **Graceful degradation** — If Total.Recall isn't running, agents fall back to standard file-reading workflows. No tool is a hard dependency.
5. **Namespace isolation** — Multiple repos share one server with different namespace subdirectories. Data never cross-contaminates.
6. **Performance by default** — In-memory `StoreRegistry` singletons, O(1) type lookups via pre-built dictionaries, startup pre-warm, shared `JsonSerializerOptions` instances.
7. **Three-layer observability** — Logs (stderr, configurable level), Metrics (in-memory counters), Sessions (persistent JSONL for cross-session learning).

## Tools

### v2 — Decision Engine

| Tool | Purpose |
|------|---------|
| `get_testable_targets` | Pre-scored target list cross-joining 6 data sources. **First call of every session.** |
| `get_source_snippet` | Actual C# source from target repo (replaces `read_file` calls) |
| `generate_test_scaffold` | Complete test class skeleton: usings, mocks, constructor, [Fact] stubs, gotcha comments |
| `log_session` | Write session outcomes for cross-session learning. **Last call of every session.** |
| `get_sessions` | Session history + aggregate analytics (tokens, coverage deltas, success rates) |
| `get_uncovered_methods` | Method-level ROI targets — use when class-level targeting is exhausted |
| `get_stub_classes` | Zero-coverage trivially-testable classes (POCOs, stubs, static helpers) |

### v1 — Lookup Index

| Tool | Purpose |
|------|---------|
| `resolve_type` | Namespace + constructor + property lookup. O(1) exact match, fallback to fuzzy. |
| `get_context` | Combined: type + gotchas + tests + mocks + assessments + coverage + sessions in one call |
| `get_mock_recipe` | Pre-built Moq setup code for an interface |
| `get_coverage_gaps` | ROI-ranked uncovered classes + methods from Cobertura XML |
| `get_gotchas` / `add_gotcha` | Known pitfalls for a type / record new ones |
| `get_test_inventory` | Existing test methods per class (prevent duplication) |
| `add_assessment` / `get_assessments` | Record and query testability verdicts |

### Static Analysis

| Tool | Purpose |
|------|--------|
| `get_class_metrics` | Per-class coupling (Ca/Ce), instability, archetype, cluster, dependency lists |
| `get_dependency_graph` | Local subgraph for a class — deps, consumers, Mermaid diagram |
| `get_analysis_summary` | Architectural overview: hot interfaces, most coupled classes, clusters |

### Observability & Learning

| Tool | Purpose |
|------|--------|
| `get_metrics` | Server telemetry: tool calls, cache hit rates, lookup strategy distribution |
| `learn_test_patterns` | Analyze existing test files to learn naming, assertion, and mock conventions |
| `get_gotcha_insights` | Cluster gotchas into patterns, generate Footguns documentation |
| `refresh_coverage` | Re-parse Cobertura XML mid-session without a full rescan |

### Telemetry & Eval (Cuts 1–6, v2.4)

Every tool call is intercepted and recorded to `tool-calls.jsonl` (controlled by `TOTAL_RECALL_MODE`). These tools let agents observe their own behaviour and run reproducible evals.

| Tool | Purpose |
|------|--------|
| `start_task` / `end_task` / `log_task` | Bracket agent work into named tasks. Auto-abandons stale tasks on re-start. |
| `get_cycles` | Recent detected behaviour cycles (re-query / context-loss / oscillation) for self-diagnosis |
| `get_tool_call_stats` | Per-tool call counts, p50/p95 latency, average response bytes |
| `get_efficiency_report` | Sessions × cycles × tasks: tokens/task, redundant-call rate, plateau warnings |
| `get_model_scorecard` | Cross-model aggregate metrics from sessions + tasks + evals |
| `get_next_challenge` / `submit_challenge` | Pull and submit deterministic eval problems (graded 40% required-tools + 20% budget + 40% correctness, pass ≥ 0.7) |
| `get_eval_leaderboard` | Aggregated eval pass/fail rates by model |
| `report_context_reset` | Agent self-reports a compaction; rotates session id so post-reset behaviour is attributed correctly |

See [docs/TOOL_REFERENCE.md](docs/TOOL_REFERENCE.md) for complete parameter documentation.

## How It Works

VS Code spawns the Total.Recall process (via `.vscode/mcp.json`) when Copilot initializes. The server stays alive for the session, and Copilot auto-discovers all 34 tools over stdio JSON-RPC.

### Recommended workflow

1. **`get_testable_targets`** — pick targets (pre-scored, pre-filtered by ROI)
2. **`get_source_snippet`** — read the implementation
3. **`generate_test_scaffold`** — get a complete test skeleton
4. **Fill in test logic** using `get_context`, `get_mock_recipe`, `get_gotchas`, etc.
5. **`add_gotcha`** — record any new pitfalls discovered
6. **`log_session`** — persist outcomes for future sessions

### Before vs. after

| Without MCP | With MCP |
|---|---|
| Read source to find constructors (~10-15 tool calls per type) | `get_testable_targets` → `get_source_snippet` — 2 calls |
| Trial-and-error mock setups → build failures (3-5 per interface) | `get_mock_recipe` — copy-paste working Moq code |
| No memory of past pitfalls → repeat the same mistakes | `get_gotchas` — known traps surfaced automatically |
| Accidentally re-write existing tests | `get_test_inventory` — see what's already covered |
| Guess which classes need tests most | `get_testable_targets` — ROI-ranked with composite scoring |
| Start every session from scratch | `get_sessions` — see what worked (and failed) before |

## Performance

- **Startup pre-warm**: All JSONL loaded into memory on server start (<2MB, <1s)
- **O(1) type lookups**: Pre-built `Dictionary<string, TypeRecord>` (exact + case-insensitive)
- **Singleton stores**: All tools share `StoreRegistry` singletons — no redundant file reads
- **Shared serializer**: 3 static `JsonSerializerOptions` instances (STJ caches reflection metadata)
- **Cache invalidation**: File-change detection via `LastWriteTimeUtc` — auto-reloads on rescan

## Usage

### Scan a target assembly

```bash
total-recall scan ^
  --assembly "path/to/YourProject.dll" ^
  --coverage "path/to/coverage.cobertura.xml" ^
  --tests "path/to/YourProject.Tests/" ^
  --source-root "path/to/your-repo/src" ^
  --namespace myproject ^
  --enrich
```

| Flag | Description |
|------|-------------|
| `--assembly` | Target .NET assembly (.dll) — builds type registry |
| `--coverage` | Cobertura XML coverage report — builds coverage gaps |
| `--tests` | Test project directory — builds test inventory |
| `--source-root` | Target repo source root — enables `get_source_snippet` (persisted to `config.json`) |
| `--namespace` | Namespace subdirectory under `TOTAL_RECALL_DATA` |
| `--output` | Override data output directory entirely |
| `--enrich` | Cross-reference coverage with type registry + test inventory |
| `--analyze` | Run static analysis: dependency graph, coupling metrics, cluster detection |
| `--watch` | Watch mode: auto re-scan on file changes (Ctrl+C to stop) |
| `--test-framework` | Test framework: `xunit` (default), `nunit`, `mstest` |
| `--mock-library` | Mock library: `moq` (default), `nsubstitute`, `fakeiteasy` |
| `--test-namespace-pattern` | Pattern for test namespace derivation (default: `{Namespace}.Tests`) |

### When to rescan

| Condition | What to run |
|-----------|------------|
| Rebuilt target assembly | `--assembly path/to/dll --namespace ns --enrich` |
| New coverage run | `--coverage path/to/xml --namespace ns --enrich` |
| Changed test files | `--tests path/to/tests --namespace ns` |
| Just re-enrich | `--namespace ns --enrich` |
| **Continuous (recommended)** | **Add `--watch` to any of the above** |

You do **not** need to rescan for gotchas (appended live), mock recipes (manual edits), assessments (appended live), or sessions (appended live).

### Watch mode

Add `--watch` to keep the scanner running and automatically re-scan when files change:

```bash
total-recall scan ^
  --assembly "path/to/YourProject.dll" ^
  --coverage "path/to/coverage.cobertura.xml" ^
  --tests "path/to/YourProject.Tests/" ^
  --namespace myproject ^
  --enrich --analyze --watch
```

The watcher monitors:
- **Assembly .dll** — re-scans type registry when you rebuild
- **Coverage .xml** — re-parses when test runner produces new results (auto-finds newest in TestResults/)
- **Test .cs files** — re-scans test inventory when you add/modify tests

Changes are debounced (1.5s) to coalesce rapid build events. After each re-scan, `--enrich` and `--analyze` run automatically if enabled. Press **Ctrl+C** to stop.

### VS Code MCP configuration

Create `.vscode/mcp.json` in your **target workspace** (the repo you're writing tests for).

**Recommended — using the installed global tool** (`dotnet tool install -g TotalRecall.Mcp`):

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "total-recall",
      "env": {
        "TOTAL_RECALL_DATA": "C:\\path\\to\\data",
        "TOTAL_RECALL_NAMESPACE": "your-namespace",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_SOURCE_ROOT": "C:\\path\\to\\target-repo\\src"
      }
    }
  }
}
```

**Alternative — running from a source checkout** (no `dotnet tool install` required):

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\Total.Recall.MCP\\src\\Total.Recall\\Total.Recall.csproj"
      ],
      "env": {
        "TOTAL_RECALL_DATA": "C:\\path\\to\\data",
        "TOTAL_RECALL_NAMESPACE": "your-namespace",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_SOURCE_ROOT": "C:\\path\\to\\target-repo\\src"
      }
    }
  }
}
```

### Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `TOTAL_RECALL_DATA` | `"data"` | Root data directory containing namespace subdirectories |
| `TOTAL_RECALL_NAMESPACE` | `"default"` | Default namespace subdirectory under data root |
| `TOTAL_RECALL_LOG_LEVEL` | `"info"` | Log verbosity: `debug`, `info`, `warn`, `error`, `quiet` |
| `TOTAL_RECALL_SOURCE_ROOT` | (none) | Override source root for `get_source_snippet` |
| `TOTAL_RECALL_MODE` | `"passive"` | Telemetry mode: `off` (no recording), `passive` (record tool calls + cycles), `active-eval` (passive + serve challenges via `get_next_challenge`) |

## Inspecting Telemetry (CLI Reports)

The same process is a triple-mode entry point: **MCP server** (default), **scanner** (`scan` sub-command), and **report reader** (`report` sub-command). The report mode reads the recorded telemetry JSONL without needing the server to be live — handy for terminals, CI, and post-mortems.

```bash
total-recall report tool-stats   --ns myproject
total-recall report cycles       --ns myproject --pattern re-query --last 50
total-recall report scorecard    --ns myproject
total-recall report sessions     --ns myproject --last 10
total-recall report efficiency   --ns myproject
total-recall report leaderboard  --ns myproject
```

| Sub-command | What it reads |
|-------------|---------------|
| `tool-stats` | Per-tool call counts, p50/p95 latency, avg response bytes |
| `cycles` | Detected behaviour cycles (re-query, context-loss, oscillation) |
| `sessions` | Session history + aggregate analytics |
| `scorecard` | Cross-model metrics from sessions + tasks + evals |
| `efficiency` | Sessions × cycles × tasks: tokens/task, redundant-call rate, plateau warnings |
| `leaderboard` | Eval pass/fail rates by model |

Options: `--ns <name>` (or `--namespace`), `--last <int>`, `--pattern <string>`, `--format <json|table>`.
Exit codes: `0` ok, `1` missing/unknown sub-command, `2` underlying tool threw.

Default output is JSON. Pass `--format table` for a built-in fixed-width text table:

```bash
total-recall report scorecard --ns myproject --format table
```

For advanced shaping, JSON output pipes cleanly through PowerShell or `jq`:

```powershell
total-recall report tool-stats --ns myproject `
  | ConvertFrom-Json | Select-Object -ExpandProperty tools | Format-Table -AutoSize
```

While a report runs, `TOTAL_RECALL_MODE` is forced to `off` so the report itself doesn't append to `tool-calls.jsonl`. The original mode is restored when it exits.

## Documentation

| Document | Purpose |
|----------|---------|
| [QUICKSTART.md](docs/QUICKSTART.md) | Step-by-step setup guide |
| [TOOL_REFERENCE.md](docs/TOOL_REFERENCE.md) | Complete parameter docs for all 34 tools |
| [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Observability guide + common problem fixes |
| [INTEGRATION.md](docs/INTEGRATION.md) | How to wire Total.Recall into a target repo |
| [copilot-instructions-template.md](docs/copilot-instructions-template.md) | Drop-in template for `.github/copilot-instructions.md` |
| [agents-md-template.md](docs/agents-md-template.md) | Drop-in template for target repo `AGENTS.md` MCP section |
| [ADR-001](docs/ADR-001-repo-level-integration.md) | Architecture decision: repo-level integration |
| [SPEC.md](SPEC.md) | Full implementation specification |

## Troubleshooting

### Observability layers

| Layer | Where | Persisted? | Purpose |
|-------|-------|-----------|---------|
| **Logs** | stderr (console) | No — gone when process exits | Startup diagnostics, error traces, operational events |
| **Metrics** | In-memory counters | No — resets on restart | Per-process tool call counts, cache hit rates, lookup strategy stats |
| **Sessions** | `sessions.jsonl` on disk | Yes — append-only | Cross-session learning: tokens, classes, coverage deltas, failure patterns |

**Logs** go to stderr (never stdout — that's the JSON-RPC transport). In VS Code: **View → Output → select "Total.Recall"**. From a terminal: `total-recall 2> total-recall.log`

**Metrics** are queried via the `get_metrics` tool. Key signals: `cache.hitRate` > 90% is healthy; `lookup.exact` dominating lookups means agents are using precise names; `typeindex.rebuilds` ≤ 1 means the index stays warm.

**Sessions** persist to `sessions.jsonl` and survive restarts. Query with `get_sessions` for aggregate analytics (tokens/test, success rate, coverage deltas, plateau detection).

### Common problems

| Problem | Check | Fix |
|---------|-------|-----|
| Server won't start | `total-recall` from terminal | Fix build errors; verify `.vscode/mcp.json` paths; restart VS Code |
| Tools return empty results | Startup log shows `✗` for data files | Run scanner: `total-recall scan --assembly ... --namespace ...` |
| `get_source_snippet` fails | No `TOTAL_RECALL_SOURCE_ROOT` or `config.json` | Set env var in `mcp.json`, or re-run scanner with `--source-root` |
| Type not found | Name not in registry | `Select-String "TypeName" data/ns/type-registry.jsonl`; re-scan if stale |
| High cache miss rate | Another process writing JSONL? | Scanner running concurrently? File sync tools touching data dir? Exclude from antivirus/OneDrive |
| Scanner fails | Assembly missing deps | Ensure all referenced DLLs are in the same directory; verify Cobertura XML format |

### Startup log anatomy

```
✓ = file loaded and cached in memory
✗ = file missing or empty (tools for that data return empty results)
⚡ = type index built (O(1) lookups ready)
WARN = degraded but not fatal
ERROR = load failed — check file permissions or JSON format
```

### Quick data health check

```powershell
Get-ChildItem "data/your-namespace/*.jsonl" | ForEach-Object {
    [PSCustomObject]@{
        File = $_.Name
        Size = "{0:N0} KB" -f ($_.Length / 1KB)
        Records = (Get-Content $_.FullName | Measure-Object).Count
    }
} | Format-Table -AutoSize
```

### Lifecycle

On **server start**: logs to stderr, pre-warms all JSONL into memory, builds type index, starts metrics at zero. During **operation**: every tool call increments metrics; writes (gotchas, assessments, sessions) append to JSONL on disk. On **server stop**: metrics and logs are lost; sessions, gotchas, and assessments survive on disk.

See [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) for the full observability guide, detailed metrics interpretation, session analytics, data file forensics, and corrupt JSONL detection.

---

## Repo layout

```
src/
  Total.Recall/                 ← main project (MCP server + scanner + report CLI)
    Infrastructure/             ← shared helpers (JSON, stores, logging, metrics)
    Tools/                      ← MCP tool entry points (one class per tool)
    Scanners/                   ← assembly / coverage / test scanners
    Models/                     ← record types serialised to JSONL
    Reporting/                  ← report CLI sub-commands + table renderer
  Total.Recall.Analyzers/       ← Roslyn analyzer (TR0001 duplicate-helper rule)
tests/
  Total.Recall.Tests/           ← xUnit tests for main project
  Total.Recall.Analyzers.Tests/ ← xUnit tests for the analyzer
docs/                           ← ADRs, integration guide, tool reference, demo
data/                           ← gitignored — scanner output lands here per-namespace
```

Start with [`AGENTS.md`](AGENTS.md) for the working rules, [`docs/DECISIONS.md`](docs/DECISIONS.md) for the architectural reasoning, then [`docs/QUICKSTART.md`](docs/QUICKSTART.md) for the end-to-end flow.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Before opening a PR, please skim [`docs/DECISIONS.md`](docs/DECISIONS.md) — the numbered Architecture Decisions list documents every load-bearing design choice and is the most common source of regressions when contradicted.

The default branch is `main`. CI runs on every push and PR.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the version history. Format follows
[Keep a Changelog](https://keepachangelog.com).

## License

[MIT](LICENSE) © 2026 Lyndon Swan

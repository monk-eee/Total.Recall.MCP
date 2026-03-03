# Total.Recall

**Persistent MCP memory server for AI agents doing .NET code coverage work.**

## The Problem

When an AI agent writes tests for a large .NET codebase, it burns 60–70% of its context re-discovering the same information every session: type constructors, mock patterns, coverage gaps, known pitfalls, and what's already tested. Over 30 sessions to reach a coverage target, this wastes ~450K tokens and ~22 hours of wall-clock time in re-discovery and build-fail-fix cycles.

## The Solution

Total.Recall converts ephemeral agent knowledge into durable, queryable data:

- **Scanners** extract type metadata, coverage gaps, and test inventories into JSONL files
- **20 MCP tools** let agents query this data instantly — one tool call replaces 10–15 file reads
- **Agents write back** gotchas, assessments, and session logs, creating a feedback loop that makes each session smarter than the last

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
| `get_metrics` | Server telemetry: tool calls, cache hit rates, lookup strategy distribution |

### Static Analysis

| Tool | Purpose |
|------|---------||
| `get_class_metrics` | Per-class coupling (Ca/Ce), instability, archetype, cluster, dependency lists |
| `get_dependency_graph` | Local subgraph for a class — deps, consumers, Mermaid diagram |
| `get_analysis_summary` | Architectural overview: hot interfaces, most coupled classes, clusters |

See [docs/TOOL_REFERENCE.md](docs/TOOL_REFERENCE.md) for complete parameter documentation.

## How It Works

VS Code spawns the Total.Recall process (via `.vscode/mcp.json`) when Copilot initializes. The server stays alive for the session, and Copilot auto-discovers all 20 tools over stdio JSON-RPC.

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
dotnet run --project src/Total.Recall -- scan ^
  --assembly "path/to/Server.dll" ^
  --coverage "path/to/coverage.cobertura.xml" ^
  --tests "path/to/UnitTest/" ^
  --source-root "path/to/Server/src" ^
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

You do **not** need to rescan for gotchas (appended live), mock recipes (manual edits), assessments (appended live), or sessions (appended live).

### VS Code MCP configuration

Create `.vscode/mcp.json` in your **target workspace** (the repo you're writing tests for):

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

### Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `TOTAL_RECALL_DATA` | `"data"` | Root data directory containing namespace subdirectories |
| `TOTAL_RECALL_NAMESPACE` | `"default"` | Default namespace subdirectory under data root |
| `TOTAL_RECALL_LOG_LEVEL` | `"info"` | Log verbosity: `debug`, `info`, `warn`, `error`, `quiet` |
| `TOTAL_RECALL_SOURCE_ROOT` | (none) | Override source root for `get_source_snippet` |

## Documentation

| Document | Purpose |
|----------|---------|
| [QUICKSTART.md](docs/QUICKSTART.md) | Step-by-step setup guide |
| [TOOL_REFERENCE.md](docs/TOOL_REFERENCE.md) | Complete parameter docs for all 20 tools |
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

**Logs** go to stderr (never stdout — that's the JSON-RPC transport). In VS Code: **View → Output → select "Total.Recall"**. From a terminal: `dotnet run --project src/Total.Recall 2> total-recall.log`

**Metrics** are queried via the `get_metrics` tool. Key signals: `cache.hitRate` > 90% is healthy; `lookup.exact` dominating lookups means agents are using precise names; `typeindex.rebuilds` ≤ 1 means the index stays warm.

**Sessions** persist to `sessions.jsonl` and survive restarts. Query with `get_sessions` for aggregate analytics (tokens/test, success rate, coverage deltas, plateau detection).

### Common problems

| Problem | Check | Fix |
|---------|-------|-----|
| Server won't start | `dotnet run --project src/Total.Recall` from terminal | Fix build errors; verify `.vscode/mcp.json` paths; restart VS Code |
| Tools return empty results | Startup log shows `✗` for data files | Run scanner: `dotnet run -- scan --assembly ... --namespace ...` |
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

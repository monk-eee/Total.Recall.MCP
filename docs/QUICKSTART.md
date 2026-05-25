# Total.Recall v2 — Quick Start Guide

## First 5 Minutes

This is the fast path for anyone with a built .NET repo and a coverage XML
already on disk. The five steps below take you from "never heard of it" to
"Copilot is using Total.Recall".

```bash
# 1. Install (once, globally)
dotnet tool install -g TotalRecall.Mcp --version 2.4.0-preview.1

# 2. From inside your target repo
cd path/to/your-repo

# 3. Auto-discover layout and write config
total-recall init .
# Reads your .csproj tree, locates the newest .dll under bin/, the newest
# coverage.cobertura.xml under TestResults/, and the matching test project.
# Writes config.json AND prints a ready-to-paste .vscode/mcp.json block.

# 4. Run the scan command it printed (also auto-generated, copy & paste)
total-recall scan --assembly ... --coverage ... --tests ... --enrich

# 5. Paste the printed JSON block into .vscode/mcp.json, restart VS Code,
#    open Copilot agent chat, and type:
#       "get testable targets, top 5"
```

If anything looks off, run `total-recall doctor` — it prints env vars,
data root status, per-namespace data file counts, and validates the paths
in `config.json` are still on disk.

The rest of this document is the long-form reference: full prerequisites,
option B (build from source), per-step troubleshooting, and CI patterns.

---

## Prerequisites

- .NET 8.0 SDK (8.0.400+)
- VS Code with GitHub Copilot (agent mode)
- The target repo built (assembly DLL must exist)
- Coverage report generated (`dotnet test --collect:"XPlat Code Coverage"`)

## 1. Install Total.Recall

### Option A — Install the global tool (recommended)

```bash
dotnet tool install -g TotalRecall.Mcp --version 2.4.0-preview.1
```

This puts a single command, `total-recall`, on your `PATH`. It is the
MCP server (default), the scanner (`total-recall scan`), and the report
reader (`total-recall report`) in one binary.

To update or remove later:

```bash
dotnet tool update    -g TotalRecall.Mcp
dotnet tool uninstall -g TotalRecall.Mcp
```

### Option B — Build from source

```bash
git clone https://github.com/monk-eee/Total.Recall.MCP
cd Total.Recall.MCP
dotnet build src/Total.Recall/Total.Recall.csproj
```

When building from source, replace every `total-recall <args>` below with
`dotnet run --project src/Total.Recall/Total.Recall.csproj -- <args>`.

## 2. Scan Your Target Repo

### Full scan (recommended for first run)

```bash
total-recall scan ^
  --assembly "C:\path\to\YourProject.dll" ^
  --coverage "C:\path\to\coverage.cobertura.xml" ^
  --tests "C:\path\to\YourProject.Tests" ^
  --source-root "C:\path\to\your-repo\src" ^
  --namespace myproject ^
  --enrich
```

### What each flag does

| Flag | Required | Description |
|------|----------|-------------|
| `--assembly` | no* | Path to target .NET assembly (.dll) — builds type registry |
| `--coverage` | no* | Path to Cobertura XML coverage report — builds coverage gaps |
| `--tests` | no* | Path to test project directory — builds test inventory |
| `--source-root` | no | Path to target repo source root — enables `get_source_snippet` tool |
| `--namespace` | no | Namespace subdirectory under `TOTAL_RECALL_DATA` (default: env var) |
| `--output` | no | Override data output directory entirely |
| `--enrich` | no | Cross-reference coverage with type registry + test inventory |
| `--analyze` | no | Run static analysis: dependency graph, coupling metrics, clusters |
| `--watch` | no | Watch mode: auto re-scan on file changes (Ctrl+C to stop) |
| `--help` | — | Print usage |

\* At least one of `--assembly`, `--coverage`, `--tests`, or `--enrich` is required.

### Example output

```
Total.Recall Scanner v2 — output: C:\...\data\myproject
  Scanning assembly... ✓ type-registry.jsonl — 1176 types
  Parsing coverage... ✓ coverage-gaps.jsonl — 539 classes
  Scanning tests... ✓ test-inventory.jsonl — 157 test files
  Enriching coverage data... ✓ 412 classes enriched with test counts + testability
  ✓ config.json updated
Done. [types:1176, coverage-classes:539, test-files:157, enriched:412]
```

### Incremental re-scans

After running tests with fresh coverage, you don't need a full scan — just update what changed:

```bash
# Re-scan coverage after a test run + enrich
total-recall scan ^
  --coverage "C:\path\to\new-coverage.cobertura.xml" ^
  --namespace myproject --enrich

# Just re-enrich existing data (no new scans)
total-recall scan ^
  --namespace myproject --enrich
```

### Watch mode (recommended for active development)

Instead of running manual re-scans, use `--watch` to keep the scanner running and auto-rescan on file changes:

```bash
total-recall scan ^
  --assembly "C:\path\to\YourProject.dll" ^
  --coverage "C:\path\to\coverage.cobertura.xml" ^
  --tests "C:\path\to\YourProject.Tests" ^
  --namespace myproject ^
  --enrich --analyze --watch
```

This watches:
- **Assembly .dll** — re-scans type registry when you rebuild
- **Coverage .xml** — re-parses when `dotnet test` produces new results (auto-finds newest in TestResults/)
- **Test .cs files** — re-scans test inventory when tests are added or modified

Changes are debounced (1.5s) to handle rapid build events. Press **Ctrl+C** to stop.

**Example output:**
```
  👁 Watching 3 path(s) for changes (Ctrl+C to stop):
    • Assembly: C:\path\to\YourProject.dll
    • Coverage: C:\path\to\TestResults\*.xml
    • Tests:    C:\path\to\YourProject.Tests\**\*.cs

  [14:32:05] Changes detected — re-scanning...
    Parsing coverage... ✓ 539 classes
    Enriching... ✓ 412 classes
    Done. [coverage:539, enriched:412]
```

## 3. Inspect Telemetry from the Command Line (Optional)

Once agents have used the server for a few sessions, you can read the recorded telemetry without spinning up VS Code. The `report` sub-command dispatches to the same tool methods the MCP server exposes and prints JSON to stdout:

```bash
# Per-tool call counts + p50/p95 latency + average response bytes
total-recall report tool-stats --ns myproject

# Recent behaviour cycles (re-query loops, context loss, oscillation)
total-recall report cycles --pattern re-query --last 50 --ns myproject

# Cross-model scorecard from sessions + tasks + evals
total-recall report scorecard --ns myproject

# Session history (last N sessions)
total-recall report sessions --last 10 --ns myproject

# Sessions × cycles × tasks efficiency summary
total-recall report efficiency --ns myproject

# Eval pass/fail rates by model
total-recall report leaderboard --ns myproject
```

Sub-commands: `tool-stats | efficiency | scorecard | cycles | sessions | leaderboard`.
Options: `--ns <name>` (or `--namespace`), `--last <int>`, `--pattern <string>`, `--format <json|table>`.

Default output is JSON. For a quick built-in text table, add `--format table`:

```bash
total-recall report tool-stats --ns myproject --format table
```

For more flexible shaping, pipe the JSON through `ConvertFrom-Json` (PowerShell) or `jq`:

```powershell
total-recall report tool-stats --ns myproject `
  | ConvertFrom-Json | Select-Object -ExpandProperty tools | Format-Table -AutoSize
```

While a report runs, telemetry recording is forced off so the report itself doesn't pollute `tool-calls.jsonl`.

## 4. Wire Up VS Code

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
        "TOTAL_RECALL_NAMESPACE": "myproject",
        "TOTAL_RECALL_SOURCE_ROOT": "C:\\path\\to\\your-repo\\src",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_MODE": "passive"
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
        "TOTAL_RECALL_NAMESPACE": "myproject",
        "TOTAL_RECALL_SOURCE_ROOT": "C:\\path\\to\\your-repo\\src",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_MODE": "passive"
      }
    }
  }
}
```

Restart VS Code. The MCP server starts automatically when Copilot initializes.

> **Note:** `TOTAL_RECALL_SOURCE_ROOT` is optional. If you provided `--source-root` during scanning, the value is persisted in `config.json` and the server reads it automatically.

### Startup validation

On first launch the server pre-loads all JSONL data and builds O(1) lookup indexes:

```
[Total.Recall] data dir: C:\...\data\myproject
  ✓ type-registry: 1176 records (cached)
  ✓ coverage-gaps: 539 records (cached)
  ✓ test-inventory: 157 records (cached)
  ✓ gotchas: 70 records (cached)
  ✓ mock-recipes: 12 records (cached)
  ✓ sessions: 0 records (cached)
  ⚡ type index: 1176 entries (O(1) lookups ready)
```

## 5. Use the Tools

Total.Recall exposes **34 MCP tools**. Here's the recommended workflow for a coverage-uplift session:

### Step 1 — Pick targets

> "Get the top 5 testable targets with max 4 constructor params"

Uses `get_testable_targets` — cross-joins 6 data sources, pre-filters by DI complexity, and returns a scored list. **This replaces 4+ manual tool calls and manual reasoning.**

### Step 2 — Understand the code

> "Show me the source of OrderService.ProcessOrder"

Uses `get_source_snippet` — serves actual C# source from the target repo. **No more `read_file` calls to the target repo.**

### Step 3 — Generate scaffold

> "Generate a test scaffold for OrderService"

Uses `generate_test_scaffold` — produces a complete `.cs` test file with correct `using` statements, mock wiring, constructor setup, and `[Fact]` stubs for every uncovered method.

### Step 4 — Write tests

Fill in the scaffold stubs. Use the other tools as needed:

| Tool | When to use |
|------|-------------|
| `get_context` | Combined type + gotchas + tests + mocks for a type |
| `resolve_type` | Just need constructor/property signatures |
| `get_mock_recipe` | Pre-built Moq setup code for an interface |
| `get_gotchas` | Known pitfalls before testing a type |
| `add_gotcha` | Record a new pitfall discovered during testing |
| `get_test_inventory` | Check what's already tested before generating duplicates |
| `get_coverage_gaps` | ROI-ranked list of uncovered classes |
| `add_assessment` / `get_assessments` | Testability verdicts for classes |
| `get_source_snippet` | Read specific method implementations from target repo |
| `get_uncovered_methods` | Method-level ROI targets when class-level is exhausted |
| `get_stub_classes` | Trivially-testable zero-coverage classes |
| `get_class_metrics` | Static analysis: coupling, instability, archetype |
| `get_dependency_graph` | Visualize class dependency neighborhood (Mermaid) |
| `get_analysis_summary` | Architectural overview: hot interfaces, clusters |
| `learn_test_patterns` | Learn naming, assertion, mock conventions from existing tests |
| `get_gotcha_insights` | Cluster gotchas into patterns, generate Footguns docs |
| `refresh_coverage` | Re-parse Cobertura XML mid-session without full rescan |
| `get_metrics` | Server telemetry (cache hits, tool calls) |
| `start_task` / `end_task` / `log_task` | Bracket work into named tasks for telemetry attribution |
| `get_cycles` | Recent detected behaviour cycles (re-query / context-loss / oscillation) |
| `get_tool_call_stats` / `get_efficiency_report` / `get_model_scorecard` | Cross-session telemetry views |
| `get_next_challenge` / `submit_challenge` / `get_eval_leaderboard` | Deterministic eval harness for cross-model scoring |
| `report_context_reset` | Self-report a compaction so post-reset behaviour is attributed correctly |

### Step 5 — Log the session

> "Log this session: claude-sonnet-4-20250514, 850K prompt tokens, 320K completion tokens, attempted OrderService+UserController+PaymentGateway, all succeeded, 45 tests, coverage 25.66 to 38.2"

Uses `log_session` — persists outcomes for cross-session analytics. Call `get_sessions` in future sessions to see what worked.

## 6. Verify It's Working

In Copilot chat, try: `get testable targets with top 3`

You should see a scored list of classes. If it doesn't appear:

1. `.vscode/mcp.json` exists in the workspace root
2. VS Code was restarted after adding the file
3. `total-recall` is on your `PATH` (re-open the terminal after `dotnet tool install`)
4. `TOTAL_RECALL_DATA` env var points to a directory with `.jsonl` files
5. The namespace directory exists and contains data

## 7. Data File Locations

| File | Updated By | Description |
|------|-----------|-------------|
| `type-registry.jsonl` | `--assembly` scan | Every public/internal type in the target assembly |
| `coverage-gaps.jsonl` | `--coverage` scan | Uncovered lines/methods per class |
| `test-inventory.jsonl` | `--tests` scan | Existing test methods per class |
| `gotchas.jsonl` | `add_gotcha` tool + manual seeding | Type-specific traps and workarounds |
| `mock-recipes.jsonl` | Manual curation | Pre-built Moq setup code per interface |
| `assessments.jsonl` | `add_assessment` tool | Testability verdicts from agent analysis |
| `sessions.jsonl` | `log_session` tool | Session outcomes for cross-session learning |
| `tool-calls.jsonl` | Auto (every tool call) | Every MCP tool call: name, ns, sessionId, taskId, params summary, latency, response bytes |
| `tasks.jsonl` | `start_task` / `end_task` | Agent task bracketing — start/end, success/abandon, intent |
| `cycles.jsonl` | Auto (CycleDetector) | Detected behaviour cycles: re-query, context-loss, oscillation |
| `challenges.jsonl` | `get_next_challenge` / `submit_challenge` | Eval challenge problems offered to agents |
| `evals.jsonl` | `submit_challenge` (graded) | Eval scoring outcomes: pass/fail, score, breakdown |
| `config.json` | Scanner `--source-root` | Persisted scan config (source root, paths, timestamp) |

All files live under `$TOTAL_RECALL_DATA/{namespace}/`.

## 8. Integrate with Your Agent Workflow

Total.Recall is **standalone** — it doesn't depend on any specific skill or workflow. But it works best when the agent knows it exists.

See [INTEGRATION.md](INTEGRATION.md) for ready-to-copy templates for `.github/copilot-instructions.md` and `AGENTS.md` injection in your target repo. The integration guide is self-contained and explains everything a new user needs.

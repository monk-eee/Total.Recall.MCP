# Total.Recall — Troubleshooting & Observability Guide

Where logs go, what telemetry tracks, how sessions persist, and how to diagnose issues.

---

## The Three Observability Layers

Total.Recall has three distinct data streams, each serving a different purpose:

| Layer | Where | Persisted? | Purpose |
|-------|-------|-----------|---------|
| **Logs** | stderr (console) | No — gone when process exits | Startup diagnostics, error traces, operational events |
| **Metrics** | In-memory counters | No — resets on restart | Per-process tool call counts, cache hit rates, lookup strategy stats |
| **Sessions** | `sessions.jsonl` on disk | Yes — append-only | Cross-session learning: tokens, classes, coverage deltas, failure patterns |

---

## 1. Logs (stderr)

### Where they go

All logging uses `Console.Error.WriteLine()` via the `Log` class. This writes to **stderr**, which is critical — the MCP protocol uses stdout for JSON-RPC, so any stdout pollution would corrupt the transport.

When VS Code spawns the MCP server, stderr output appears in the **MCP Server Output** panel:

```
VS Code → View → Output → select "Total.Recall" from dropdown
```

If you're running the server manually from a terminal:

```bash
# stderr shows in the terminal alongside stdout
dotnet run --project src/Total.Recall/Total.Recall.csproj

# Or redirect stderr to a file for later review
dotnet run --project src/Total.Recall/Total.Recall.csproj 2> total-recall.log
```

### Log format

All lines are prefixed with `[Total.Recall]`:

```
[Total.Recall] starting Total.Recall
[Total.Recall]   PID: 12345
[Total.Recall]   args: []
[Total.Recall]   cwd: C:\Users\lyndonswan\Repos\Total.Recall
[Total.Recall]   env TOTAL_RECALL_DATA: C:\Users\lyndonswan\Repos\Total.Recall\data
[Total.Recall]   env TOTAL_RECALL_NAMESPACE: linter
[Total.Recall] mode: MCP server (stdio)
[Total.Recall] default namespace: 'linter'
[Total.Recall] data dir: C:\...\data\linter
[Total.Recall]   ✓ type-registry: 1176 records (cached)
[Total.Recall]   ✓ coverage-gaps: 539 records (cached)
[Total.Recall]   ✓ test-inventory: 157 records (cached)
[Total.Recall]   ✓ gotchas: 70 records (cached)
[Total.Recall]   ✓ mock-recipes: 12 records (cached)
[Total.Recall]   ✓ sessions: 3 records (cached)
[Total.Recall]   ⚡ type index: 1176 entries (O(1) lookups ready)
[Total.Recall] telemetry tracking active (started 2026-02-27 10:30:01 UTC)
[Total.Recall] startup validation complete
[Total.Recall] starting host...
```

### Log levels

| Prefix | Meaning | Action |
|--------|---------|--------|
| `[Total.Recall]` | Informational | Normal operation — startup, cache loads |
| `[Total.Recall] WARN:` | Warning | Something degraded but server still works (missing data file, env var not set) |
| `[Total.Recall] ERROR:` | Error | Something failed — tool exception, crash, data corruption |

### What startup tells you

The startup sequence validates everything upfront. Read it top to bottom:

```
✓ = file loaded, data cached in memory
✗ = file missing or empty (tools for that data will return empty results)
WARN = something is off but not fatal
ERROR = load failed — check file permissions or JSON format
⚡ = type index built (enables O(1) lookups instead of linear scans)
```

**If you see `✗` for a data file**: run the scanner to populate it:

```bash
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan --help
```

**If you see `WARN: data dir does NOT exist`**: the `TOTAL_RECALL_DATA` path is wrong or the namespace subdirectory hasn't been created yet.

### Tool-level error logs

When a tool throws, the error is logged to stderr AND returned to the caller:

```
[Total.Recall] ERROR: [GetSourceSnippet] failed for 'FakeClass': FileNotFoundException: ...
```

The agent sees the error message as the tool's return value: `ERROR in GetSourceSnippet: FileNotFoundException: ...`

---

## 2. Metrics (In-Memory Telemetry)

### What it tracks

The `Metrics` class maintains `ConcurrentDictionary<string, long>` counters. Thread-safe, zero-allocation increment path. **Resets to zero when the server process restarts.**

Counter categories:

| Category | Counters | What they tell you |
|----------|----------|-------------------|
| **Tool calls** | `tool.resolve_type`, `tool.get_context`, `tool.get_coverage_gaps`, `tool.get_gotchas`, `tool.add_gotcha`, `tool.get_mock_recipe`, `tool.get_test_inventory`, `tool.add_assessment`, `tool.get_assessments`, `tool.get_metrics`, `tool.get_testable_targets`, `tool.get_source_snippet`, `tool.generate_test_scaffold`, `tool.log_session`, `tool.get_sessions` | Which tools agents actually use (and which they don't) |
| **Cache** | `cache.hit`, `cache.miss`, `cache.reload` | How well the in-memory cache is working. High miss rate = files changing frequently |
| **Type index** | `typeindex.hit`, `typeindex.rebuild` | O(1) index effectiveness. Rebuilds happen when JSONL changes on disk |
| **Lookup strategy** | `lookup.exact`, `lookup.case_insensitive`, `lookup.contains`, `lookup.interface`, `lookup.namespace`, `lookup.miss` | How agents search for types — exact is best, contains/interface = fallback |

### How to query

Call the `get_metrics` tool from Copilot:

> "Show me the server metrics"

Or programmatically — the tool returns JSON:

```json
{
  "uptime": {
    "hours": 2.45,
    "minutes": 147.2,
    "startedUtc": "2026-02-27 10:30:01"
  },
  "totalToolCalls": 47,
  "cache": {
    "hits": 89,
    "misses": 7,
    "reloads": 2,
    "hitRate": "92.7%"
  },
  "typeIndex": {
    "hits": 23,
    "rebuilds": 1
  },
  "lookupStrategy": {
    "exact": 15,
    "caseInsensitive": 6,
    "contains": 2,
    "interface": 0,
    "namespace": 0,
    "miss": 0
  },
  "tools": {
    "tool.get_testable_targets": 5,
    "tool.get_source_snippet": 12,
    "tool.generate_test_scaffold": 4,
    "tool.resolve_type": 8,
    "tool.get_context": 6,
    "tool.get_metrics": 1
  }
}
```

### Making sense of metrics

**Healthy session**:
- `cache.hitRate` > 90% — data is being served from memory, not re-read from disk
- `typeindex.rebuilds` ≤ 1 — index built once and stays warm
- `lookup.exact` + `lookup.case_insensitive` > 80% of lookups — agents are using precise names
- `tool.get_testable_targets` called early — agents are using the v2 target selection workflow

**Warning signs**:
- `cache.hitRate` < 50% — something is modifying JSONL files while the server is running (another scanner instance?)
- `lookup.contains` or `lookup.miss` is high — agents are using vague type names. Improve copilot-instructions.md to push toward exact names
- `tool.get_source_snippet` frequently called but `get_testable_targets` never called — agent isn't following the recommended workflow
- `tool.log_session` = 0 at end of session — agent didn't log outcomes. Recommend adding to copilot-instructions.md
- `typeindex.rebuilds` > 5 — file is being rewritten frequently (scanner running concurrently?)

**Metrics are ephemeral.** They reset on process restart. For persistent tracking across sessions, use session logging (below).

---

## 3. Sessions (Persistent Cross-Session Data)

### Where sessions live

```
$TOTAL_RECALL_DATA/{namespace}/sessions.jsonl
```

e.g., `data/linter/sessions.jsonl`

Each line is one JSON object — one session record. Append-only, never modified.

### What a session record contains

```json
{
  "sessionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "startedUtc": "2026-02-27T10:30:00.0000000Z",
  "endedUtc": "2026-02-27T10:30:00.0000000Z",
  "model": "claude-sonnet-4-20250514",
  "promptTokens": 850000,
  "completionTokens": 320000,
  "totalTokens": 1170000,
  "classesAttempted": ["ContentBlock", "ArtifactSchema", "DocSet"],
  "classesSucceeded": ["ContentBlock", "ArtifactSchema"],
  "classesFailed": [
    { "class": "DocSet", "reason": "Heavy DI: 8 services required" }
  ],
  "testsGenerated": 45,
  "coverageBefore": 25.66,
  "coverageAfter": 38.2,
  "coverageDelta": 12.54,
  "gotchasDiscovered": 3,
  "assessmentsRecorded": 5,
  "notes": "ArtifactSchema cluster was high-yield"
}
```

### How to query

Call `get_sessions` from Copilot:

> "Show me the last 3 sessions"

Returns recent sessions + aggregate analytics:

```json
{
  "aggregates": {
    "totalSessions": 5,
    "totalTests": 247,
    "totalTokens": 5200000,
    "totalCoverageDelta": 28.44,
    "avgTokensPerTest": 21052,
    "avgTestsPerSession": 49.4,
    "avgCoverageDeltaPerSession": 5.69,
    "classSuccessRate": "78.6%",
    "totalClassesAttempted": 28,
    "totalClassesSucceeded": 22,
    "totalClassesFailed": 6
  },
  "topSuccessfulClasses": [...],
  "topFailedClasses": [...],
  "recentSessions": [...]
}
```

### Making sense of sessions

**Key aggregates to watch**:

| Metric | Good | Bad | What to do |
|--------|------|-----|------------|
| `avgTokensPerTest` | < 25,000 | > 50,000 | High = scaffolding not used, or complex classes. Push agent toward simpler targets |
| `classSuccessRate` | > 75% | < 50% | Low = targeting classes that are too complex. Tighten `maxCtorParams` filter |
| `avgCoverageDeltaPerSession` | > 3% | < 1% | Low = diminishing returns. Shift to Tier 2 strategies or denominator management |
| `avgTestsPerSession` | > 30 | < 10 | Low = too much time debugging. More scaffolding, fewer complex targets |

**Pattern detection**:
- If the same class appears in `topFailedClasses` across multiple sessions → add an assessment (`add_assessment` with verdict `skip` or `coupled`) so `get_testable_targets` stops recommending it
- If a class succeeds after previously failing → check what changed (gotcha added? different approach?)
- Rising `avgTokensPerTest` over sessions → easy targets exhausted, consider coverage ceiling

### Manual inspection

Sessions are plain JSONL — grep/parse with any tool:

```powershell
# All sessions sorted by coverage delta
Get-Content data/linter/sessions.jsonl |
  ForEach-Object { $_ | ConvertFrom-Json } |
  Sort-Object coverageDelta -Descending |
  Format-Table model, testsGenerated, coverageBefore, coverageAfter, coverageDelta

# Total tokens burned
Get-Content data/linter/sessions.jsonl |
  ForEach-Object { $_ | ConvertFrom-Json } |
  Measure-Object -Property totalTokens -Sum |
  Select-Object Sum

# Classes that always fail
Get-Content data/linter/sessions.jsonl |
  ForEach-Object { $_ | ConvertFrom-Json } |
  ForEach-Object { $_.classesFailed } |
  Group-Object class |
  Sort-Object Count -Descending |
  Format-Table Count, Name
```

---

## 4. Common Problems & Fixes

### Server won't start

**Symptom**: No output in MCP Output panel, tools not available.

| Check | Fix |
|-------|-----|
| `dotnet run --project src/Total.Recall/Total.Recall.csproj` works from terminal? | If not, fix build errors first: `dotnet build` |
| `.vscode/mcp.json` exists in target workspace root? | Create it — see [QUICKSTART.md](QUICKSTART.md) §3 |
| VS Code restarted after adding `mcp.json`? | Restart VS Code (not just reload window) |
| Absolute path in `mcp.json` correct? | Verify path separators: `\\` on Windows JSON |

### Server starts but tools return empty results

**Symptom**: `get_testable_targets` returns "No coverage gap data available."

| Check | Fix |
|-------|-----|
| Startup log shows `✗` for data files | Run the scanner: `dotnet run -- scan --help` |
| `TOTAL_RECALL_DATA` env var correct? | Check `.vscode/mcp.json` `env` section |
| `TOTAL_RECALL_NAMESPACE` matches scanned namespace? | Verify with `ls $TOTAL_RECALL_DATA/` — data must be in the right subdirectory |
| Data directory has `.jsonl` files? | `ls data/your-namespace/` should show type-registry.jsonl etc. |

### `get_source_snippet` says "Source root not configured"

**Symptom**: Tool returns fallback message instead of source code.

| Check | Fix |
|-------|-----|
| Source root set? | Either set `TOTAL_RECALL_SOURCE_ROOT` in `mcp.json` env, or re-run scanner with `--source-root` |
| `config.json` exists in data directory? | `cat data/your-namespace/config.json` — should have `sourceRoot` field |
| Source root path actually exists? | Verify the directory exists and contains `.cs` files |
| File paths in coverage data match source root? | Coverage XML uses relative paths (e.g., `Parsing\Models\DocSet.cs`) that get joined with source root |

### Type not found by `resolve_type`

**Symptom**: "No types found matching 'TypeName'."

| Check | Fix |
|-------|-----|
| Type exists in registry? | `Select-String "TypeName" data/your-namespace/type-registry.jsonl` |
| Using correct name? | Try partial match, or check namespace. `resolve_type` searches: exact → case-insensitive → contains → interface → namespace |
| Registry stale? | Re-scan assembly: `dotnet run -- scan --assembly path/to/dll --namespace your-ns` |

### Cache not working (high miss rate)

**Symptom**: `get_metrics` shows `cache.hitRate` below 50%.

| Check | Fix |
|-------|-----|
| Another process writing JSONL files? | Scanner running concurrently? Each write triggers cache invalidation on next read |
| Rapid `add_gotcha` / `log_session` calls? | Each append changes file timestamp → cache invalidates. Normal behavior for write-heavy sessions |
| File modification time jumping? | Antivirus or file sync tools (OneDrive, Dropbox) can touch files. Exclude data directory |

### Scanner fails

**Symptom**: `dotnet run -- scan ...` exits with error.

| Check | Fix |
|-------|-----|
| Assembly path exists? | Scanner validates paths upfront — read the error message |
| Assembly has dependencies? | `AssemblyScanner` uses `MetadataLoadContext` which needs reference DLLs in the same directory |
| Coverage XML valid? | Open in browser/editor — must be Cobertura format |
| Test directory exists? | Scanner walks `.cs` files looking for `[Fact]`/`[Theory]` attributes |

---

## 5. Data File Forensics

All data under `$TOTAL_RECALL_DATA/{namespace}/`:

| File | Format | Mutable? | Inspect with |
|------|--------|----------|-------------|
| `type-registry.jsonl` | JSONL | Replaced on scan | `Get-Content ... \| ConvertFrom-Json \| Select Name, Namespace` |
| `coverage-gaps.jsonl` | JSONL | Replaced on scan | `... \| Sort-Object uncoveredLines -Desc \| Select class, uncoveredLines -First 10` |
| `test-inventory.jsonl` | JSONL | Replaced on scan | `... \| Select class, testCount \| Sort testCount -Desc` |
| `gotchas.jsonl` | JSONL | Append-only | `... \| Group-Object type \| Sort Count -Desc` |
| `mock-recipes.jsonl` | JSONL | Manual edits | `... \| Select interface, namespace` |
| `assessments.jsonl` | JSONL | Append-only | `... \| Group-Object verdict` |
| `sessions.jsonl` | JSONL | Append-only | `... \| Select model, testsGenerated, coverageDelta` |
| `config.json` | JSON | Written by scanner | `Get-Content config.json \| ConvertFrom-Json` |

### Quick health check

```powershell
$ns = "linter"
$dir = "data/$ns"

# File sizes and record counts
Get-ChildItem "$dir/*.jsonl" | ForEach-Object {
    [PSCustomObject]@{
        File = $_.Name
        Size = "{0:N0} KB" -f ($_.Length / 1KB)
        Records = (Get-Content $_.FullName | Measure-Object).Count
    }
} | Format-Table -AutoSize
```

### Corrupt JSONL detection

```powershell
# Find lines that aren't valid JSON
$file = "data/linter/gotchas.jsonl"
$lineNum = 0
Get-Content $file | ForEach-Object {
    $lineNum++
    try { $_ | ConvertFrom-Json | Out-Null }
    catch { Write-Warning "Line $lineNum is invalid JSON: $_" }
}
```

---

## 6. Lifecycle Summary

```
Server start
  │
  ├── Log to stderr: PID, args, env vars, data dir
  ├── Pre-warm: load all JSONL → in-memory cache
  ├── Build type index (O(1) dictionary)
  ├── Start Metrics counters at zero
  │
  ▼
Server running (processing MCP tool calls)
  │
  ├── Every tool call: Metrics.Increment("tool.xxx")
  ├── Every cache read: Metrics.Increment("cache.hit" or "cache.miss")
  ├── Every type lookup: Metrics.Increment("lookup.exact" or fallback)  
  ├── Errors: Log.Error() to stderr + return error string to caller
  ├── Writes (add_gotcha, log_session, etc.): append to JSONL on disk
  │
  ▼
Session end (agent calls log_session)
  │
  ├── Session record appended to sessions.jsonl (persisted)
  │
  ▼
Server stops (VS Code closes, restart, crash)
  │
  ├── Metrics counters LOST (in-memory only)
  ├── Logs LOST (stderr only, unless redirected to file)
  ├── Sessions PRESERVED (on disk)
  ├── Gotchas PRESERVED (on disk)
  ├── Assessments PRESERVED (on disk)
  │
  ▼
Next server start → cycle repeats, caches rebuild from JSONL
```

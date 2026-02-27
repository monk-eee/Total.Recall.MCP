# Total.Recall

Persistent MCP memory server for AI-driven .NET code coverage uplift.

Eliminates 70% of context burn by providing queryable type metadata, mock recipes, coverage gaps, and gotchas across agent sessions.

## Tools

| Tool | Purpose |
|------|---------|
| `resolve_type` | Namespace + constructor + property lookup for any type |
| `get_mock_recipe` | Pre-built Moq setup code for common interfaces |
| `get_coverage_gaps` | ROI-ranked uncovered classes + methods from Cobertura XML |
| `get_gotchas` | Type-specific pitfalls accumulated across sessions |
| `get_test_inventory` | Existing test methods per class (prevent duplication) |
| `add_gotcha` | Record a new pitfall discovered during test generation |
| `get_context` | Combined: type + gotchas + tests + mock recipes in one call |

## Performance

- **Startup pre-warm**: All JSONL data loaded into memory on server start (~1.2MB, <1s)
- **O(1) type lookups**: Pre-built `Dictionary<string, TypeRecord>` index (exact + case-insensitive)
- **Singleton stores**: All tools share `StoreRegistry` singletons — no redundant file reads
- **Shared serializer options**: 3 static `JsonSerializerOptions` instances (STJ caches reflection metadata)
- **Cache invalidation**: File-change detection via `LastWriteTimeUtc` — auto-reloads on rescan

## How It Works

VS Code spawns the Total.Recall process (via `.vscode/mcp.json`) when Copilot initializes. The server stays alive for the session, and Copilot auto-discovers all 7 tools over stdio JSON-RPC.

When you (or the coverage-uplift skill) ask Copilot to write tests, it calls tools like `GetContext("SomeClass")` to get constructors, properties, interfaces, gotchas, existing tests, and mock recipes — all in one round-trip instead of reading 4-5 source files. Every tool call hits the in-memory cache (pre-warmed on startup), so responses are sub-millisecond.

### How this speeds up coverage

| Without MCP | With MCP |
|---|---|
| Read source to find constructors, properties, namespaces (~10-15 tool calls per type) | `GetContext("AuditEntry")` — 1 call, instant |
| Trial-and-error mock setups → build failures → fix cycles (3-5 per interface) | `GetMockRecipe("IContentBase")` — copy-paste working Moq code |
| No memory of past pitfalls → repeat the same mistakes | `GetGotchas("AuditEntry")` — known traps surfaced automatically |
| Accidentally re-write existing tests | `GetTestInventory("AuditEntry")` — see what's already covered |
| Guess which classes need tests most | `GetCoverageGaps(sortBy: "roi")` — ROI-ranked prioritization |

Over 30 generations to reach 60% coverage, this saves an estimated ~450K context tokens and ~22 hours of wall-clock time in re-discovery and build-fail-fix cycles.

### When to rescan

Rescan when the source data changes:

| Condition | Flag | Example |
|---|---|---|
| Rebuilt the target assembly (new/changed types) | `--assembly` | `dotnet run -- scan --assembly "path\to\Server.dll" --output "data\linter"` |
| New coverage run (ran tests with Cobertura collection) | `--coverage` | `dotnet run -- scan --coverage "path\to\coverage.cobertura.xml" --output "data\linter"` |
| Added/renamed/deleted test files | `--tests` | `dotnet run -- scan --tests "path\to\UnitTest" --output "data\linter"` |

You do **not** need to rescan for:
- **Gotchas** — appended live via `AddGotcha()` tool calls
- **Mock recipes** — manually curated, edit the JSONL directly
- **Server restart** — auto-reloads from disk if JSONL files changed (file-change detection via `LastWriteTimeUtc`)

## Usage

### Scan a target assembly

```bash
dotnet run --project src/Total.Recall -- scan ^
  --assembly "path/to/Server.dll" ^
  --coverage "path/to/coverage.cobertura.xml" ^
  --tests "path/to/UnitTest/" ^
  --output "data/linter"
```

### Run as MCP server (VS Code launches this)

```json
// .vscode/mcp.json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\Total.Recall.csproj"],
      "env": {
        "TOTAL_RECALL_DATA": "C:\\path\\to\\Total.Recall\\data\\linter"
      }
    }
  }
}
```

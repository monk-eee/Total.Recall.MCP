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

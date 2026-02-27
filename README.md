# Total.Recall

Persistent MCP memory server for AI-driven .NET code coverage uplift.

Eliminates 70% of context burn by providing queryable type metadata, mock recipes, coverage gaps, and gotchas across agent sessions.

## Tools

| Tool | Purpose |
|------|---------|
| `resolve_type` | Namespace + constructor + property lookup for any type |
| `get_mock_recipe` | Pre-built Moq setup code for common interfaces |
| `get_coverage_gaps` | Ranked uncovered classes + methods from Cobertura XML |
| `get_gotchas` | Type-specific pitfalls accumulated across sessions |
| `get_test_inventory` | Existing test methods per class (prevent duplication) |
| `add_gotcha` | Record a new pitfall discovered during test generation |

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

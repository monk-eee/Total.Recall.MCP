# Total.Recall — Quick Start Guide

## Prerequisites

- .NET 8.0 SDK (8.0.400+)
- VS Code with GitHub Copilot
- The target repo built (assembly DLL must exist)

## 1. Build Total.Recall

```bash
cd C:\Users\lyndonswan\Repos\Total.Recall
dotnet build src/Total.Recall/Total.Recall.csproj
```

## 2. Scan Your Target Repo

Run all three scanners in one command:

```bash
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --assembly "C:\Users\lyndonswan\Repos\Linter\src\LanguageServer\Server\bin\Debug\net8.0\win-x64\Server.dll" ^
  --coverage "C:\Users\lyndonswan\Repos\Linter\TestResults\<guid>\coverage.cobertura.xml" ^
  --tests "C:\Users\lyndonswan\Repos\Linter\src\LanguageServer\UnitTest" ^
  --output "C:\Users\lyndonswan\Repos\Total.Recall\data\linter"
```

Or scan individually:

```bash
# Assembly only (type registry)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --assembly "path\to\Server.dll" ^
  --output "data\linter"

# Coverage only
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --coverage "path\to\coverage.cobertura.xml" ^
  --output "data\linter"

# Tests only
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --tests "path\to\UnitTest" ^
  --output "data\linter"
```

**Output:**
```
Total.Recall Scanner — output: C:\...\data\linter
Scanning assembly... ✓ type-registry.jsonl — 1176 types
Parsing coverage... ✓ coverage-gaps.jsonl — 539 classes
Scanning tests... ✓ test-inventory.jsonl — 157 test files
Done.
```

## 3. Wire Up VS Code

Create `.vscode/mcp.json` in your target workspace (e.g., the Linter repo):

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\lyndonswan\\Repos\\Total.Recall\\src\\Total.Recall\\Total.Recall.csproj"
      ],
      "env": {
        "TOTAL_RECALL_DATA": "C:\\Users\\lyndonswan\\Repos\\Total.Recall\\data\\linter"
      }
    }
  }
}
```

Restart VS Code. The MCP server starts automatically when Copilot initializes.

## 4. Use the Tools

Once wired, Copilot sees 6 tools. Use them naturally in your prompts or they'll be auto-discovered:

### resolve_type
> "Resolve the type `ContentBlock` — I need its namespace, constructors, and properties"

Returns full type metadata from the registry. Supports partial name matching.

### get_mock_recipe
> "Get the mock recipe for `IJobOutputInstance`"

Returns pre-built Moq setup code with required usings and known gotchas.

### get_coverage_gaps
> "Show me the top 10 classes with the most uncovered lines"

Returns ranked list of classes by uncovered line count, with method-level detail.

### get_gotchas
> "What gotchas exist for `ContentRange`?"

Returns all known pitfalls for a type — constructor traps, enum quirks, namespace issues.

### add_gotcha
> "Record a gotcha: ExportOutput 4-string ctor with empty filename throws ArgumentException"

Persists a new gotcha to disk for future sessions.

### get_test_inventory
> "What tests already exist for `AuditEntry`?"

Returns existing test methods, files, and inferred method coverage.

## 5. Re-scan After Changes

After running tests with coverage, re-scan to update the data:

```bash
# Re-scan coverage (after dotnet test with coverage)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --coverage "C:\...\TestResults\<new-guid>\coverage.cobertura.xml" ^
  --output "data\linter"

# Re-scan tests (after adding new test files)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --tests "C:\...\UnitTest" ^
  --output "data\linter"

# Re-scan assembly (after adding new source types)
dotnet run --project src/Total.Recall/Total.Recall.csproj -- scan ^
  --assembly "C:\...\Server.dll" ^
  --output "data\linter"
```

## 6. Verify It's Working

In your Copilot chat, type: `@Total.Recall resolve AuditEntry`

You should see the tool invoked and return type metadata. If it doesn't appear, check:
1. `.vscode/mcp.json` exists in the workspace root
2. VS Code was restarted after adding the file
3. `dotnet run --project <path>` works from terminal
4. `TOTAL_RECALL_DATA` env var points to a directory with `.jsonl` files

## Data File Locations

| File | Path | Updated By |
|------|------|-----------|
| Type registry | `data/linter/type-registry.jsonl` | `--assembly` scan |
| Coverage gaps | `data/linter/coverage-gaps.jsonl` | `--coverage` scan |
| Test inventory | `data/linter/test-inventory.jsonl` | `--tests` scan |
| Gotchas | `data/linter/gotchas.jsonl` | `add_gotcha` tool + manual seeding |
| Mock recipes | `data/linter/mock-recipes.jsonl` | Manual curation |

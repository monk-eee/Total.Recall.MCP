# Total.Recall — Implementation Specification

## 1. Overview

**Total.Recall** is an MCP (Model Context Protocol) server that provides persistent, queryable memory for AI-driven code coverage work on large .NET repositories. It eliminates the 60-70% of context burned on re-discovering type metadata, mock patterns, and coverage gaps each generation.

### Problem Statement

| Metric | Per Generation | Over 30 Gens to 60% |
|--------|---------------|---------------------|
| Namespace discovery tool calls | 10-15 | 300-450 |
| Context tokens on re-discovery | ~15K | ~450K |
| Build-fail-fix cycles from type mismatches | 3-5 | 90-150 |
| Estimated wasted wall-clock hours | ~45 min | ~22 hours |

### Solution Components

| Component | Purpose | Trigger |
|-----------|---------|---------|
| **Type Registry** | Namespace + constructor + property lookup | Every test file written |
| **Mock Recipe Book** | Pre-built mock setups for common interfaces | Every mock instantiation |
| **Coverage Gaps** | Ranked uncovered classes + methods | Start of each generation |
| **Gotcha Database** | Type-specific pitfalls + workarounds | Before writing tests for a type |
| **Test Inventory** | Existing test methods per class | Before writing tests (avoid duplication) |

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│ VS Code (Linter workspace)                               │
│  ┌─────────────────────┐                                 │
│  │ GitHub Copilot Agent │───── tool calls (stdio) ──────┐│
│  └─────────────────────┘                                ││
│                                                          ││
│  .vscode/mcp.json                                        ││
│  { "Total.Recall": { command: "dotnet", args: [...] } }  ││
└──────────────────────────────────────────────────────────┘│
                                                            │
┌───────────────────────────────────────────────────────────┘
│
▼
┌─────────────────────────────────────────────────────────┐
│ Total.Recall MCP Server (stdio)                          │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ resolve_type  │  │ get_mock_    │  │ get_coverage_ │  │
│  │ + get_context │  │ recipe       │  │ gaps          │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬────────┘  │
│         │                 │                  │           │
│  ┌──────┴───────┐  ┌──────┴───────┐  ┌──────┴────────┐  │
│  │ get_gotchas  │  │ get_test_    │  │ add_gotcha    │  │
│  │              │  │ inventory    │  │               │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬────────┘  │
│         │                 │                  │           │
│         ▼                 ▼                  ▼           │
│  ┌──────────────────────────────────────────────────┐   │
│  │ StoreRegistry (singleton in-memory cache)         │   │
│  │  ┌─ TypeRegistry ─ Dict<name,TypeRecord> index    │   │
│  │  ├─ CoverageGaps   (file-change invalidation)    │   │
│  │  ├─ TestInventory   SharedJsonOptions (3 static)  │   │
│  │  ├─ Gotchas         RepoConfig (cached path)      │   │
│  │  └─ MockRecipes                                   │   │
│  └──────────────────────────────────────────────────┘   │
│         │                                                │
│         ▼                                                │
│  ┌──────────────────────────────────────────────────┐   │
│  │ Data Layer (JSONL files per repo)                 │   │
│  │  data/linter/type-registry.jsonl                  │   │
│  │  data/linter/mock-recipes.jsonl                   │   │
│  │  data/linter/coverage-gaps.jsonl                  │   │
│  │  data/linter/gotchas.jsonl                        │   │
│  │  data/linter/test-inventory.jsonl                 │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Terminology

| Term | Definition |
|------|-----------|
| **MCP** | Model Context Protocol — open standard for AI tool integration. Agent calls tools exposed by MCP servers via JSON-RPC over stdio. |
| **stdio transport** | MCP communication mode where VS Code spawns the server process, communicates via stdin/stdout JSON-RPC. No HTTP, no ports. |
| **JSONL** | JSON Lines — one JSON object per line. Efficient for grep, append, and streaming reads. |
| **Type Registry** | Reflection dump of every public/internal type in a target assembly: namespace, constructors, properties, interfaces, base type. |
| **Mock Recipe** | Pre-built C# code for mocking a specific interface, including required `.Setup()` chains and known gotchas. |
| **Gotcha** | A type-specific pitfall discovered during test generation — wrong namespace, missing constructor, unreachable code, API quirk. |
| **Scan** | One-time or periodic data generation: reflect assembly → type-registry.jsonl, parse Cobertura → coverage-gaps.jsonl, scan tests → test-inventory.jsonl. |

---

## 3. Repository Structure

```
C:\Users\lyndonswan\Repos\Total.Recall\
├── Total.Recall.sln
├── global.json                          # Pin to .NET 8.0
├── README.md
├── SPEC.md                              # This file
├── AGENTS.md                            # Agent working memory
├── .gitignore
│
├── src/
│   └── Total.Recall/
│       ├── Total.Recall.csproj          # Console app, stdio MCP server
│       ├── Program.cs                   # Entry point: MCP server registration
│       │
│       ├── Tools/                       # MCP tool implementations
│       │   ├── ResolveTypeTool.cs       # resolve_type
│       │   ├── MockRecipeTool.cs        # get_mock_recipe
│       │   ├── CoverageGapsTool.cs      # get_coverage_gaps
│       │   ├── GotchaTool.cs            # get_gotchas + add_gotcha
│       │   └── TestInventoryTool.cs     # get_test_inventory
│       │
│       ├── Scanners/                    # Data generation (CLI scan command)
│       │   ├── AssemblyScanner.cs       # Reflection → type-registry.jsonl
│       │   ├── CoberturaParser.cs       # Cobertura XML → coverage-gaps.jsonl
│       │   └── TestProjectScanner.cs    # Test .cs files → test-inventory.jsonl
│       │
│       ├── Models/                      # Shared data models
│       │   ├── TypeRecord.cs
│       │   ├── MockRecipe.cs
│       │   ├── CoverageGap.cs
│       │   ├── Gotcha.cs
│       │   └── TestInventoryEntry.cs
│       │
│       └── Infrastructure/              # JSONL I/O, config, caching
│           ├── JsonLineStore.cs         # Generic read/write/query for JSONL (file-change caching)
│           ├── RepoConfig.cs            # Resolve data path from env var (cached on first call)
│           ├── StoreRegistry.cs         # Singleton store instances + pre-built type index
│           └── SharedJsonOptions.cs     # 3 static JsonSerializerOptions (avoid per-call allocs)
│
├── data/                                # Per-repo data directories
│   └── linter/                          # Linter-specific data
│       ├── type-registry.jsonl          # Generated by scan
│       ├── mock-recipes.jsonl           # Manually curated + auto-generated
│       ├── coverage-gaps.jsonl          # Generated from Cobertura XML
│       ├── gotchas.jsonl                # Seeded from 26 gens, grows over time
│       └── test-inventory.jsonl         # Generated by scan
│
└── tests/
    └── Total.Recall.Tests/
        ├── Total.Recall.Tests.csproj
        ├── AssemblyScannerTests.cs
        ├── CoberturaParserTests.cs
        ├── TestProjectScannerTests.cs
        └── JsonLineStoreTests.cs
```

---

## 4. Data Models

### 4.1 TypeRecord (type-registry.jsonl)

```json
{
  "name": "ContentBlock",
  "namespace": "Server.Parsing.Models.Parsers.Output.Container.File.Content",
  "fullUsing": "using Server.Parsing.Models.Parsers.Output.Container.File.Content;",
  "constructors": [
    { "params": [] },
    { "params": ["int index", "CodeContentBlock code", "ContentParameters parms", "string tag", "string lang"] }
  ],
  "baseType": "ContentBlockBase",
  "interfaces": ["IContentBlock"],
  "isAbstract": false,
  "isStatic": false,
  "isInternal": false,
  "isInterface": false,
  "isEnum": false,
  "properties": [
    { "name": "CodeLines", "clrType": "List<ContentLine>", "hasSet": true, "hasInit": false },
    { "name": "ArtifactType", "clrType": "ArtifactEnum", "hasSet": true, "hasInit": false }
  ],
  "enumValues": null
}
```

For enum types, special handling:

```json
{
  "name": "ArtifactEnum",
  "namespace": "Server.Common",
  "fullUsing": "using Server.Common;",
  "constructors": [],
  "baseType": "Enum",
  "interfaces": [],
  "isAbstract": false,
  "isStatic": false,
  "isInternal": false,
  "isInterface": false,
  "isEnum": true,
  "properties": [],
  "enumValues": ["None", "Header", "Table", "CodeFence", "Link", "Note", "Image"]
}
```

### 4.2 MockRecipe (mock-recipes.jsonl)

```json
{
  "interface": "IJobOutputInstance",
  "namespace": "Server.Parsing.Models.Parsers.Output.Container.Interfaces",
  "requiredUsings": [
    "using Server.Parsing.Models.Parsers.Output.Container.Interfaces;",
    "using Server.Parsing.Models.Parsers.Output.Container.File.Content.Interfaces;"
  ],
  "recipe": "var mockJobOutput = new Mock<IJobOutputInstance>();\nvar mockFromFile = new Mock<IContentBase>();\nvar mockRepo = new Mock<IRepoBase>();\nmockFromFile.Setup(f => f.Repository).Returns(mockRepo.Object);\nmockFromFile.Setup(f => f.RepoFileName).Returns(\"test.md\");\nmockJobOutput.Setup(j => j.FromFile).Returns(mockFromFile.Object);\nmockJobOutput.Setup(j => j.Repository).Returns(mockRepo.Object);",
  "gotchas": [
    "IJobOutputInstance.FromFile returns IContentBase (interface), NOT FileToken",
    "Must set up Repository on BOTH mockJobOutput and mockFromFile"
  ],
  "usedByClasses": ["AuditEntry", "ExportOutput", "ContentDataContainer", "DiagnosticChangeEvent"]
}
```

### 4.3 CoverageGap (coverage-gaps.jsonl)

```json
{
  "class": "ContentBlock",
  "namespace": "Server.Parsing.Models.Parsers.Output.Container.File.Content",
  "file": "Parsing/Models/Parsers/Output/Container/File/Content/ContentBlock.cs",
  "totalLines": 412,
  "coveredLines": 156,
  "uncoveredLines": 256,
  "coveragePercent": 37.86,
  "uncoveredMethods": [
    { "name": "SetContentParameters", "startLine": 120, "endLine": 165, "uncoveredLines": 45 },
    { "name": "BuildBlocks", "startLine": 230, "endLine": 268, "uncoveredLines": 38 }
  ],
  "existingTestCount": 18,
  "testability": "medium",
  "skipReason": null
}
```

### 4.4 Gotcha (gotchas.jsonl)

```json
{
  "type": "ContentRange",
  "category": "constructor",
  "gotcha": "Parameterless ctor leaves StartLine/EndLine null - copy ctor NREs. Initialize with new ContentLinePosition(0,0)",
  "discoveredInGen": 12,
  "date": "2026-02-28"
}
```

Categories: `constructor`, `namespace`, `enum`, `equality`, `mock`, `unreachable`, `property`, `inheritance`, `bug`, `static`

### 4.5 TestInventoryEntry (test-inventory.jsonl)

```json
{
  "class": "AuditEntry",
  "testFiles": ["AuditEntryTests.cs", "AuditEntryAdditionalTests.cs"],
  "testMethods": [
    "Ctor_Default_SetsDefaults",
    "Groups_ReturnsNonNullList",
    "IsDirectDependent_True_WhenDirectChild",
    "AllInnerRules_ReturnsNestedRules",
    "SetPropertyValues_CopiesFromSource"
  ],
  "testCount": 45,
  "inferredCoveredMethods": ["Ctor", "Groups", "IsDirectDependent", "AllInnerRules", "SetPropertyValues"]
}
```

---

## 5. Tool Specifications

### Tool 1: `resolve_type`

| Field | Value |
|-------|-------|
| **MCP Name** | `resolve_type` |
| **Description** | Resolve a .NET type name to its full namespace, constructors, properties, base type, and interfaces. Supports partial name matching. |
| **Input** | `typeName` (string, required) — Exact or partial class/interface/enum name |
| **Output** | JSON object: TypeRecord (or array if multiple matches) |
| **Data Source** | `type-registry.jsonl` |
| **Search Strategy** | Exact match first → case-insensitive contains → interface name match |

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Load type registry | `JsonLineStore<TypeRecord>.LoadAll()` |
| 2 | Exact name match | `Where(t => t.Name == typeName)` |
| 3 | If no exact match, fuzzy | `Where(t => t.Name.Contains(typeName, OrdinalIgnoreCase))` |
| 4 | Return matches | JSON array, max 5 results |

**Effort**: Low

---

### Tool 2: `get_mock_recipe`

| Field | Value |
|-------|-------|
| **MCP Name** | `get_mock_recipe` |
| **Description** | Get a pre-built Moq setup recipe for a .NET interface, including required usings and known gotchas. |
| **Input** | `interfaceName` (string, required) — Interface name (with or without `I` prefix) |
| **Output** | JSON object: MockRecipe |
| **Data Source** | `mock-recipes.jsonl` |

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Load recipes | `JsonLineStore<MockRecipe>.LoadAll()` |
| 2 | Normalize name | Ensure `I` prefix for search |
| 3 | Exact match | `Where(r => r.Interface == normalizedName)` |
| 4 | Return recipe | Full MockRecipe JSON including code, usings, gotchas |

**Effort**: Low

---

### Tool 3: `get_coverage_gaps`

| Field | Value |
|-------|-------|
| **MCP Name** | `get_coverage_gaps` |
| **Description** | Get the top N classes ranked by uncovered lines, filtered by testability. Includes uncovered method names and line ranges. |
| **Input** | `top` (int, optional, default: 20) — Max results. `skipUntestable` (bool, optional, default: true) — Filter out classes with `skipReason`. |
| **Output** | JSON array of CoverageGap, sorted by `uncoveredLines` descending |
| **Data Source** | `coverage-gaps.jsonl` |

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Load gaps | `JsonLineStore<CoverageGap>.LoadAll()` |
| 2 | Filter | If `skipUntestable`, exclude where `skipReason != null` |
| 3 | Sort | `OrderByDescending(g => g.UncoveredLines)` |
| 4 | Take top N | `.Take(top)` |
| 5 | Return | JSON array |

**Effort**: Low

---

### Tool 4: `get_gotchas` + `add_gotcha`

| Field | Value |
|-------|-------|
| **MCP Name** | `get_gotchas` |
| **Description** | Get all known pitfalls/gotchas for a specific type. Returns construction traps, namespace issues, enum quirks, and API surprises. |
| **Input** | `typeName` (string, required) — Type to look up gotchas for |
| **Output** | JSON array of Gotcha |
| **Data Source** | `gotchas.jsonl` |

| Field | Value |
|-------|-------|
| **MCP Name** | `add_gotcha` |
| **Description** | Record a new gotcha discovered during test generation. Persists to disk for future sessions. |
| **Input** | `typeName` (string), `category` (string), `gotcha` (string) |
| **Output** | Confirmation message |
| **Data Source** | Appends to `gotchas.jsonl` |

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Load gotchas | `JsonLineStore<Gotcha>.LoadAll()` |
| 2 | Filter by type | `Where(g => g.Type.Contains(typeName, OrdinalIgnoreCase))` |
| 3 | Return | JSON array |

For `add_gotcha`:

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Create Gotcha object | Set `date = DateTime.UtcNow.ToString("yyyy-MM-dd")` |
| 2 | Append to file | `JsonLineStore<Gotcha>.Append(gotcha)` |
| 3 | Return confirmation | `"Added gotcha for {typeName}"` |

**Effort**: Low

---

### Tool 5: `get_test_inventory`

| Field | Value |
|-------|-------|
| **MCP Name** | `get_test_inventory` |
| **Description** | Get existing test methods for a class, including which file they're in and inferred method coverage. Prevents test duplication. |
| **Input** | `className` (string, required) |
| **Output** | JSON object: TestInventoryEntry (or null if no tests exist) |
| **Data Source** | `test-inventory.jsonl` |

| Step | Action | Implementation |
|------|--------|----------------|
| 1 | Load inventory | `JsonLineStore<TestInventoryEntry>.LoadAll()` |
| 2 | Match class | `Where(t => t.Class.Contains(className, OrdinalIgnoreCase))` |
| 3 | Return | JSON array of matches |

**Effort**: Low

---

## 6. Scanner Specifications

### 6.1 AssemblyScanner

**Trigger**: `total-recall scan --assembly <path-to-dll> --output <data-dir>`

| Step | Action | Implementation | Dependencies |
|------|--------|----------------|--------------|
| 1 | Load assembly | `MetadataLoadContext` with `PathAssemblyResolver` for safe reflection-only load | Target assembly must be built |
| 2 | Get all types | `assembly.GetTypes()` filtering out compiler-generated, anonymous | — |
| 3 | For each type, extract | Name, Namespace, IsAbstract, IsStatic, IsInternal | — |
| 4 | Extract constructors | `type.GetConstructors()` → parameter name + type pairs | — |
| 5 | Extract properties | `type.GetProperties()` → name, CLR type, has set, has init | — |
| 6 | Extract interfaces | `type.GetInterfaces()` → interface names | — |
| 7 | Extract base type | `type.BaseType?.Name` | — |
| 8 | Handle enums | `type.GetFields(Static|Public)` for enum types (names excluding `value__`) | — |
| 9 | Write JSONL | One line per type → `type-registry.jsonl` | — |

**Critical implementation detail**: Use `MetadataLoadContext` (from `System.Reflection.MetadataLoadContext` NuGet) instead of `Assembly.LoadFrom`. This avoids loading the assembly into the execution context (which fails when dependencies like `Microsoft.Docs.Build.ContentParser` are missing). `MetadataLoadContext` does reflection-only load — perfect for metadata extraction.

**Dependency resolution for MetadataLoadContext**: The `PathAssemblyResolver` needs paths to all assemblies the target references. Collect these from the target DLL's directory (`*.dll`) plus the runtime directory (`typeof(object).Assembly.Location` parent). This handles most framework types and NuGet dependencies that copy-local.

**Effort**: Medium

### 6.2 CoberturaParser

**Trigger**: `total-recall scan --coverage <path-to-cobertura.xml> --output <data-dir>`

| Step | Action | Implementation | Dependencies |
|------|--------|----------------|--------------|
| 1 | Load XML | `XDocument.Load(coberturaPath)` | Coverage XML must exist |
| 2 | Parse packages/classes | XPath: `//package/classes/class` | — |
| 3 | For each class, extract | name, filename, line-rate, branch-rate | — |
| 4 | Parse methods | `class/methods/method` → name, line-rate | — |
| 5 | Parse uncovered lines | `class/lines/line[@hits='0']` → line numbers | — |
| 6 | Group uncovered lines into method ranges | Match line numbers to method start/end | — |
| 7 | Calculate uncovered line count | `lines.Count(l => l.hits == 0)` | — |
| 8 | Write JSONL | One line per class → `coverage-gaps.jsonl` | — |

**Effort**: Medium

### 6.3 TestProjectScanner

**Trigger**: `total-recall scan --tests <path-to-test-dir> --output <data-dir>`

| Step | Action | Implementation | Dependencies |
|------|--------|----------------|--------------|
| 1 | Find test files | `Directory.GetFiles(testDir, "*Tests*.cs", SearchOption.AllDirectories)` | — |
| 2 | For each file, regex scan | `[Fact]` and `[Theory]` method names | — |
| 3 | Extract class name | From filename: `AuditEntryTests.cs` → `AuditEntry` | — |
| 4 | Extract method names | Regex: `public\s+(?:async\s+)?(?:Task\s+\|void\s+)(\w+)\s*\(` after `[Fact]`/`[Theory]` | — |
| 5 | Infer covered methods | Heuristic: strip test suffixes, map to production method names | — |
| 6 | Group by class | Merge multiple test files per class | — |
| 7 | Write JSONL | One line per class → `test-inventory.jsonl` | — |

**Effort**: Low

---

## 7. Program.cs — MCP Server Entry Point

```
Program.cs flow:

1. Parse command line args
2. If args contain "scan" → run scanner CLI mode (no MCP)
3. Else → start stdio MCP server

MCP registration:
  builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()

  app.RunAsync()
```

The project uses a **dual-mode** pattern:
- **Server mode** (default, no args): Runs as stdio MCP server. VS Code launches this.
- **Scan mode** (`scan` subcommand): Runs scanners, writes JSONL, exits. You run this manually or in a build script.

This avoids needing two separate executables.

**Effort**: Low

---

## 8. Project Configuration

### Total.Recall.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Total.Recall</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="0.3.0-preview.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="System.Reflection.MetadataLoadContext" Version="8.0.1" />
  </ItemGroup>
</Project>
```

### global.json

```json
{
  "sdk": {
    "version": "8.0.400",
    "rollForward": "latestFeature"
  }
}
```

---

## 9. VS Code Integration

### Linter workspace `.vscode/mcp.json`

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

Once registered, Copilot sees tools like `Total.Recall_resolve_type`, `Total.Recall_get_mock_recipe`, etc. They appear in the tool selector alongside existing tools.

---

## 10. Seed Data Plan

### 10.1 Gotchas (seed from 26 generations)

Extract ~200 gotchas from the AGENTS.md `BUILD GOTCHAS` section and conversation summaries. Examples of what gets seeded:

| Type | Category | Gotcha |
|------|----------|--------|
| ContentRange | constructor | Parameterless ctor leaves StartLine/EndLine null |
| ContentRange | equality | partial record — auto-generates == value equality |
| OperationEnum | enum | File_FileName_Equals is value 0 (default), None is ~25 |
| OneOfComponents | unreachable | Filters List\<IUnitNode\> for non-IUnitNode — ALWAYS EMPTY |
| IJobOutputInstance | mock | FromFile returns IContentBase, NOT FileToken |
| Writable | static | Static class in IWriteOperation.cs line 269, not own file |
| FullTextDocument | constructor | Only parameterless ctor, set content via reflection |
| ArtifactEnum | enum | Has CodeFence, NOT Code |
| FormatterServices | constructor | GetUninitializedObject bypasses heavy constructors |
| ContentBlock | property | OnChanged sets private changed field — cannot assert |

### 10.2 Mock Recipes (curate from experience)

Seed ~20 recipes for the most commonly mocked interfaces:

| Interface | Complexity | Used By (# classes) |
|-----------|-----------|---------------------|
| IJobOutputInstance | High (3 chained mocks) | 15+ |
| IContentBase | Medium | 10+ |
| IRepoBase | Low | 8+ |
| IAuditRule | Medium (References HashSet) | 6+ |
| IUnitNode | Medium (As\<IBaseObject\>) | 5+ |
| IComponentNode | Medium (As\<IBaseObject\>) | 5+ |
| IMetadata | Low | 5+ |
| ILintIndex | Medium (FromAudit chain) | 4+ |
| IContentBlock | Low | 4+ |
| IContentParserOptions | Low | 3+ |
| ITestResult / ITestResultValue | Medium (Get/Set pattern) | 3+ |
| IExternalFileReference | Low | 3+ |
| IAuditDataContainer | Low | 3+ |
| ILogger | Low (Moq.ILogger) | 20+ |
| IWriteOperationBase | Medium (Combine default params) | 3+ |
| IBaseObject | Low | 5+ |
| IAuditable | Low | 3+ |
| ISchemaContainer | Low | 3+ |
| IDocSet | Low | 3+ |
| IOutputOptions | Low | 2+ |

---

## 11. Scan Workflow

```
Manual steps (run once after build, ~5 seconds total):

1. Build Linter:
   dotnet build src/LanguageServer/Server/Server.csproj

2. Scan assembly + coverage + tests:
   dotnet run --project C:\...\Total.Recall\src\Total.Recall -- scan
     --assembly "C:\...\Linter\src\LanguageServer\Server\bin\Debug\net8.0\win-x64\Server.dll"
     --coverage "C:\...\Linter\TestResults\<guid>\coverage.cobertura.xml"
     --tests "C:\...\Linter\src\LanguageServer\UnitTest"
     --output "C:\...\Total.Recall\data\linter"

Output:
   ✓ type-registry.jsonl — 554 types scanned
   ✓ coverage-gaps.jsonl — 312 classes parsed
   ✓ test-inventory.jsonl — 89 test files scanned
```

---

## 12. Effort Summary

| Task | Effort | Est. Hours | Dependencies |
|------|--------|-----------|--------------|
| Scaffold repo + csproj + global.json | Low | 0.5 | — |
| Program.cs (dual-mode entry point) | Low | 1 | — |
| Models (5 record types) | Low | 0.5 | — |
| JsonLineStore\<T\> (generic JSONL I/O) | Low | 1 | Models |
| RepoConfig (env var resolution) | Low | 0.5 | — |
| AssemblyScanner (reflection) | Medium | 3 | Models, JsonLineStore |
| CoberturaParser (XML) | Medium | 2 | Models, JsonLineStore |
| TestProjectScanner (regex) | Low | 1 | Models, JsonLineStore |
| resolve_type tool | Low | 1 | JsonLineStore, TypeRecord |
| get_mock_recipe tool | Low | 0.5 | JsonLineStore, MockRecipe |
| get_coverage_gaps tool | Low | 0.5 | JsonLineStore, CoverageGap |
| get_gotchas + add_gotcha tools | Low | 1 | JsonLineStore, Gotcha |
| get_test_inventory tool | Low | 0.5 | JsonLineStore, TestInventoryEntry |
| Seed gotchas (extract from history) | Low | 1 | Gotcha model |
| Seed mock recipes (curate + verify) | Medium | 2 | MockRecipe model |
| Unit tests | Medium | 2 | All scanners |
| VS Code integration + testing | Low | 1 | Everything |
| **TOTAL** | | **~18 hours** | |

### Dependency Matrix

```
Phase 1 (parallel):                    Phase 2 (parallel):         Phase 3:
  Models ─────────┐                      resolve_type ──┐
  JsonLineStore ──┤                      get_mock_recipe┤
  RepoConfig ─────┤── Phase 1 done ──→  get_gotchas ───┤── All tools ──→ Integration
  Program.cs ─────┘                      get_coverage   │                  + Testing
                                         get_test_inv ──┘
  AssemblyScanner ─┐
  CoberturaParser ─┤── Phase 1.5 done   Seed data ─────────────────────→ Integration
  TestScanner ─────┘
```

Phase 1 and Phase 1.5 can overlap. Phase 2 tools and seed data are independent. Integration is last.

---

## 13. Expected Impact

### Context savings per generation

| Activity | Before (tool calls) | After (tool calls) | Saved |
|----------|--------------------|--------------------|-------|
| Namespace discovery | 10-15 | 1 (resolve_type) | 90% |
| Constructor/property lookup | 5-8 | 0 (in resolve_type) | 100% |
| Mock setup investigation | 3-5 per mock | 1 (get_mock_recipe) | 75% |
| Gap analysis | 3-5 | 1 (get_coverage_gaps) | 80% |
| Test duplication check | 2-3 | 1 (get_test_inventory) | 65% |
| Gotcha re-discovery | 1-2 per type | 1 (get_gotchas) | 50% |
| **Total per generation** | **~30-40** | **~8-10** | **~70%** |

### Context token savings

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| Tokens per generation | ~50K | ~15K | -70% |
| Generations to 60% | ~30 | ~30 | same |
| Total tokens wasted | ~1.05M | ~300K | -750K saved |
| Wall-clock per gen | ~2 hrs | ~45 min | -63% |
| Total wall-clock remaining | ~60 hrs | ~22 hrs | **-38 hrs saved** |

### First-build success rate

| Metric | Before | After |
|--------|--------|-------|
| First build succeeds | ~30% | ~80% (estimated) |
| Avg fix cycles per gen | 3-5 | 0-1 |

---

## 14. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| MetadataLoadContext can't resolve all Linter dependencies | Medium | Scanner fails | Fallback: use `Assembly.LoadFrom` with all dependency DLLs on the load path |
| MCP SDK .NET 8 compatibility issues | Low | Won't compile | The NuGet README says .NET 8+ supported; template requires 10, SDK doesn't |
| Type registry stale after source changes | Medium | Wrong data returned | Add `--scan` to build script, or detect stale data (compare DLL timestamp vs JSONL timestamp) |
| Mock recipes become outdated | Low | Wrong mock code | Recipes are curated; add_gotcha catches new issues |
| JSONL files grow unbounded | Low | Slow reads | Type registry ~554 lines, gotchas ~200 lines — trivial for years. Add `--prune` later if needed |
| Agent doesn't use MCP tools | Medium | No benefit | Add MCP tool hints to coverage skill instructions. Use tool names in prompts explicitly. |

---

## 15. Future Enhancements (v2)

Not in scope for v1, but natural extensions:

| Enhancement | Value | Effort |
|-------------|-------|--------|
| `suggest_tests` tool — given a class, suggest which methods to test based on gaps + gotchas + testability | High | Medium |
| `validate_test` tool — given test code, check it against type registry for compile errors before build | High | High |
| Auto-scan on build (FileSystemWatcher on DLL) | Medium | Low |
| SSE/HTTP transport for remote use | Low | Medium |
| Multi-repo support (scan multiple assemblies) | Medium | Low |
| Publish to NuGet as a dotnet tool | Medium | Medium |

---

## 16. Performance Architecture

### Problem

MCP tool calls are stateless function invocations — each must return quickly since the agent is blocked waiting. The JSONL data files total ~1.2MB (1,176 types + 539 coverage classes + 157 test files + 70 gotchas + 12 mock recipes). Without caching, every tool call re-reads and re-parses the files from disk.

### Solution: Three-layer caching

```
Layer 1: RepoConfig              — cached data path (env var read once)
Layer 2: StoreRegistry            — singleton JsonLineStore<T> per data file
Layer 3: Pre-built type index     — Dictionary<string, TypeRecord> for O(1) name lookups
```

### Components

| Component | File | Purpose |
|-----------|------|---------|
| `StoreRegistry` | `Infrastructure/StoreRegistry.cs` | Static properties returning singleton `JsonLineStore<T>` instances. All tools share the same stores, so the in-memory cache persists across tool calls within the MCP server process lifecycle. |
| `SharedJsonOptions` | `Infrastructure/SharedJsonOptions.cs` | Three static `JsonSerializerOptions` instances (CamelCase, CamelCaseIndented, Indented). System.Text.Json caches reflection metadata inside the options object — reusing gives ~3x speedup on subsequent serializations. |
| `RepoConfig` cache | `Infrastructure/RepoConfig.cs` | `GetDataPath()` caches the resolved path after first call. Eliminates repeated `Environment.GetEnvironmentVariable()` + `Path.GetFullPath()` calls. |
| Type name index | `StoreRegistry.GetTypeIndex()` | Builds `Dictionary<string, TypeRecord>` (exact + case-insensitive) on first call. Turns `resolve_type` and `get_context` exact lookups from O(n) linear scan to O(1) dictionary lookup. Auto-invalidates when the underlying cache refreshes. |
| Cache-aware `Count()`/`HasData()` | `Infrastructure/JsonLineStore.cs` | When the in-memory cache is populated, returns `_cache.Count` instead of re-reading the file from disk. |
| Startup pre-warm | `Program.cs` | `ValidateDataOnStartup()` calls `LoadAll()` on every store and builds the type index before any tool call arrives. First tool call hits memory, not disk. |

### Cache Invalidation

`JsonLineStore<T>` tracks `File.GetLastWriteTimeUtc()`. If the file changes on disk (e.g., after a `scan` command or `add_gotcha`), the next `LoadAll()` call detects the timestamp change and reloads from disk. `Append()` and `WriteAll()` set `_cache = null` to force reload on next access.

The type name index (`StoreRegistry.GetTypeIndex()`) tracks the list reference from `LoadAll()`. When the cache refreshes (new list allocated), the index rebuilds automatically.

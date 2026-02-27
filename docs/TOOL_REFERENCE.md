# Total.Recall — Tool Reference

Complete reference for all 6 MCP tools exposed by the server.

---

## resolve_type

**Purpose**: Look up any .NET type from the scanned assembly by name.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Exact or partial type name |

**Search strategy** (in order):
1. Exact name match (case-sensitive)
2. Case-insensitive exact match
3. Partial match (contains, case-insensitive)
4. Interface name match (searches interface lists)

**Returns**: Up to 5 matching `TypeRecord` objects as JSON.

**Example input**: `"ContentBlock"`

**Example output** (abbreviated):
```json
[
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
]
```

**When to use**: Every time you need a namespace, constructor signature, or property list for a type you're about to use in test code.

---

## get_mock_recipe

**Purpose**: Get pre-built Moq setup code for an interface.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `interfaceName` | string | yes | Interface name (with or without `I` prefix) |

**Returns**: `MockRecipe` objects with C# code, required usings, and known gotchas.

**Example input**: `"IJobOutputInstance"`

**Example output** (abbreviated):
```json
[
  {
    "interface": "IJobOutputInstance",
    "namespace": "Server.Parsing.Models.Parsers.Output.Container.Interfaces",
    "requiredUsings": [
      "using Server.Parsing.Models.Parsers.Output.Container.Interfaces;",
      "using Server.Parsing.Models.Parsers.Output.Container.File.Content.Interfaces;",
      "using Moq;"
    ],
    "recipe": "var mockJobOutput = new Mock<IJobOutputInstance>();\nvar mockFromFile = new Mock<IContentBase>();\n...",
    "gotchas": [
      "FromFile returns IContentBase (interface), NOT FileToken",
      "Must set up Repository on BOTH mockJobOutput AND mockFromFile"
    ],
    "usedByClasses": ["AuditEntry", "ExportOutput", "ContentDataContainer"]
  }
]
```

**When to use**: Before writing any test that needs to mock an interface. Saves 3-5 tool calls of discovery.

---

## get_coverage_gaps

**Purpose**: Ranked list of classes with the most uncovered code.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 20 | Max results to return |
| `skipUntestable` | bool | no | true | Filter out classes with a skip reason |

**Returns**: Array of `CoverageGap` objects, sorted by uncovered lines descending.

**Example input**: `top=5, skipUntestable=true`

**Example output** (abbreviated):
```json
[
  {
    "class": "LinterExtension",
    "namespace": "Server.LanguageServerExtension",
    "file": "LanguageServerExtension/LinterExtension.cs",
    "totalLines": 2588,
    "coveredLines": 0,
    "uncoveredLines": 2588,
    "coveragePercent": 0.0,
    "uncoveredMethods": [
      { "name": "OnInitialize", "startLine": 45, "endLine": 120, "uncoveredLines": 75 }
    ],
    "existingTestCount": 0,
    "testability": "unknown",
    "skipReason": null
  }
]
```

**When to use**: Start of each test generation session to pick the highest-ROI target.

---

## get_gotchas

**Purpose**: Get all known pitfalls for a type before writing tests.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type name to look up |

**Returns**: Array of `Gotcha` objects with category, description, and discovery context.

**Example input**: `"ContentRange"`

**Example output**:
```json
[
  {
    "type": "ContentRange",
    "category": "constructor",
    "gotcha": "Parameterless ctor leaves StartLine/EndLine null - copy ctor NREs. Initialize with new ContentLinePosition(0,0)",
    "discoveredInGen": 12,
    "date": "2026-02-28"
  },
  {
    "type": "ContentRange",
    "category": "equality",
    "gotcha": "partial record — auto-generates == value equality. Asserting reference inequality fails.",
    "discoveredInGen": 5,
    "date": "2026-02-24"
  }
]
```

**Categories**: `constructor`, `namespace`, `enum`, `equality`, `mock`, `unreachable`, `property`, `inheritance`, `bug`, `static`

**When to use**: Before writing ANY tests for a type. Check gotchas first to avoid known traps.

---

## add_gotcha

**Purpose**: Record a new pitfall discovered during test generation.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type the gotcha applies to |
| `category` | string | yes | One of: constructor, namespace, enum, equality, mock, unreachable, property, inheritance, bug, static |
| `gotcha` | string | yes | Description of the pitfall |

**Returns**: Confirmation message.

**Example input**: `typeName="ExportOutput", category="constructor", gotcha="4-string ctor with empty filename throws ArgumentException from GetFileType"`

**When to use**: After discovering a new trap during test generation. Persists across sessions.

---

## get_test_inventory

**Purpose**: Check what tests already exist for a class.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class name to look up |

**Returns**: `TestInventoryEntry` with test files, method names, counts, and inferred coverage.

**Example input**: `"AuditEntry"`

**Example output** (abbreviated):
```json
[
  {
    "class": "AuditEntry",
    "testFiles": ["AuditEntryTests.cs"],
    "testMethods": [
      "RuleId_NoGroupPath_ReturnsRuleIdOnly",
      "DoNotLint_SuppressionDoNotLint_ReturnsTrue",
      "Groups_ExistingKey_ReturnsValue",
      "SetPropertyValues_CopiesSharedProperties"
    ],
    "testCount": 55,
    "inferredCoveredMethods": [
      "RuleId", "DoNotLint", "Groups", "SetPropertyValues", "FormatTitle"
    ]
  }
]
```

**When to use**: Before writing new tests — avoid duplicating existing coverage.

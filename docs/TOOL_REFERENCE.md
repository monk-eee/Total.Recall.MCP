# Total.Recall — Tool Reference

Complete reference for all 15 MCP tools exposed by the server.

All tools accept an optional `ns` parameter to target a specific namespace dataset.

---

## v2 Tools (Decision Engine)

### get_testable_targets

**Purpose**: Pre-scored, pre-filtered list of "here's your next N classes to test." Cross-joins 6 data sources so you don't have to.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `top` | int | no | 10 | Max results to return |
| `maxCtorParams` | int | no | 5 | Max constructor params to include (lower = simpler DI) |
| `maxTotalLines` | int | no | 500 | Max total lines in class |
| `excludeAbstract` | bool | no | true | Exclude abstract classes |
| `excludeAssessed` | bool | no | true | Exclude classes with "skip"/"coupled" assessments |
| `requireZeroTests` | bool | no | false | Only show classes with zero existing tests |

**Scoring formula**:
```
score = uncoveredLines
      × testabilityMultiplier (1.0 high, 0.7 medium, 0.3 low)
      × ctorSimplicity (1.0 for 0-2, 0.7 for 3-4, 0.3 for 5+)
      × mockCoverage (1.0 all mocked, 0.7 partial, 0.5 none)
      / (1 + existingTestCount)
      / (1 + gotchaCount × 0.1)
```

**Returns**: Array of `TestableTarget` objects with class, score, reason, uncoveredMethods, ctorParams, and all cross-joined metadata.

**When to use**: **First tool call of every coverage session.** Replaces 4+ manual tool calls and manual target selection reasoning.

---

### get_source_snippet

**Purpose**: Serve actual C# source code from the target repo. Eliminates "I have to read_file anyway."

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class name to get source for |
| `methodName` | string | no | Specific method to extract (returns just that method) |
| `maxLines` | int | no | Max lines to return (default: 200) |

**Resolution**: className → `coverage-gaps.jsonl` (file path) → source root + relative path → read file.

**Requires**: `TOTAL_RECALL_SOURCE_ROOT` env var or scanner `--source-root` (persisted to `config.json`).

**Returns**: JSON with `filePath`, `startLine`, `endLine`, `source` (string), `totalLines`.

**When to use**: After picking a target — read the implementation before writing tests. Replaces `read_file` calls to the target repo.

---

### generate_test_scaffold

**Purpose**: Complete C# test class skeleton combining type metadata + mock recipes + coverage gaps + gotchas.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class to generate test scaffold for |

**Generates**:
- All required `using` statements (type namespace + dependencies + Moq + Xunit)
- `Mock<T>` field declarations for every interface constructor parameter
- Default values for concrete params (`string` → `""`, `int` → `0`, etc.)
- Test class with constructor that wires mocks → SUT
- One `[Fact]` method stub per uncovered method
- `// ⚠️ GOTCHA:` comments for known pitfalls
- Mock recipe `Setup()` calls in constructor

**Returns**: Complete `.cs` file content as a string.

**When to use**: After `get_testable_targets` + `get_source_snippet` — scaffold the test file, then fill in assertions.

---

### log_session

**Purpose**: Write session outcomes for cross-session learning. Bidirectional — the agent writes data *back* to Total.Recall.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `model` | string | yes | — | LLM model used (e.g., "claude-sonnet-4-20250514") |
| `promptTokens` | long | no | 0 | Approximate prompt tokens consumed |
| `completionTokens` | long | no | 0 | Approximate completion tokens consumed |
| `classesAttempted` | string | no | "" | Comma-separated class names attempted |
| `classesSucceeded` | string | no | "" | Comma-separated class names that compiled + passed |
| `classesFailed` | string | no | "" | Comma-separated "ClassName:reason" pairs |
| `testsGenerated` | int | no | 0 | Total test methods generated |
| `coverageBefore` | double | no | 0 | Line coverage % before session |
| `coverageAfter` | double | no | 0 | Line coverage % after session |
| `gotchasDiscovered` | int | no | 0 | New gotchas added this session |
| `assessmentsRecorded` | int | no | 0 | New assessments added this session |
| `notes` | string | no | null | Free-form session notes |

**Returns**: Confirmation + session ID.

**When to use**: **End of every coverage session.** Captures what happened for future analytics.

---

### get_sessions

**Purpose**: Session history + aggregate analytics.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `last` | int | no | 5 | Number of recent sessions to return |

**Returns**: JSON with recent sessions + aggregates: total tests generated, total coverage delta, average tokens per test, success rate, top patterns.

**When to use**: Start of a session to see what worked previously, or for ROI measurement.

---

## v1 Tools (Lookup Index)

### resolve_type

**Purpose**: Look up any .NET type from the scanned assembly by name.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Exact or partial type name |

**Search strategy** (in order):
1. Exact name match (case-sensitive) — **O(1) dictionary lookup**
2. Case-insensitive exact match — **O(1) dictionary lookup**
3. Partial match (contains, case-insensitive) — linear scan (fallback only)
4. Interface name match (searches interface lists) — linear scan (fallback only)
5. Namespace search — linear scan (fallback only)

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
| `sortBy` | string | no | `"roi"` | Sort order: `"roi"` (default, ROI score), `"uncovered"` (uncovered lines desc), `"coverage"` (coverage % asc) |

**ROI formula**: `uncoveredLines * testabilityMultiplier / (1 + existingTestCount)`. Higher = more value from writing tests.

**Returns**: Array of `CoverageGap` objects with `roiScore`, sorted by chosen order.

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

---

## get_context

**Purpose**: Combined query — returns type metadata, gotchas, test inventory, and mock recipes in a single call.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `typeName` | string | yes | Type name to look up |

**Returns**: JSON object with `type` (TypeRecord), `gotchas` (Gotcha[]), `testInventory` (TestInventoryEntry), `mockRecipes` (MockRecipe[]), and `assessments` (Assessment[]).

**When to use**: When you need the full picture for a type before writing tests. Saves 4-5 individual tool calls.

---

## add_assessment

**Purpose**: Record a testability verdict for a class.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | yes | Class being assessed |
| `verdict` | string | yes | One of: `testable`, `skip`, `coupled`, `complex`, `trivial` |
| `reasoning` | string | yes | Why this verdict was given |
| `deps` | string | no | Comma-separated key dependencies |
| `cluster` | string | no | Related class cluster name |

**Returns**: Confirmation message.

**When to use**: After evaluating a class — persist the verdict so future sessions (and `get_testable_targets`) can skip already-assessed classes.

---

## get_assessments

**Purpose**: Retrieve previous testability assessments.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `className` | string | no | Filter by class name (returns all if omitted) |
| `verdict` | string | no | Filter by verdict |

**Returns**: Array of Assessment objects. Deduplicates by class name (last assessment wins).

**When to use**: Check if a class was already assessed before spending time evaluating it.

---

## get_metrics

**Purpose**: Server telemetry — tool call counts, cache hit/miss rates, type index stats.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| (none) | — | — | — |

**Returns**: JSON object with all counter values.

**When to use**: Debugging server performance or verifying tool usage.

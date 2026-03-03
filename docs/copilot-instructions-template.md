<!-- Total.Recall — copilot-instructions template
     Copy this entire file into your target repo at .github/copilot-instructions.md
     (or append it to an existing one). VS Code auto-injects it into every Copilot conversation.

     CUSTOMIZE: Replace placeholder paths/namespace below before committing.
-->

## Total.Recall MCP Server

This workspace has a Total.Recall MCP server connected (configured in `.vscode/mcp.json`).
It provides persistent, queryable memory for AI-driven .NET code coverage uplift — type
metadata, coverage gaps, mock recipes, gotchas, test inventories, and session history.

One tool call replaces 10–15 file reads. Agents write data back (gotchas, assessments,
session logs), creating a feedback loop that makes each session smarter than the last.

### Recommended Workflow

#### 1. Pick targets — `GetTestableTargets`

```
GetTestableTargets(top: 5, maxCtorParams: 4)
```

Returns pre-scored, pre-filtered targets ranked by ROI. Cross-joins coverage gaps, type
registry, test inventory, assessments, gotchas, and mock recipes into a composite score.
**This is the first call of every coverage session.**

#### 2. Read source — `GetSourceSnippet`

```
GetSourceSnippet(className: "ContentBlock", methodName: "BuildBlocks")
```

Serves actual C# source from the target repo. Use instead of `read_file` for any code
in the scanned assembly.

#### 3. Scaffold tests — `GenerateTestScaffold`

```
GenerateTestScaffold(className: "ContentBlock")
```

Generates a complete `.cs` test file: correct `using` statements, mock field declarations,
constructor setup, `[Fact]` stubs for every uncovered method, and inline gotcha comments.

#### 4. Fill in test logic

Use these tools as needed while writing test bodies:

| Tool | When to use |
|------|-------------|
| `GetContext(typeName)` | Everything about a type in one call: metadata + gotchas + tests + mocks + coverage + sessions |
| `ResolveType(typeName)` | Just constructors, properties, namespace |
| `GetMockRecipe(interfaceName)` | Pre-built Moq setup code (copy-paste ready) |
| `GetGotchas(typeName)` | Known pitfalls — **check BEFORE writing tests** |
| `AddGotcha(typeName, category, gotcha)` | Persist new pitfalls for future sessions |
| `GetTestInventory(className)` | Existing tests — avoid writing duplicates |
| `GetCoverageGaps(top: 10, sortBy: "roi")` | ROI-ranked uncovered classes |
| `AddAssessment(className, verdict, reasoning)` | Record testability verdict (`testable`, `coupled`, `skip`, `deferred`) |
| `GetAssessments(verdict: "skip")` | Query previous verdicts |

#### 5. Log the session — `LogSession`

```
LogSession(model: "claude-sonnet-4-20250514", promptTokens: 50000, completionTokens: 15000,
  classesAttempted: ["ContentBlock", "AuditEntry"], classesSucceeded: ["ContentBlock", "AuditEntry"],
  testsGenerated: 24, coverageBefore: 45.2, coverageAfter: 48.7,
  gotchasDiscovered: 2, assessmentsRecorded: 1)
```

**Last call of every session.** Call `GetSessions(last: 5)` in future sessions to see
what worked, what failed, and how coverage is trending.

### When to Fall Back to `read_file`

- Source root not configured → `GetSourceSnippet` unavailable
- Files outside the scanned assembly (configs, scripts, project files, `.csproj`)
- Runtime behavior investigation, exception flows, or debugging
- Code in referenced assemblies not included in the scan

### Performance Notes

- All data is pre-loaded into memory on server start (<2MB, <1s)
- Type lookups are O(1) via pre-built dictionary indexes
- Tool responses are sub-millisecond (memory reads, not disk I/O)
- Cache auto-invalidates when JSONL files change on disk (after re-scan)

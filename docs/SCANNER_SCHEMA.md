# Scanner JSONL Schema

This document is the **contract** between Total.Recall scanners (any language)
and the MCP server. The server reads JSONL files from
`data/<namespace>/` and does not care which scanner produced them — as long
as each file conforms to the schema below, every one of the 34 MCP tools works.

> **Audience.** Contributors writing a new scanner (Python, TypeScript, Go,
> Rust, …). The existing .NET scanner already conforms; this doc describes
> what *any* scanner must produce.

## Versioning

Every record in every JSONL store carries a `schemaVersion` field
(integer, default `1` when absent). The server treats absent or older
`schemaVersion` as version `1`. **Schema changes are additive.** Adding
a field is a non-breaking change. Removing or renaming a field bumps the
major version and the server gains a compat shim — never a hard break.

When you write a scanner, emit `"schemaVersion": 1` on every record. Newer
scanners may emit higher versions; older servers must continue to read
them by ignoring unknown fields (JSON serializers do this by default).

## File-level conventions

- **One JSON object per line.** No leading/trailing blank lines. No BOM. UTF-8.
- **No trailing newline after the last record** (matches the .NET writer).
- **Append-only.** Writers may rewrite the whole file (`type-registry`,
  `coverage-gaps`, `test-inventory` — these are derived from source) but
  must never partially mutate a record. Append-only stores (`gotchas`,
  `assessments`, `sessions`, `tool-calls`, `tasks`, `cycles`, `evals`,
  `challenges`) only ever receive new lines at the end.
- **Field naming.** `camelCase` everywhere. Discriminators are string
  enums (`kind: "class"`), not numeric.
- **Timestamps.** ISO 8601 UTC with `Z` suffix
  (`2026-05-26T10:42:31.123Z`).
- **Identifiers.** `name`, `namespace`, `filePath` are required on every
  symbol record. `filePath` is repo-relative, forward-slashed
  (`src/foo/bar.py`), so the data is portable across machines.

## Stores produced by scanners

A scanner is **required** to produce three stores to be useful end-to-end:

| Store | File | Required? | Source |
|---|---|---|---|
| Symbol registry | `type-registry.jsonl` | **Yes** | Source-tree / compiled artefact analysis |
| Coverage gaps | `coverage-gaps.jsonl` | Strongly recommended | Coverage report parser |
| Test inventory | `test-inventory.jsonl` | Strongly recommended | Test file scan |
| Mock recipes | `mock-recipes.jsonl` | Optional | Auto-derived from interfaces/protocols |
| Class metrics | `class-metrics.jsonl` | Optional | Static dependency analysis |
| Dependency graph | `dependency-graph.jsonl` | Optional | Static dependency analysis |
| Config | `config.json` | Strongly recommended | Persisted scan inputs |

Stores produced by the **server** (never the scanner) — listed here only so
scanners don't accidentally write them:

`gotchas.jsonl`, `assessments.jsonl`, `sessions.jsonl`, `tool-calls.jsonl`,
`tasks.jsonl`, `cycles.jsonl`, `challenges.jsonl`, `evals.jsonl`.

---

## 1. `type-registry.jsonl` — Symbol registry

The legacy name is kept for backward compatibility; conceptually this is
a **symbol** registry — classes, interfaces, functions, enums, type aliases.

### Required canonical fields

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | int | `1` |
| `name` | string | Symbol name (`UserService`, `parse_url`, `IUserRepo`). No leading dots. |
| `namespace` | string | Dotted path containing the symbol. .NET namespace, Python module, TS module path (slash → dot). Empty string if global. |
| `kind` | enum string | `class \| interface \| function \| enum \| struct \| type-alias \| module \| protocol` |
| `filePath` | string | Repo-relative, forward-slashed path to the defining file. |
| `fullUsing` | string | Language-idiomatic import statement (`using Foo;` / `from foo import Bar` / `import { Bar } from "foo"`). Empty if not applicable. |
| `isAbstract` | bool | Cannot be instantiated directly. |
| `isStatic` | bool | Static / module-level / no instance state. |
| `isInternal` | bool | Not part of public API (`internal` in C#, `_leading_underscore` convention in Python, non-`export` in TS). |
| `isInterface` | bool | True only for `kind: "interface" \| "protocol"`. |
| `isEnum` | bool | True only for `kind: "enum"`. |
| `constructors` | `Constructor[]` | Empty array for non-instantiable kinds. |
| `properties` | `Property[]` | Public-API instance state: C# properties, Python dataclass / instance attrs, TS class fields & interface members. |
| `baseType` | string? | Single base type fully-qualified name. Null for interfaces / functions / multi-inheritance languages (then use `interfaces`). |
| `interfaces` | `string[]` | Implemented interfaces / protocols / additional bases. Fully-qualified. |
| `enumValues` | `string[]?` | Only when `kind: "enum"`. |
| `lang` | `LangExt` | Language discriminator + language-specific extension fields. See below. |

### `Constructor`

```json
{
  "params": ["IUserRepo userRepo", "ILogger<UserService> logger"]
}
```

`params` is an array of fully-rendered, language-idiomatic parameter
declarations including type and name. Optional parameters render with
their default (`int max = 10`, `max: int = 10`, `max?: number`).

### `Property`

```json
{
  "name": "Id",
  "clrType": "Guid",
  "hasSet": false,
  "hasInit": true
}
```

`clrType` is the legacy field name; readers use it as "the type rendered in
this scanner's language" — so Python emits `"clrType": "int"`, TS emits
`"clrType": "number"`. `hasSet` is true if the property has a setter;
`hasInit` is true if it's settable only at construction (C# `init`, Python
frozen dataclass field, TS `readonly`).

### `lang` discriminator

```jsonc
// .NET
{ "kind": "dotnet", "isSealed": true, "isRecord": false, "genericArity": 0 }

// Python
{ "kind": "python", "isDataclass": true, "isFrozen": false,
  "isAbc": false, "isProtocol": false, "decorators": ["@dataclass"] }

// TypeScript
{ "kind": "typescript", "isExported": true, "isAmbient": false,
  "isReadonlyClass": false, "generics": ["T", "U extends string"] }
```

Servers ignore unknown `lang.kind` values gracefully — extension fields
inform tools (mock recipe generation, scaffold rendering) but never gate
core lookups.

### Minimal example (Python)

```json
{"schemaVersion":1,"name":"UserService","namespace":"app.services.users","kind":"class","filePath":"src/app/services/users.py","fullUsing":"from app.services.users import UserService","isAbstract":false,"isStatic":false,"isInternal":false,"isInterface":false,"isEnum":false,"constructors":[{"params":["repo: UserRepo","logger: logging.Logger"]}],"properties":[],"baseType":null,"interfaces":[],"lang":{"kind":"python","isDataclass":false,"isFrozen":false,"isAbc":false,"isProtocol":false,"decorators":[]}}
```

### Minimal example (TypeScript)

```json
{"schemaVersion":1,"name":"UserService","namespace":"app/services/users","kind":"class","filePath":"src/app/services/users.ts","fullUsing":"import { UserService } from \"./services/users\";","isAbstract":false,"isStatic":false,"isInternal":false,"isInterface":false,"isEnum":false,"constructors":[{"params":["repo: UserRepo","logger: Logger"]}],"properties":[],"baseType":null,"interfaces":[],"lang":{"kind":"typescript","isExported":true,"isAmbient":false,"isReadonlyClass":false,"generics":[]}}
```

---

## 2. `coverage-gaps.jsonl` — Coverage gaps

One record per class / module with uncovered lines.

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | int | `1` |
| `className` | string | Fully-qualified symbol name (matches `{namespace}.{name}` from `type-registry`). For module-level coverage use the module path. |
| `filePath` | string | Repo-relative source file. |
| `linesCovered` | int | |
| `linesTotal` | int | |
| `coveragePercent` | number | `0.0` to `100.0`. |
| `uncoveredMethods` | `UncoveredMethod[]` | |
| `existingTests` | int? | Populated by `--enrich` after cross-referencing test inventory. Null until then. |
| `testabilityScore` | number? | `0.0` to `1.0`. Populated by `--enrich`. |

### `UncoveredMethod`

```json
{
  "name": "ProcessOrder",
  "signature": "ProcessOrder(Order order, CancellationToken ct)",
  "uncoveredLines": [42, 43, 44, 51, 52],
  "totalLines": 18
}
```

### Coverage format inputs

Scanners SHOULD accept **at minimum Cobertura XML** as input (it's the
lingua franca: dotnet-coverage, coverage.py, Istanbul / nyc, JaCoCo, gocov
all emit it). They MAY accept additional formats via a Strategy registry
(`--coverage-format coveragepy-json | istanbul-json | lcov | cobertura`).
The on-disk JSONL is identical regardless of input format.

---

## 3. `test-inventory.jsonl` — Test inventory

One record per source-of-tests file.

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | int | `1` |
| `className` | string | Class under test (best-effort: filename minus `Tests`/`_test`/`.test` suffix). |
| `testFilePath` | string | Repo-relative path to the test file. |
| `testFramework` | string | `xunit \| nunit \| mstest \| pytest \| unittest \| jest \| vitest \| mocha`. |
| `tests` | `TestEntry[]` | |

### `TestEntry`

```json
{ "name": "ShouldRejectInvalidEmail", "kind": "fact", "lineNumber": 42 }
```

`kind` values: `fact` / `theory` (xUnit), `test` (NUnit / MSTest /
unittest / pytest), `it` / `describe` (Jest / Vitest / Mocha). The server
treats unknown kinds as `test`.

---

## 4. `mock-recipes.jsonl` — Mock recipes

Auto-emitted by the scanner for symbols where mocking is idiomatic
(C# interfaces, Python `Protocol` / `ABC`, TS `interface` / abstract
class). Hand-edited recipes (the more interesting kind) are appended
by the agent via `add_gotcha`-style tools — scanners must never overwrite
hand-edited entries on re-scan.

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | int | `1` |
| `interfaceName` | string | Fully-qualified symbol name. |
| `mockLibrary` | string | `moq \| nsubstitute \| fakeiteasy \| unittest.mock \| pytest-mock \| jest-mock \| vitest \| sinon`. |
| `setupCode` | string | Ready-to-paste snippet showing the canonical mock setup for this symbol. |
| `notes` | string? | Optional commentary. |
| `source` | enum string | `scanner \| agent`. `scanner` rows may be rewritten on re-scan; `agent` rows are never touched. |

### Conflict rule

On re-scan, scanners load existing `mock-recipes.jsonl`, keep all
`source: "agent"` rows verbatim, and replace `source: "scanner"` rows
in-place. This is the only file where a scanner is allowed to do a
read-merge-write cycle.

---

## 5. `class-metrics.jsonl` / `dependency-graph.jsonl` — Static analysis (optional)

Produced by `--analyze`. See the .NET `DependencyAnalyzer` for the
canonical fields:

- `ClassMetrics`: `className`, `afferent` (Ca), `efferent` (Ce),
  `instability` (`Ce / (Ca + Ce)`), `archetype`
  (`stable-abstraction | unstable-concrete | leaf | hub | …`),
  `cluster` (string label), `dependencies` (`string[]`),
  `consumers` (`string[]`).
- `DependencyEdge`: `from`, `to`, `kind` (`inherits | implements |
  composes | uses`).

A scanner MAY skip these files entirely on v1. The static-analysis MCP
tools (`get_class_metrics`, `get_dependency_graph`,
`get_analysis_summary`) degrade gracefully — they return "not analyzed"
when the file is absent.

---

## 6. `config.json` — Scan config

Single JSON object (not JSONL). Persisted at the namespace data dir root.

```json
{
  "schemaVersion": 1,
  "language": "dotnet",
  "sourceRoot": "C:\\repos\\Target\\src",
  "assemblyPath": "C:\\repos\\Target\\bin\\Debug\\net8.0\\Target.dll",
  "coveragePath": "C:\\repos\\Target\\TestResults\\…\\coverage.cobertura.xml",
  "testsPath": "C:\\repos\\Target\\tests\\Target.Tests",
  "testFramework": "xunit",
  "mockLibrary": "moq",
  "testNamespacePattern": "{Namespace}.Tests",
  "scannedUtc": "2026-05-26T10:42:31.123Z",
  "scannerVersion": "2.5.0-preview.1"
}
```

`language` is new in v2.5 — `dotnet | python | typescript | …`. Servers
that don't know a `language` value treat it as opaque.

Re-scanning **merges** with the existing `config.json` — fields not
re-supplied on the command line survive. The existing .NET `init` /
scan / `RepoConfig` helpers all do this; new scanners must too.

---

## Conformance test suite

`tests/conformance/` (added in commit 2) contains a tiny target project
per language plus the expected JSONL output (sorted, timestamps stripped).
A scanner that wants to call itself conformant runs its scanner against
the matching fixture and diffs the output against the golden. CI in each
scanner repo runs the same diff. **When this spec changes, all golden
files update in the same PR** — that is the lever that prevents drift.

## Tooling implications

The 34 MCP tools were written against .NET-shaped data, but their joins
only depend on canonical fields:

- `resolve_type`, `get_context`, `get_class_metrics`, `get_dependency_graph`
  → key on `name` + `namespace`.
- `get_testable_targets`, `get_uncovered_methods`, `get_stub_classes`,
  `get_coverage_gaps` → cross-join `type-registry` ↔ `coverage-gaps` ↔
  `test-inventory` by `name`/`className`.
- `get_mock_recipe` → key on `interfaceName`. Works for any symbol with
  `isInterface: true`.
- `generate_test_scaffold` → reads `constructors` + `properties`, renders
  using the configured `testFramework` / `mockLibrary`. Per-language
  scaffold rendering will land alongside the Python/TS scanners; the
  tool stays language-agnostic by dispatching on `lang.kind`.

A scanner that emits this schema unlocks all 34 tools. There is no
per-language tool registration.

---

See [`SCANNERS.md`](SCANNERS.md) for how to build a new scanner.
See [`DECISIONS.md`](DECISIONS.md) entry **66** for why the schema is
extracted into this document.

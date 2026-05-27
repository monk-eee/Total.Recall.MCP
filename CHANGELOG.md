# Changelog

All notable changes to Total.Recall are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions are reconstructed from `git log` — no formal git tags exist yet.
Dates reflect the commit date in the local repo.

## [Unreleased]

### Fixed
- **TypeScript scanner `scan` exits 0 on benign warnings (0.1.1)** —
  `total-recall-ts scan` previously returned exit code 1 whenever the
  source root contained no `.ts`/`.js` files OR `--tests` pointed at a
  path that didn't exist, even though the data files were written
  successfully (type-registry.jsonl emitted, config.json written). That
  broke CI bootstrap steps on fresh repos and tripped users whose test
  directory has a non-standard name. Scan now returns 0 on success,
  surfaces warnings on stderr, and reserves exit 2 for filesystem
  errors (missing `--source-root`, parseArgs failures). Regression
  tests in `tests/cli.test.ts` pin the new contract. npm release 0.1.1.
- **Python scanner `init` exits 0 on benign warnings (0.1.1)** —
  `total-recall-py init <repo>` previously returned exit code 1 whenever
  discovery raised any notes (missing `tests/`, missing `coverage.xml`),
  even though the command had succeeded (config.json written, mcp.json
  printed). That broke CI bootstrap steps on fresh repos that
  legitimately have no coverage yet, and surprised users following the
  QUICKSTART. Init now returns 0 on success and surfaces warnings only
  in the printed report. Exit 1 is reserved for validation errors
  (unsafe namespace) and exit 2 for filesystem errors. Regression tests
  in `tests/test_init.py` pin the new contract. PyPI release 0.1.1.

### Added
- **Python scanner `init` sub-command** (`total-recall-py init <repo>`) —
  auto-discovers a Python repo's source root (`src/<pkg>` or flat layout
  via `pyproject.toml` `[project].name`), tests directory (`tests/` /
  `test/`), newest Cobertura coverage XML (skipping junk dirs like
  `.venv`, `__pycache__`, `node_modules`), and suggests a sanitised
  namespace from the package name. Writes `<data>/<ns>/config.json`
  preserving any prior `lastScanUtc`, prints a copy-pasteable
  `.vscode/mcp.json` block with `command: "total-recall"` and the
  resolved env vars, and prints the exact `scan` command to run next.
  Exit codes: 0 success (warnings are informational only, surfaced in
  the report), 1 on validation error (unsafe namespace), 2 on
  filesystem error. Namespace validation rejects path separators and
  traversal segments.
- **Python scanner `--watch` mode** (`total-recall-py scan --watch`) —
  after the initial scan, polls every 1.5 s for `.py` mtime changes
  under the source root + tests dir + the coverage XML, debounces
  bursts (0.5 s) into a single rescan, and re-emits all three JSONL
  files. Zero new runtime deps — pure stdlib `os.stat`. Ctrl+C exits
  cleanly. Watcher is fully injectable (`sleep`, `snapshot`,
  `iterations`) so tests run deterministically without sleeping.
- **PyPI publish workflow** (`.github/workflows/publish-python-scanner.yml`)
  — manual `workflow_dispatch` with `target` input (`testpypi` /
  `pypi`) plus a `pyscan-v*` tag trigger that publishes to PyPI prod
  after verifying `pyproject.toml` version matches the tag.
  Builds sdist + wheel via `python -m build`, validates with `twine
  check`, uploads built artifacts to the GH run. Uses
  `PYPI_API_TOKEN` / `TEST_PYPI_API_TOKEN` repo secrets.
- **npm publish workflow** (`.github/workflows/publish-typescript-scanner.yml`)
  — manual `workflow_dispatch` with `dist_tag` input (`next` /
  `latest`) plus a `tsscan-v*` tag trigger that publishes with
  `--access public --tag latest` after verifying `package.json`
  version matches the tag. Uses `NPM_TOKEN` repo secret. Uploads a
  `npm pack` tarball as a GH run artifact for inspection.
- **TypeScript scanner skeleton** (`src/Total.Recall.Scanners.TypeScript/`) —
  Node 18+ / TypeScript 5+ sibling project published as npm
  `@total-recall/scan` with console script `total-recall-ts`. Uses the
  TypeScript compiler API (`ts.createSourceFile`) for single-file AST
  parsing — no project resolution, no type-checker, fast and predictable.
  Emits canonical `type-registry.jsonl` (class, interface, enum,
  function, type-alias), `coverage-gaps.jsonl` (Cobertura via
  `fast-xml-parser`), `test-inventory.jsonl` (vitest/jest test
  extraction), and `config.json`. Records carry `lang.kind:
  "typescript"` with the documented extension fields (`isExported`,
  `isAmbient`, `isReadonlyClass`, `generics`). Detects `extends` →
  `baseType`, `implements` → `interfaces[]`, `abstract` modifier,
  parameter-properties, and forward-slashes `filePath`. Conformance
  fixture at `tests/conformance/fixtures/typescript-sample/` exercises
  every shape. 22-test vitest suite green; `tsc -p` clean.
- **Python scanner skeleton** (`src/Total.Recall.Scanners.Python/`) —
  pure-stdlib AST walker that emits canonical `type-registry.jsonl`,
  `coverage-gaps.jsonl` (Cobertura XML), `test-inventory.jsonl` (pytest),
  and `config.json`. Ships as a sibling project, not part of the .NET
  pack. Console script `total-recall-py scan --source-root ... --tests
  ... --coverage ... --namespace ... --output ...`. Detects dataclasses,
  frozen dataclasses, `typing.Protocol`, `abc.ABC`, enums, abstract
  methods, leading-underscore-internal classes, and emits the
  `lang.kind: "python"` discriminator with the documented extension
  fields. PyPI package name `total-recall-scan-py`; install via
  `pipx install total-recall-scan-py`. 26-test pytest suite (registry +
  coverage + tests inventory + CLI) green; conformance fixture lives at
  `tests/conformance/fixtures/python-sample/` so the .NET / Python / TS
  scanners can be diffed against identical source.
- **`.gitignore` Python + Node entries** — `__pycache__/`, `*.py[cod]`,
  `.venv/`, `*.egg-info/`, `.pytest_cache/`, `.mypy_cache/`,
  `.ruff_cache/`, `node_modules/`, `*.tsbuildinfo`, etc.
- **Bug reports as a first-class persistent-knowledge surface** alongside
  gotchas and assessments. Three new MCP tools:
  - `report_bug` — file a class-scoped bug report. Fixed severity enum
    (`low` / `medium` / `high` / `critical`). Returns a stable
    `bug-{12-hex}` id.
  - `get_bugs` — query bugs by class (partial match) / severity / status.
    Defaults to `status=open`, sorted critical-first.
  - `update_bug_status` — transition `open` → `triaged` / `fixed` /
    `wontfix`. Append-only history; latest record per id wins.
  - Bumps server tool count 34 → 37.
- **`bugs.jsonl` data file** — eighth append-only JSONL store, registered
  through `RepoConfig.BugsPath` + `NamespaceStores.Bugs`. Pre-warmed on
  startup. Surfaced by `total-recall doctor` like every other store.
- **`get_context` now folds open bugs** into both `standard` and `full`
  depth responses, so agents see known-broken behaviour before authoring
  tests — no extra tool call needed.
- **`total-recall report bugs`** CLI sub-command (`--class`, `--severity`,
  `--status` options) for inspecting bugs without spinning up the MCP
  server.
- **Additive `TypeRecord` schema fields** — `schemaVersion` (int, default
  `1`), `kind` (string discriminator, default `"class"`), `lang`
  (optional language-specific block), and optional `filePath`. Existing
  pre-2.5 `type-registry.jsonl` files keep working unchanged — readers
  default missing fields. Rescan with 2.5 to populate them. See
  [docs/SCANNER_SCHEMA.md](docs/SCANNER_SCHEMA.md).
- **[docs/UPGRADE.md](docs/UPGRADE.md)** — dedicated upgrade guide
  covering the 2.4 → 2.5 path, doctor warnings, optional rescan,
  rollback, and recovery from partial writes. Linked from the README.

### Notes
- All on-disk changes in this release are strictly additive. No
  migration is required; `dotnet tool update -g TotalRecall.Mcp` is the
  whole upgrade procedure. Existing `data/<namespace>/*.jsonl` files
  keep working without rewrite.

## [2.5.0-preview.1] — 2026-05-25

Quality-of-life release focused on first-run UX and a scanner robustness fix.

### Added
- **`total-recall init <repo-path>`** — auto-discovers your target repo's
  production csproj, newest output DLL, newest `coverage.cobertura.xml`,
  test project, and source root. Writes (or merges with) `config.json` in
  the namespace data dir, then prints a ready-to-paste `.vscode/mcp.json`
  block and the matching `total-recall scan` command. Exit codes 0/1/2.
- **`total-recall doctor`** — prints env vars, data root status, per-namespace
  data file presence + record counts + last write times, and validates each
  `config.json`'s `sourceRoot` / `assemblyPath` / `coveragePath` / `testsPath`
  still resolve on disk. Missing core files (`type-registry.jsonl`,
  `coverage-gaps.jsonl`, `test-inventory.jsonl`) surface as warnings.
  Exit codes 0/1/2.
- **TTY guard on bare invocation** — `total-recall` with no args from an
  interactive terminal now prints help and exits 0 instead of silently
  entering MCP stdio server mode and appearing to hang. VS Code still pipes
  stdin so server mode triggers normally there.
- `--help` / `-h` / `help` print the same root help; `--version` / `-v` print
  just the version.
- **First 5 Minutes** quick-start section in `docs/QUICKSTART.md`.
- README polish: Recallmon mascot logo (`assets/recallmon.png`) and CI / NuGet
  version / NuGet downloads / MIT / .NET 8 badges below the H1.

### Fixed
- **Scanner crash on publish-style targets** — `AssemblyScanner` no longer
  throws `FileLoadException: Assembly with same name is already loaded` when
  the `--assembly` target directory ships its own copy of `mscorlib.dll` /
  `System.Private.CoreLib.dll` / `netstandard.dll` alongside the host runtime
  dir's copy. `PathAssemblyResolver` candidates are now deduplicated by
  `AssemblyName.Name`, target-dir copies winning over runtime-dir copies.
  Pinned by regression test
  `BuildResolverPaths_DuplicateIdentityAcrossDirs_DedupesByAssemblyName`.

## [2.4.0-preview.1] — 2026-05-25

First public release. Published to NuGet as
[`TotalRecall.Mcp`](https://www.nuget.org/packages/TotalRecall.Mcp)
and to GitHub at
[`monk-eee/Total.Recall.MCP`](https://github.com/monk-eee/Total.Recall.MCP).

### Packaging
- Ships as a [.NET global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools)
  on NuGet: `dotnet tool install -g TotalRecall.Mcp --version 2.4.0-preview.1`
  exposes a single `total-recall` command (MCP server / scanner / report reader).
- `.vscode/mcp.json` can now point at `"command": "total-recall"` directly
  instead of `dotnet run --project <path>`. Both forms supported.
- Pre-release scrub: replaced internal product / type names from the development
  sample target repo with neutral examples (`Invoice`, `OrderExport`, `MyApp.Billing`)
  across 24 doc and test files.

### Added
- Report CLI sub-command (`total-recall report …`) for telemetry inspection
  without spinning up the MCP server. Sub-commands: `tool-stats`, `efficiency`,
  `scorecard`, `cycles`, `sessions`, `leaderboard`. JSON by default; pipe through
  `ConvertFrom-Json | Format-Table` or `jq`.
- `--format table` option on `report` for a built-in fixed-width text table when
  piping through PowerShell or `jq` is inconvenient. Default remains `json`.
- `TR0001` Roslyn analyzer (`Total.Recall.Analyzers`) that fires a warning when
  the same non-public `static` method signature (`internal static` or
  `private static`) appears in two or more distinct files under `Tools/` or
  `Scanners/` — extraction to `Infrastructure/` is the fix.
- Doc-drift gate test for `TOOL_REFERENCE.md` — fails the suite when a tool's
  signature drifts from its documentation.
- `data/README.md` + `.gitkeep` so the data directory is committed (empty) and
  agents can find the layout convention.
- Community-health files: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/*`.
- `.editorconfig` for cross-IDE formatting consistency.
- `SECURITY.md` describing supported versions and disclosure flow.
- GitHub Actions CI workflow building + testing on Ubuntu and Windows.
- `docs/DECISIONS.md` — the full 62-entry Architecture Decisions list, moved
  out of `AGENTS.md` so the rules file stays focused.
- README "Seeing it in action" section with real terminal output from
  `scan` and `report` sub-commands.
- **Telemetry substrate**: every MCP tool call is intercepted by
  `Telemetry.Track(toolName, ns, params, handler)` and appended to
  `tool-calls.jsonl` with session ID, task ID, latency, and response bytes.
- **`TOTAL_RECALL_MODE` env var**: `off` (no recording), `passive` (default —
  record tool calls + cycles), `active-eval` (serve eval challenges via
  `get_next_challenge`).
- **Cycle detection**: `CycleDetector.Observe` flags three behaviour patterns —
  `re-query` (≥3 identical calls), `context-loss` (lookup calls with no
  intervening write after compaction), `oscillation` (≥3 distinct source-snippet
  targets in a 5-call window with no assessment between). Appended to
  `cycles.jsonl`; deduped per session.
- **Task bracketing**: `start_task` / `end_task` / `log_task` tools persist
  named units of agent work to `tasks.jsonl`. Auto-abandons prior task if
  agent forgets to close it.
- **Eval challenges**: `get_next_challenge` / `submit_challenge` /
  `get_eval_leaderboard`. `ChallengeGrader` is a deterministic 40 % required-tools
  + 20 % stayed-under-budget + 40 % output-correctness rubric. Pass threshold 0.7.
- **`report_context_reset`**: agent self-reports a compaction event; rotates
  `Telemetry.SessionId` so downstream analysis correctly attributes post-reset
  calls.
- **`get_tool_call_stats`, `get_efficiency_report`, `get_model_scorecard`** —
  read-side tools that aggregate the new telemetry into useful shapes.

### Changed
- Default branch renamed from `develop` to `main`.
- `AGENTS.md` "Architecture Decisions" section reduced to a one-paragraph pointer
  at `docs/DECISIONS.md`. Cross-references updated in `CONTRIBUTING.md`,
  `README.md`, `.github/pull_request_template.md`, and the feature-request
  issue template.

### Refactored
- Extract `MermaidId` and `AssessmentLookup` to `Infrastructure/` so callers
  in `Tools/` and `Scanners/` don't duplicate the logic. Triggered by `TR0001`.
- Broaden `TR0001` to cover `private static` duplicates after a sweep found
  three real instances (`SanitizeId`, `BuildLatestAssessments`,
  `TryGetAssessment`) that the `internal static`-only rule missed.
- Test scaffold renderer refactored: planner (`TestScaffoldPlanner`) +
  renderer separated, assertion rules pulled to a data table
  (`AssertionRules.s_prefixHints`) instead of an if-chain.
- OSS-prep: AGENTS.md prime directive section, mechanical enforcement rules,
  refactor discipline.

### Documentation
- Plain-language elevator pitch added to `README.md`.
- README "Install" section + global-tool-based `.vscode/mcp.json` snippet
  in both `README.md` and `docs/QUICKSTART.md`.
- `docs/TODO.md` restructured into "Now / Known bugs / Known duplicates /
  From log sweep / Done" discipline sections.

## [2.3.0] — 2026-03-04

### Fixed
- Data directory resolution unified through
  `RepoConfig.GetNamespacePath(ns, outputPath)`; the previous if-chain made
  `--output` and `--namespace` falsely mutually exclusive.
- Scanner now warns when its resolved output directory diverges from the path
  the MCP server would use (`TOTAL_RECALL_DATA`), so silent "where did my data
  go?" no longer happens.
- Watch mode (`--watch`) propagates `OperationCanceledException` cleanly so
  Ctrl+C always exits 0.

### Added
- Scan summary table printed at the end of every scan run, showing record
  counts per JSONL file in the output directory.
- `RepoConfig.ClearCache()` public API for tests and runtime env-var changes.

## [2.2.0] — 2026-03-04

### Fixed
- Seven correctness fixes across `get_testable_targets`, scanner enrichment,
  source-root resolution, scaffold generation, and session aggregation.
  Details in the v2.2.0 commit body.

### Added
- Multi-method source snippets: `get_source_snippet` accepts comma-separated
  method names and returns each method's source independently.
- Paginated assessment queries: `get_assessments` now supports `top` and `skip`
  with an envelope response.

## Earlier development — late February to early March 2026

This is the reconstructed pre-2.2.0 history. No version numbers were stamped
in the commit messages, so entries are grouped by commit rather than tag.

### Added
- **v2 decision-engine tools** (commit `38ecd2d`):
  `get_testable_targets`, `get_source_snippet`, `generate_test_scaffold`,
  `log_session`, `get_sessions`, `get_uncovered_methods`, `get_stub_classes`,
  `learn_test_patterns`, `get_gotcha_insights`, `refresh_coverage`.
- **Eight scoring + targeting features** (commit `1ad4e1b`):
  mockability-aware scoring, namespace-cluster coupling penalty, gotcha →
  interface scoring propagation, log-scaled uncoveredLines base,
  external-service-dependency penalty, heavily-tested-class cliff penalty,
  HasTestFile scoring bias, plateau detection. +42 unit tests.
- **Five feedback-loop features** (commit `12d52af`):
  negative feedback loop on `log_session`, assessment deduplication, gotcha
  insights clustering, mock-recipe usage examples enrichment, overload
  disambiguation in scaffold generation.
- **Assessments, telemetry, and namespace support** (commit `17ca968`):
  `add_assessment` / `get_assessments` tools, in-memory `Metrics` counters,
  `TOTAL_RECALL_NAMESPACE` env var, multi-namespace `StoreRegistry`.
- **Initial source + tests** (commits `fe42096`, `403cf62`, `c5f4457`):
  MCP server with v1 lookup tools (`resolve_type`, `get_context`,
  `get_mock_recipe`, `get_coverage_gaps`, `get_gotchas`, `add_gotcha`,
  `get_test_inventory`, `get_metrics`), JSONL stores, `MetadataLoadContext`
  scanner, Cobertura parser, and 151 unit tests at 92.22 % line coverage.

---

## Versioning notes

- `Total.Recall.csproj` carries the canonical version. As of [Unreleased],
  it reads `2.4.0` because no version bump has been made for the post-2.4.0
  additions yet.
- A formal `v2.4.0` git tag has not been created. When publishing, tag the
  commit that introduces a `[Released]` section with `git tag -a v2.X.Y`.
- New features should land in `[Unreleased]` and graduate to a numbered
  section on release.

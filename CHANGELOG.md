# Changelog

All notable changes to Total.Recall are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions are reconstructed from `git log` — no formal git tags exist yet.
Dates reflect the commit date in the local repo.

## [Unreleased]

_No unreleased changes yet._

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

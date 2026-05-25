# Total.Recall — TODO

Running list of known bugs, known duplicates, log-sweep findings, and
shipped-feature history. Agents append here per the AGENTS.md "Always
report bugs and failures" / "Code reuse" / log-sweep disciplines.

When adding an entry:
- Be specific: file path + symbol or test name, what was observed, impact.
- Use the most relevant section. Create a new section only if none fits.
- Move entries to `## Resolved` (with the fix commit hash) rather than deleting them.
- Keep this file linear and append-only — no nested sub-tasks.

---

## Known bugs

_Bugs noticed in passing during unrelated work. Each entry must record:
(1) what you observed, (2) where (file + symbol or test name),
(3) impact, (4) whether fixed in this run or left for later. See
AGENTS.md "Test-driven workflow (NON-NEGOTIABLE)"._

_(none currently tracked)_

---

## Known duplicates

_Helper code duplicated across `Tools/` and `Scanners/` that should live
in `Infrastructure/`. Each entry: pattern, files involved, proposed
`Infrastructure/` home. See AGENTS.md "Code reuse (NON-NEGOTIABLE)"._

_(none currently tracked)_

---

## From log sweep

_Recurring errors / warnings / silent failures surfaced by the
`log-sweep` skill. Each entry: date, log source, pattern, count, suspected
cause. See AGENTS.md "Always report bugs and failures"._

_(none currently tracked)_

---

## Backlog (nice-to-have)

_Non-urgent improvements that don't block shipping. Move to `## In progress`
when picked up._

- Roslyn analyzer to fail the build on duplicate `internal static` signatures
  across `Tools/` and `Scanners/` (per AGENTS.md "Mechanical enforcement").

---

## Shipped

### v2.4.0 — Agent evaluation harness (Cuts 1–6)

Six-cut rollout. All cuts pure-additive, no existing tools modified. See
AGENTS.md Architecture Decisions #53–#58 for design notes and the v2.4.0
commit for the diff.

- [x] **Cut 1 — Passive tool-call telemetry.** `Models/ToolCall.cs`,
  `Infrastructure/TelemetryConfig.cs`, `Infrastructure/Telemetry.cs`,
  `NamespaceStores.ToolCalls`, `Telemetry.Track` wrapping every public
  tool entry. Tests: `TelemetryTests`, `ToolCallStoreTests`.
- [x] **Cut 2 — Cycle detection.** `Models/CycleRecord.cs`,
  `Infrastructure/CycleDetector.cs` (re-query / context-loss /
  oscillation patterns), `NamespaceStores.Cycles`, `Tools/CyclesTool.cs`
  (`get_cycles`). Tests: `CycleDetectorTests`, `CyclesToolTests`.
- [x] **Cut 3 — Task bracketing.** `Models/TaskRecord.cs`,
  `NamespaceStores.Tasks`, `Tools/TaskTool.cs` (`start_task`, `end_task`,
  `log_task`), `Telemetry` stamps active task id on every `ToolCall`.
  Tests: `TaskToolTests`.
- [x] **Cut 4 — Model scorecard.** `Tools/ScorecardTool.cs`
  (`get_model_scorecard`, `get_efficiency_report`,
  `get_tool_call_stats`). Tests: `ScorecardTests`.
- [x] **Cut 5 — Active eval.** `Models/ChallengeRecord.cs`,
  `Models/EvalRecord.cs`, `NamespaceStores.Challenges`,
  `NamespaceStores.Evals`, `Infrastructure/ChallengeGrader.cs`,
  `Tools/ChallengeTool.cs` (`get_next_challenge`, `submit_challenge`,
  `get_eval_leaderboard`). Tests: `ChallengeGraderTests`,
  `ChallengeToolTests`.
- [x] **Cut 6 — Context-loss reporting.** `Tools/ContextResetTool.cs`
  (`report_context_reset`), `CycleDetector` context-loss pattern.
  Tests: `ContextResetTests`.
- [x] **Cross-cutting.** AGENTS.md Architecture Decisions #53–#58 added,
  `SPEC.md` + `README.md` updated for `TOTAL_RECALL_MODE` env var, version
  bumped to 2.4.0.

### Post-v2.4.0 — Triple-mode entry

- [x] **`report` CLI sub-command.** `src/Total.Recall/Reporting/ReportRunner.cs`
  dispatches `dotnet run -- report <sub-cmd>` to existing tool methods.
  Sub-commands: `tool-stats`, `efficiency`, `scorecard`, `cycles`,
  `sessions`, `leaderboard`. Tests: `ReportRunnerTests` (12). AGENTS.md
  Architecture Decision #60.
- [x] **Doc-drift CI gate.** `tests/Total.Recall.Tests/Docs/ToolReferenceDocDriftTests.cs`
  reflects over every `[McpServerTool]` in the production assembly and
  asserts each snake_case tool name appears in `docs/TOOL_REFERENCE.md`,
  no retired tools linger in the docs, and the "all N MCP tools" count
  in the doc header matches the live assembly. Three tests; gates every
  build because they run in the standard test suite.
- [x] **`report --format table` text renderer.**
  `src/Total.Recall/Reporting/TableRenderer.cs` parses each tool's JSON
  envelope and renders it as a fixed-width text table. Strategy: parse,
  find the longest array property as the table data, render header +
  separator + rows; render scalar properties as a key/value list above
  the table; pass non-JSON output (empty-state messages) through
  unchanged. Tests: `TableRendererTests` (9) + `ReportRunnerTests`
  format-flag coverage (4 new).

---

## Resolved

_Entries from `## Known bugs` / `## Known duplicates` / `## From log sweep`
that have been fixed. Format: `**[YYYY-MM-DD] <commit-hash>** — original
entry text + one-line description of the fix._

_(none yet)_

# Total.Recall — Agent Evaluation Harness (Cuts 1–6)

Tracking the six-cut rollout of the Total.Recall evaluation harness. Each cut is pure-additive — existing 23 tools and their schemas are not modified.

## Cut 1 — Passive tool-call telemetry  [in progress]

Foundation. Captures every MCP tool invocation to `tool-calls.jsonl`. All later cuts read from this.

- [x] `Models/ToolCall.cs` — record schema
- [x] `Infrastructure/TelemetryConfig.cs` — `TOTAL_RECALL_MODE` env var (`passive`|`active-eval`|`off`)
- [x] `Infrastructure/Telemetry.cs` — `Track(...)` wrapper + in-process session id + dedupe key
- [x] `NamespaceStores` — add `ToolCalls` store
- [x] `RepoConfig.ToolCallsPath`
- [x] Instrument tools via `Telemetry.Track` wrapper (start: `GetGotchas`, `GetContext`, `GetTestableTargets`, `GetSourceSnippet`, `ResolveType`)
- [x] Tests: `TelemetryTests`, `ToolCallStoreTests`

## Cut 2 — Cycle detection

Reads `tool-calls.jsonl`, surfaces wasteful loops.

- [ ] `Models/CycleRecord.cs`
- [ ] `Infrastructure/CycleDetector.cs` — re-query + oscillation patterns
- [ ] `NamespaceStores.Cycles`
- [ ] `Tools/CyclesTool.cs` — `get_cycles`
- [ ] Auto-append `cycle-detected` gotchas on first detection per session
- [ ] Tests: `CycleDetectorTests`, `CyclesToolTests`

## Cut 3 — Task bracketing

Sub-session unit of work attribution.

- [ ] `Models/TaskRecord.cs`
- [ ] `NamespaceStores.Tasks`
- [ ] `Tools/TaskTool.cs` — `start_task`, `end_task`, `log_task`
- [ ] `Telemetry` tracks current task id, stamps it on every `ToolCall`
- [ ] Tests: `TaskToolTests`

## Cut 4 — Model scorecard

Pure aggregation. No new write tools.

- [ ] `Tools/ScorecardTool.cs` — `get_model_scorecard`, `get_efficiency_report`, `get_tool_call_stats`
- [ ] Tests: `ScorecardTests`

## Cut 5 — Active eval

- [ ] `Models/ChallengeRecord.cs`, `Models/EvalRecord.cs`
- [ ] `NamespaceStores.Challenges`, `NamespaceStores.Evals`
- [ ] `Infrastructure/ChallengeGrader.cs`
- [ ] `Tools/ChallengeTool.cs` — `get_next_challenge`, `submit_challenge`, `get_eval_leaderboard`
- [ ] Seed 5 challenges from existing assessments
- [ ] Tests: `ChallengeGraderTests`, `ChallengeToolTests`

## Cut 6 — Context-loss reporting

- [ ] `Tools/ContextResetTool.cs` — `report_context_reset`
- [ ] `CycleDetector` — context-loss pattern (resolve_type repeat with no write between)
- [ ] Tests: `ContextResetTests`

## Cross-cutting

- [ ] Update `AGENTS.md` Architecture Decisions list with new behaviours
- [ ] Update `SPEC.md` + `README.md` for new tools + `TOTAL_RECALL_MODE` env var
- [ ] Bump version to 2.4.0 once Cuts 1–4 land

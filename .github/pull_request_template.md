<!--
Thanks for opening a PR. The project has a small but non-negotiable set of
working rules in `AGENTS.md` (humans + AI agents equally). Please confirm
each item below before requesting review.
-->

## What this PR does

<!-- One paragraph. What changed, why, what user-visible behaviour shifts. -->

## Linked issue / context

<!-- e.g. Closes #42, or "Picked up from docs/TODO.md ## Backlog". Delete if N/A. -->

## Type of change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Refactor (no behaviour change)
- [ ] Docs / chore
- [ ] Breaking change (tool output schema change, env var rename, etc.)

## Checklist

- [ ] `dotnet build Total.Recall.sln -warnaserror` passes locally (0 warnings)
- [ ] `dotnet test Total.Recall.sln` passes locally
- [ ] Bug fixes include a regression test that **fails** against unfixed code and **passes** against the fix
- [ ] Any test touching static state uses `[Collection("ToolTests")]` + `TelemetryTestHarness`
- [ ] New helpers live in `src/Total.Recall/Infrastructure/` (not duplicated across `Tools/` + `Scanners/`)
- [ ] If this PR adds/removes a tool, scanner, or env var:
  - [ ] `AGENTS.md` tables updated + new numbered entry appended to [`docs/DECISIONS.md`](../docs/DECISIONS.md)
  - [ ] `SPEC.md` and `README.md` user-visible-behaviour sections updated
  - [ ] `docs/TOOL_REFERENCE.md` schemas updated
  - [ ] Per-tool `[Description]` attribute updated (the agent's only contract)
- [ ] Commit message follows convention: `feat|fix|test|refactor|docs(total-recall): <description>`

## How was this verified

<!-- "Ran X, observed Y." If a behaviour change, attach before/after output. -->

## Anything you noticed but didn't fix

<!-- Per AGENTS.md "Always report bugs and failures": flag latent footguns or
flaky tests you spotted in unrelated code, and confirm they're in
`docs/TODO.md` under ## Known bugs. -->

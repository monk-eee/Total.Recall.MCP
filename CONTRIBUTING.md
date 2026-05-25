# Contributing to Total.Recall

Thanks for the interest. This project has a small, opinionated set of working rules. Please read them before opening a PR — they are not boilerplate.

## Before you start

**Read [AGENTS.md](AGENTS.md) end-to-end.** In particular, the "Architecture Decisions" numbered list is the soul of the project — every entry documents a specific design choice and the reasoning behind it. Most regressions come from changes that quietly contradict one of those entries. If your PR conflicts with one of them, either (a) make the conflict explicit in the PR description and propose a new numbered entry that supersedes the old one, or (b) reconsider the change.

Also worth reading:

- [README.md](README.md) — what the project is and why
- [SPEC.md](SPEC.md) — full specification of tools, scanners, and data files
- [docs/TOOL_REFERENCE.md](docs/TOOL_REFERENCE.md) — input/output schemas for all 34 MCP tools

## Working rules (non-negotiable)

These are lifted directly from `AGENTS.md`. They apply to humans and AI agents equally.

### 1. Tests must pass

- Run `dotnet test Total.Recall.sln` after every change. All tests must pass before commit. CI runs the same command on Ubuntu and Windows.
- Never skip, `[Fact(Skip=...)]`, or comment out a test to make CI pass. Fix the code, not the test.
- Bug fixes require a regression test. The test must FAIL against un-fixed code and PASS against the fix. Verify both directions.

### 2. Build must be clean

- `dotnet build` must succeed with zero warnings on the changed projects.
- Nullable warnings, unused-using warnings, and analyzer warnings all count.

### 3. Static-state tests need a collection

Any test that touches process-global static state (`Telemetry.*`, `CycleDetector.s_fired`, `TelemetryConfig.s_cachedMode`, `StoreRegistry` caches, `RepoConfig` cache, `TOTAL_RECALL_*` env vars) MUST be in `[Collection("ToolTests")]`. xUnit runs collections in parallel; without the attribute, sibling tests trample each other's static state. See `TelemetryTestHarness` for the canonical setup/teardown pattern.

### 4. File discipline

- Keep `.cs` files focused. ~500 lines is the soft cap. When a file holds more than one responsibility, split it.
- One public type per file where practical.

### 5. Use existing infrastructure

Before writing a new helper, check `src/Total.Recall/Infrastructure/`:

- `SharedJsonOptions` — three static `JsonSerializerOptions` instances. Never `new JsonSerializerOptions()` per call (STJ reflection cache is per-instance).
- `StoreRegistry` / `NamespaceStores` — singleton access to all JSONL stores. Never instantiate `JsonLineStore<T>` directly in a tool.
- `Log` — stderr-only logger. Never `Console.WriteLine` (stdout corrupts JSON-RPC).
- `Metrics` — `Interlocked.Add`-based counters.
- `RepoConfig` — env var + `config.json` resolution.
- `ParamHelper` — constructor-param classification.

If the helper doesn't exist, add it to `Infrastructure/` first, then call it. Do not duplicate logic across `Tools/` and `Scanners/`.

### 6. Documentation discipline

Every shipped feature must update agent-facing surfaces in the same commit:

- **`AGENTS.md`** — if the change adds/removes a tool, scanner, env var, or introduces architectural behaviour, update the relevant table and append an entry to the Architecture Decisions list.
- **`SPEC.md` / `README.md`** — user-visible behaviour changes go here too.
- **`docs/TOOL_REFERENCE.md`** — keep tool input/output schemas in sync.
- **Per-tool `[Description]` attributes** — when a tool's behaviour changes, update its `[Description]` (this is the MCP protocol contract).

### 7. Git discipline

- One commit = one meaningful unit of work. Scoped, validated, tested.
- Stage explicitly with named paths. Never `git add -A` / `git add .`.
- Verify the staged set immediately before commit: `git diff --cached --name-only` must list ONLY files you authored.
- Review every diff (`git diff --cached`) before commit.
- **Never `git stash`.** If you need to verify a regression test fails against un-fixed code, temporarily edit the fix out (and restore it), or commit a `wip` commit and amend.

## Commit convention

```
feat(total-recall): <description>
fix(total-recall): <description>
test(total-recall): <description>
refactor(total-recall): <description>
docs(total-recall): <description>
```

## MCP-specific rules

- **stdout is reserved for JSON-RPC.** Any `Console.WriteLine`, unhandled exception with a stack trace going to stdout, or rogue `Trace.Write` will corrupt the protocol. All diagnostic output goes through `Log` to stderr.
- **Tool `[Description]` attributes are the agent's only contract.** When you change a tool's behaviour, update its description in the same commit.
- **Backward compatibility on tool output schemas.** Agents may have cached field names. Add fields freely; do not rename or remove fields without a major-version note in `SPEC.md`.

## Reporting bugs

Open an issue with:

1. What you observed
2. Where (file + symbol or test name)
3. Impact
4. Whether it's reproducible from a clean clone

If you noticed a bug but didn't fix it in your PR, add an entry to `docs/TODO.md` under `## Known bugs` instead of silently dropping it.

## Code of conduct

Be direct, be kind, assume good faith. Disagreement on technical design is welcome; disagreement on whether tests should pass is not.

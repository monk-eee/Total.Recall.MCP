# Demo: Total.Recall in 90 seconds

This is the storyboard for a 90-second screen recording that shows the closed-loop memory feature end-to-end. Two sessions, one screencap, agent gets smarter between them.

## The story arc

> **Session 1**: agent picks a target, generates a scaffold, writes a test, hits a known gotcha, logs the session.
> **Session 2**: agent looks at the same target, surfaces the gotcha BEFORE writing the test, avoids the trap.

The demo is real — not staged. Both sessions are live MCP calls against the same workspace.

## Materials needed

- A small .NET 8 project (50–200 classes) with some uncovered code. The [`sample/`](../sample) directory is fine; or use any open-source library you have locally.
- A namespace already scanned: `dotnet run --project src/Total.Recall -- scan ... --namespace demo --enrich`
- VS Code with Copilot Chat + Total.Recall configured in `.vscode/mcp.json`
- A terminal alongside the chat panel (split-screen)
- Screen recorder (OBS or similar). 1280x720, ~30fps, 14pt font in editor.

## Recording tips

- Start with the workspace open, chat panel empty, terminal idle.
- Use Copilot's "Ask" mode, not "Edit" — viewers need to see tool calls in the chat thread.
- After each prompt, let the tool output render fully before moving on (don't cut). The tool names (`Total.Recall_get_testable_targets`, etc.) are part of the message.
- Total runtime budget: 90 seconds. Each scene below is ~15 seconds.

## Scene 1 — Pick a target (0:00–0:15)

**On screen:** Chat panel. Empty.
**You type:**

> Use Total.Recall to pick the next class to add tests for. ns=demo, top 3.

**Expected output:** A `get_testable_targets` tool call returns a ranked list. The top entry is a class with uncovered methods and a non-trivial score. Highlight the `score`, `uncoveredLines`, and `reason` fields.

**Voiceover (subtitle):** "Cross-joins coverage, type metadata, gotchas, and prior assessments. One call."

## Scene 2 — Read the source (0:15–0:30)

**You type:**

> Show me the source of `<ClassName>.<MethodName>`.

**Expected output:** `get_source_snippet` returns the actual method body, with line numbers. No `read_file` needed.

**Voiceover:** "Source served directly from the scanner's index. The agent doesn't have to open a file."

## Scene 3 — Generate the scaffold (0:30–0:45)

**You type:**

> Generate a test scaffold for `<ClassName>`.

**Expected output:** `generate_test_scaffold` returns a complete xUnit test class with constructor wiring, mock setups, and `[Fact]` stubs annotated with the uncovered line ranges.

**Voiceover:** "Scaffold pre-populated with mocks from the type registry and stub bodies pointing at uncovered ranges."

## Scene 4 — Write the test and run it (0:45–0:60)

**On screen:** Switch to the editor. Paste the scaffold into the test project. Fill in one stub. Switch to terminal.

**You type in terminal:**

```bash
dotnet test --filter <ClassName>Tests
```

**Expected output:** Green. Maybe one fails — that's the moment that becomes the gotcha in scene 5.

**Voiceover:** "Test runs. Real coverage. Whether it passes or fails, the next step captures what we just learned."

## Scene 5 — Log the session (0:60–0:75)

**You type in chat:**

> Log this session: 1 class attempted, 1 succeeded, 3 tests written, coverage went from 42% to 44%. Add a gotcha for `<ClassName>`: "<the trap you discovered>".

**Expected output:** Two tool calls. `add_gotcha` writes to `gotchas.jsonl`. `log_session` records the outcome to `sessions.jsonl`. Both are append-only.

**Voiceover:** "Session ends. The knowledge survives the context window."

## Scene 6 — Next session sees it (0:75–0:90)

**On screen:** Clear the chat panel. Start a new session.
**You type:**

> Get context for `<ClassName>`.

**Expected output:** `get_context` returns the type info, coverage data, AND the gotcha you just added. The gotcha appears at the top — the agent sees the trap before writing any code.

**Voiceover:** "Same class, new session. The agent is now smarter than it was 30 seconds ago. That's the whole point."

## End card (fade out)

```
Total.Recall
github.com/<your-handle>/Total.Recall
MIT
```

## Common gotchas while recording

- Disable VS Code's "Code Lens" and "Inline Suggestions" — they clutter the editor in playback.
- Run `Get-ChildItem data` once at the start to show the JSONL files. Helps viewers understand "this is just text on disk".
- If Copilot decides to use the wrong tool, edit the prompt to be more specific. Reroll until it picks the Total.Recall tool first.
- Keep terminal output short — `dotnet test` with `--no-build` and `--filter` keeps it under 5 seconds.

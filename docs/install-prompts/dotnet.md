# Install prompt — .NET

Open your target .NET repo in VS Code, open Copilot Chat in **agent mode**, and paste everything below the horizontal rule. The agent will install Total.Recall, scan the repo, and wire it into `.vscode/mcp.json`.

---

You are installing Total.Recall for this .NET repository.

Total.Recall is a global dotnet tool that ships the MCP server, the .NET scanner, and the report CLI in one binary. Repo + docs: https://github.com/monk-eee/Total.Recall.MCP

Do the steps below in order. After EACH step, report the command you ran, its exit code, and a one-line summary of the output. If a step fails, STOP and ask me — do not improvise or skip ahead.

**Step 1 — Prerequisites.** Run `dotnet --version`. Require 8.0.400 or newer. If missing or older, stop and tell me to install the .NET 8.0 SDK first.

**Step 2 — Install (or update) the global tool.** The `--version` flag is MANDATORY — do not drop it. Without it, `dotnet tool update` does not see pre-release versions and will silently leave any older install in place.
- If `total-recall` is not on PATH: `dotnet tool install -g TotalRecall.Mcp --version 2.6.0-preview.1`
- If it is on PATH: `dotnet tool update -g TotalRecall.Mcp --version 2.6.0-preview.1`
- Verify by running `total-recall doctor`. It must print `Total.Recall doctor v2.6.0-preview.1` on its first line. If the version is older, run `dotnet tool uninstall -g TotalRecall.Mcp` then the install command above and re-verify. Do NOT proceed past this step on anything but 2.6.0-preview.1 — the rest of the prompt relies on tools that don't exist in older versions.

**Step 3 — Build the target.** Run `dotnet build` from the repo root so the scanner has a fresh `.dll` to read.

**Step 4 — Generate coverage if none exists.** Look for the newest `coverage.cobertura.xml` under `TestResults/`. If absent (or older than the latest source change), run `dotnet test --collect:"XPlat Code Coverage"`.

**Step 5 — Bootstrap.** From the repo root, run `total-recall init .`. This auto-discovers the newest `.dll`, the newest Cobertura XML, and the matching test project. It writes `config.json` AND prints both (a) a ready-to-paste `.vscode/mcp.json` block and (b) the exact scan command to run next. Capture both verbatim.

**Step 6 — Run the printed scan command.** Use it as-printed. If `--enrich` is not in it, append it.

**Step 7 — Wire VS Code.** Create or update `.vscode/mcp.json` at the repo root with the JSON block `init` printed. If the file already exists with other MCP servers, MERGE the `Total.Recall` server entry — do not overwrite the file.

If you didn't capture the block from step 5 (output truncated, lost, or unclear), use this template and substitute the absolute paths you'd use in step 5. `init` writes the same shape:

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "total-recall",
      "env": {
        "TOTAL_RECALL_DATA": "<absolute path to data root, e.g. C:\\path\\to\\Total.Recall\\data>",
        "TOTAL_RECALL_NAMESPACE": "<namespace you passed to scan>",
        "TOTAL_RECALL_SOURCE_ROOT": "<absolute path to repo source root, e.g. C:\\path\\to\\target-repo\\src>",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_MODE": "passive"
      }
    }
  }
}
```

**Step 8 — Verify.** Run `total-recall doctor`. Confirm: env vars resolved, data dir exists, JSONL files present, `config.json` valid. Paste the output.

**Step 9 — Hand off.** Ask me to either restart VS Code or run "Developer: Reload Window". Then tell me to paste this into Copilot agent chat to smoke-test the server:

> Total.Recall, get the top 3 testable targets.

At the end, summarise:
- Every command run with its exit code
- Final contents of `.vscode/mcp.json`
- `total-recall doctor` output
- Anything that needed manual intervention

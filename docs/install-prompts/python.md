# Install prompt — Python

Open your target Python repo in VS Code, open Copilot Chat in **agent mode**, and paste everything below the horizontal rule. The agent will install both pieces of Total.Recall (the .NET MCP server and the Python scanner), scan your repo, and wire it into `.vscode/mcp.json`.

---

You are installing Total.Recall for this Python repository.

Total.Recall has two pieces:
- **MCP server** — a .NET global tool (`TotalRecall.Mcp` on NuGet). Distributed via NuGet because the server is .NET, but it is language-agnostic and reads whatever JSONL the scanner produces.
- **Python scanner** — a PyPI package (`total-recall-scan-py`) that walks Python source with `ast`, parses Cobertura coverage, and emits the canonical JSONL the MCP server reads.

Repo + docs: https://github.com/monk-eee/Total.Recall.MCP

Do the steps below in order. After EACH step, report the command you ran, its exit code, and a one-line summary of the output. If a step fails, STOP and ask me.

**Step 1 — Prerequisites.**
- `dotnet --version` — require 8.0.400 or newer (needed for the MCP server). If missing, stop and tell me to install the .NET 8.0 SDK.
- `python --version` — require 3.10 or newer.
- `pipx --version` (or `uv --version`). If neither is installed, stop and tell me which one I want to install first.

**Step 2 — Install the MCP server (.NET global tool).** The `--version` flag is MANDATORY — do not drop it. Without it, `dotnet tool update` does not see pre-release versions and will silently leave any older install in place.
- If `total-recall` is not on PATH: `dotnet tool install -g TotalRecall.Mcp --version 2.6.0-preview.1`
- If it is on PATH: `dotnet tool update -g TotalRecall.Mcp --version 2.6.0-preview.1`
- Verify by running `total-recall doctor`. It must print `Total.Recall doctor v2.6.0-preview.1` on its first line. If the version is older, run `dotnet tool uninstall -g TotalRecall.Mcp` then the install command above and re-verify. Do NOT proceed past this step on anything but 2.6.0-preview.1.

**Step 3 — Install the Python scanner.**
- `pipx install total-recall-scan-py` (or `uv tool install total-recall-scan-py`)
- Verify with `total-recall-py version`.

**Step 4 — Generate coverage if none exists.** Look for a Cobertura XML (`coverage.xml` or `coverage.cobertura.xml`) anywhere under the repo. If none, run:
```
coverage run -m pytest
coverage xml -o coverage.xml
```

**Step 5 — Bootstrap.** From the repo root, run `total-recall-py init .`. This auto-discovers the source root (`src/<pkg>` or flat layout), the tests directory, the newest Cobertura XML, and the package name from `pyproject.toml`. It writes `config.json` AND prints both (a) a ready-to-paste `.vscode/mcp.json` block (already pointed at the `total-recall` .NET binary) and (b) the exact `total-recall-py scan` command to run next. Capture both verbatim.

**Step 6 — Run the printed scan command.** Use it as-printed.

**Step 7 — Wire VS Code.** Create or update `.vscode/mcp.json` at the repo root with the JSON block `init` printed. If the file already exists with other MCP servers, MERGE the `Total.Recall` server entry — do not overwrite the file.

If you didn't capture the block from step 5 (output truncated, lost, or unclear), use this template and substitute the absolute paths you'd use in step 5. `total-recall-py init` writes the same shape:

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "total-recall",
      "env": {
        "TOTAL_RECALL_DATA": "<absolute path to data root>",
        "TOTAL_RECALL_NAMESPACE": "<namespace from step 5>",
        "TOTAL_RECALL_SOURCE_ROOT": "<absolute path to the Python source root, e.g. /path/to/repo/src>",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_MODE": "passive"
      }
    }
  }
}
```

Note: the `command` is `total-recall` (the .NET MCP server), NOT `total-recall-py` — the Python tool is only the scanner, the server is always the .NET binary.

**Step 8 — Verify.** Run `total-recall doctor` (the .NET MCP server's diagnostic). Confirm: env vars resolved, data dir exists, JSONL files present, `config.json` valid. Paste the output.

**Step 9 — Hand off.** Ask me to either restart VS Code or run "Developer: Reload Window". Then tell me to paste this into Copilot agent chat to smoke-test the server:

> Total.Recall, get the top 3 testable targets.

At the end, summarise:
- Every command run with its exit code
- Final contents of `.vscode/mcp.json`
- `total-recall doctor` output
- Anything that needed manual intervention

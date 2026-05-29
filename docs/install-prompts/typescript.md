# Install prompt — TypeScript

Open your target TypeScript/JavaScript repo in VS Code, open Copilot Chat in **agent mode**, and paste everything below the horizontal rule. The agent will install both pieces of Total.Recall (the .NET MCP server and the TypeScript scanner), scan your repo, and wire it into `.vscode/mcp.json`.

---

You are installing Total.Recall for this TypeScript repository.

Total.Recall has two pieces:
- **MCP server** — a .NET global tool (`TotalRecall.Mcp` on NuGet). Distributed via NuGet because the server is .NET, but it is language-agnostic and reads whatever JSONL the scanner produces.
- **TypeScript scanner** — an npm package (`@total-recall/scan`) that walks TypeScript source with the TS compiler API, parses Cobertura coverage, and emits the canonical JSONL the MCP server reads.

Repo + docs: https://github.com/monk-eee/Total.Recall.MCP

The TS scanner currently only exposes `scan` and `version` sub-commands (no `init` yet — discovery is manual). The steps below cover that.

Do the steps below in order. After EACH step, report the command you ran, its exit code, and a one-line summary of the output. If a step fails, STOP and ask me.

**Step 1 — Prerequisites.**
- `dotnet --version` — require 8.0.400 or newer (needed for the MCP server). If missing, stop and tell me to install the .NET 8.0 SDK.
- `node --version` — require 18 or newer.
- `npm --version` — confirm npm is available.

**Step 2 — Install the MCP server (.NET global tool).** The `--version` flag is MANDATORY — do not drop it. Without it, `dotnet tool update` does not see pre-release versions and will silently leave any older install in place.
- If `total-recall` is not on PATH: `dotnet tool install -g TotalRecall.Mcp --version 2.6.0-preview.1`
- If it is on PATH: `dotnet tool update -g TotalRecall.Mcp --version 2.6.0-preview.1`
- Verify by running `total-recall doctor`. It must print `Total.Recall doctor v2.6.0-preview.1` on its first line. If the version is older, run `dotnet tool uninstall -g TotalRecall.Mcp` then the install command above and re-verify. Do NOT proceed past this step on anything but 2.6.0-preview.1.

**Step 3 — Install the TypeScript scanner.**
- `npm install -g @total-recall/scan`
- Verify with `total-recall-ts version`.

**Step 4 — Discover the repo layout.** Read `package.json` and any `tsconfig.json`. Determine and report:
- `sourceRoot` — usually `./src` (read from `tsconfig.json` `compilerOptions.rootDir` or `include` if present)
- `testsPath` — typically `./tests`, `./test`, `./__tests__`, or `./src` if tests are colocated
- `namespace` — a slugified version of the `name` field in `package.json` (lowercase, non-alphanumerics to `-`). Strip any `@scope/` prefix.

**Step 5 — Generate coverage if none exists.** Look for a Cobertura XML anywhere under the repo (commonly `coverage/cobertura-coverage.xml`). If none, the project must produce one. Detect the test runner:
- **vitest** — run `npx vitest run --coverage --coverage.reporter=cobertura`
- **jest** — add `"cobertura"` to `coverageReporters` in `jest.config.*` (or pass `--coverageReporters=cobertura`), then `npx jest --coverage`
- **mocha + c8** — `npx c8 --reporter=cobertura mocha`
- If you can't determine the runner, ask me before installing one.

**Step 6 — Pick a data root.** Use `./.total-recall/data` under the repo unless I tell you otherwise. Create the directory if missing. Add `.total-recall/` to `.gitignore` if not already ignored.

**Step 7 — Scan.** Run:
```
total-recall-ts scan \
  --source-root <sourceRoot from step 4> \
  --tests <testsPath from step 4> \
  --coverage <path from step 5> \
  --namespace <namespace from step 4> \
  --output ./.total-recall/data
```
Confirm the three JSONL files (`type-registry.jsonl`, `coverage-gaps.jsonl`, `test-inventory.jsonl`) appear under `./.total-recall/data/<namespace>/`.

**Step 8 — Wire VS Code.** Create or update `.vscode/mcp.json` at the repo root. If the file already exists with other MCP servers, MERGE the `Total.Recall` server entry — do not overwrite the file. The block to add (substitute absolute paths):

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "total-recall",
      "env": {
        "TOTAL_RECALL_DATA": "<absolute path to ./.total-recall/data>",
        "TOTAL_RECALL_NAMESPACE": "<namespace from step 4>",
        "TOTAL_RECALL_SOURCE_ROOT": "<absolute path to sourceRoot>",
        "TOTAL_RECALL_LOG_LEVEL": "info",
        "TOTAL_RECALL_MODE": "passive"
      }
    }
  }
}
```

**Step 9 — Verify.** Run `total-recall doctor --ns <namespace>`. Confirm: env vars resolved, data dir exists, JSONL files present. Paste the output.

**Step 10 — Hand off.** Ask me to either restart VS Code or run "Developer: Reload Window". Then tell me to paste this into Copilot agent chat to smoke-test the server:

> Total.Recall, get the top 3 testable targets.

At the end, summarise:
- Every command run with its exit code
- Discovered source root, tests path, namespace
- Final contents of `.vscode/mcp.json`
- `total-recall doctor` output
- Anything that needed manual intervention

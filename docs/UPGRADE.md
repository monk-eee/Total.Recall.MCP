# Upgrading Total.Recall

Total.Recall is designed so that **every release is a drop-in upgrade**. There is no database to migrate, no config to rewrite, and no scanner output to throw away. All on-disk schema changes are additive — older record files are read by newer servers, and newer record files (with extra fields) are read by older servers (which ignore the unknown fields).

This guide covers what to do, what to check, and what to *not* worry about when moving between versions.

## TL;DR

```bash
dotnet tool update -g TotalRecall.Mcp
total-recall doctor
```

That's it. Restart VS Code so Copilot reconnects to the new MCP server. Your existing `data/<namespace>/*.jsonl` files keep working unchanged. New tools are auto-discovered over MCP; you do not need to edit `.vscode/mcp.json`.

If `total-recall doctor` reports `OK`, you're done. If it reports warnings, see [Doctor warnings](#doctor-warnings) below.

## Upgrade compatibility matrix

| From → To | Tool update | Restart VS Code | Re-scan required | mcp.json edit | Data loss risk |
|-----------|-------------|-----------------|------------------|---------------|----------------|
| 2.5.x → 2.6.0 | yes | yes | **no** (optional) | no | none |
| 2.4.0 → 2.5.x | yes | yes | **no** (optional) | no | none |
| Source build → tool install | `dotnet tool install -g TotalRecall.Mcp` and switch `command` in `mcp.json` to `"total-recall"` | yes | no | yes (one line) | none |

Patch (`2.5.0` → `2.5.1`) and preview (`2.6.0-preview.1` → `2.6.0-preview.2`) upgrades are always strictly additive.

## What's safe across every upgrade

These guarantees hold for every Total.Recall release on the 2.x line:

1. **JSONL files are forward- and backward-compatible.** Every record carries `schemaVersion` (defaulting to `1` when missing). Newer servers read older records by defaulting any missing fields; older servers read newer records by ignoring unknown fields (`System.Text.Json` discards them silently). Append-only means no rewrite-in-place ever happens.
2. **`config.json` is additive.** Older configs work with newer servers — any new keys default sensibly.
3. **`.vscode/mcp.json` does not change between releases.** New tools are discovered over MCP at startup; the agent sees them without configuration.
4. **Environment variables stay backward compatible.** `TOTAL_RECALL_DATA`, `TOTAL_RECALL_NAMESPACE`, `TOTAL_RECALL_LOG_LEVEL`, `TOTAL_RECALL_SOURCE_ROOT`, `TOTAL_RECALL_MODE` all retain their meaning across releases.
5. **Your `data/<namespace>/` directory is the source of truth.** It is never deleted, rewritten, or migrated by the tool. Back it up if it matters; the tool does not touch it on upgrade.

## Step-by-step

### 1. Update the global tool

```bash
dotnet tool update -g TotalRecall.Mcp
```

If the previous version was installed under a different package id (e.g. you built from a source checkout and ran via `dotnet run --project ...`), first install the global tool:

```bash
dotnet tool install -g TotalRecall.Mcp
```

Then switch `.vscode/mcp.json` `command` from `"dotnet"` to `"total-recall"` and drop the `args`. The `env` block stays the same.

### 2. Restart VS Code

VS Code spawns the MCP server when Copilot initialises; it does not pick up a new server binary mid-session. Quit and reopen the workspace.

### 3. Health check

```bash
total-recall doctor
```

Exit code `0` = healthy, `1` = warnings (still usable), `2` = error (data root missing). The report prints:

- Resolved env vars
- Resolved data root
- Each namespace's data file presence, record count, and last write time
- `config.json` validity (whether `sourceRoot` / `assemblyPath` / `coveragePath` / `testsPath` still resolve on disk)

### 4. (Optional) Re-scan to populate new schema fields

Each release may add **additive** fields to scanner output (e.g. `kind`, `lang`, `filePath` on `TypeRecord`). Existing data still works — the server defaults the new fields when reading old records. If you want the richer data:

```bash
total-recall scan \
  --assembly path/to/YourProject.dll \
  --coverage path/to/coverage.cobertura.xml \
  --tests path/to/YourProject.Tests \
  --namespace yourproject \
  --enrich --analyze
```

A rescan overwrites `type-registry.jsonl`, `coverage-gaps.jsonl`, `test-inventory.jsonl`, and (with `--enrich`) `class-metrics.jsonl` / `dependency-graph.jsonl`. Your **append-only stores are not touched** — `gotchas.jsonl`, `assessments.jsonl`, `bugs.jsonl`, `sessions.jsonl`, `tool-calls.jsonl`, `tasks.jsonl`, `cycles.jsonl`, `challenges.jsonl`, `evals.jsonl` all persist across rescans.

## 2.4.0 → 2.5.0

### New on-disk surfaces

- **`bugs.jsonl`** — new append-only store written by `report_bug` / `update_bug_status`. The file is created lazily on first write. If the file is absent, `get_bugs` returns an empty result without erroring. No migration is needed; pre-2.5 namespaces simply have no bugs recorded.
- **`type-registry.jsonl` additive fields** — `schemaVersion`, `kind`, `lang`, optional `filePath`. Old records (without these fields) deserialize fine — `kind` defaults to `"class"`, `schemaVersion` defaults to `1`, `lang` and `filePath` default to `null`. Rescan with 2.5.0 to populate them.

### New MCP tools

The MCP server auto-advertises tools via the MCP protocol — agents discover the new tools without any client-side change. The 2.5 release adds:

- `report_bug` — file a class-scoped bug report
- `get_bugs` — query open bugs (default) or filter by class / severity / status
- `update_bug_status` — transition `open` → `triaged` / `fixed` / `wontfix` (append-only history)

`get_context(className)` now folds open bugs into its `standard` and `full` depth output, so agents calling it before authoring tests see known-broken behaviour without an extra tool call.

### New CLI sub-command

```bash
total-recall report bugs --ns yourproject
total-recall report bugs --ns yourproject --severity critical --status open
```

### Behaviour fixes

- `total-recall scan` no longer crashes on publish-style targets that ship their own `mscorlib.dll` / `System.Private.CoreLib.dll` next to the host runtime copies (was: `FileLoadException: Assembly with same name is already loaded`). If you previously had to delete duplicate core DLLs from your publish output before scanning, you can stop.
- Bare `total-recall` from an interactive terminal now prints help instead of silently waiting on stdin. VS Code still pipes stdin so server mode triggers normally there.

## 2.5.x → 2.6.0-preview.1

### New on-disk surfaces

All additive. Pre-2.6 namespaces simply don't have these files until something writes to them.

- **`type-registry.jsonl` `schemaVersion: 2`** — adds the `lang` discriminator (`{ "kind": "dotnet" | "python" | "typescript", ... }`) for multi-language scanner support. v1 records (no `lang`) still read correctly; the server treats them as `{ "kind": "dotnet" }`.
- **`config.json` `language` field** — `dotnet | python | typescript`. Older configs without it are treated as `dotnet`.

### New MCP tools

Auto-discovered over MCP. No `.vscode/mcp.json` edit needed. 2.6 adds nothing new on top of the 2.5 tool surface — it's a scanner-platform release.

### Sibling scanners (new in 2.6)

The .NET MCP server is now language-agnostic. Two new scanners ship on their native package managers and emit the same JSONL schema:

- **`total-recall-scan-py`** on PyPI — `pipx install total-recall-scan-py` then `total-recall-py init <repo>` then `total-recall-py scan ...`. Includes a `--watch` mode.
- **`@total-recall/scan`** on npm — `npm install -g @total-recall/scan` then `total-recall-ts scan ...`.

You do not need either to keep using 2.6 against a .NET repo; the .NET scanner still works the same way. See the per-language [install prompts](install-prompts/) for paste-into-Copilot setup walkthroughs.

### CLI changes

- **`total-recall --version` / `-v`** — new in 2.6. Prints version and exits cleanly (previously bare `total-recall` from a TTY printed help; with `--version` it now exits with the version string without entering the help screen).
- **`total-recall init <repo-path>`** — auto-discovery is more aggressive: it now prefers the newest `.dll` whose name matches the repo folder, picks the newest Cobertura XML across all `TestResults/**`, and writes both `config.json` and a copy-pasteable `.vscode/mcp.json` block.

### Upgrade procedure

`dotnet tool update -g TotalRecall.Mcp --version 2.6.0-preview.1` is the whole story. The `--version` flag is required for previews — `dotnet tool update` without it does not see pre-release versions and silently leaves any older install in place. If `total-recall doctor` still reports an older version on its first line after the update, uninstall and reinstall:

```bash
dotnet tool uninstall -g TotalRecall.Mcp
dotnet tool install -g TotalRecall.Mcp --version 2.6.0-preview.1
```

## Doctor warnings

`total-recall doctor` returns exit `1` when something is recoverable. The most common signals after an upgrade:

| Warning | What it means | Fix |
|---------|---------------|-----|
| `bugs.jsonl: missing` | Pre-2.5 namespace, never written. | Ignore — the file is created on first `report_bug`. |
| `type-registry.jsonl: present (older schema)` | Records have no `schemaVersion` / `kind` / `lang`. | Rescan with 2.5.0 to populate; not required. |
| `config.json: sourceRoot does not resolve` | The path you scanned from has moved or is on a different machine. | Rerun `total-recall init <repo-path>` or `total-recall scan --source-root <new-path>`. |
| `coverage-gaps.jsonl: 0 records` | Coverage XML was empty or scan target was a stub. | Regenerate the cobertura file and rescan. |
| Data root exists but contains no namespaces | First-time install, or `TOTAL_RECALL_DATA` points at the wrong directory. | Run `total-recall init <repo-path>` or fix the env var. |

## Rollback

```bash
dotnet tool update -g TotalRecall.Mcp --version 2.5.0-preview.1
```

Restart VS Code. Your `data/` directory is untouched, including any 2.6-era files (`tool-calls.jsonl`, `tasks.jsonl`, `cycles.jsonl`, `challenges.jsonl`, `evals.jsonl`) — 2.5 simply doesn't read them. No data is lost; if you reinstall 2.6 later, the files are picked up again.

## Recovery

If something does go wrong and a JSONL file becomes unreadable (e.g. partial write during a crash), each store is line-oriented and recoverable with a one-liner:

```powershell
# Drop the trailing partial line in PowerShell
Get-Content data/yourproject/bugs.jsonl | Where-Object { $_ -match '^\s*{.*}\s*$' } | Set-Content data/yourproject/bugs.jsonl.tmp
Move-Item -Force data/yourproject/bugs.jsonl.tmp data/yourproject/bugs.jsonl
```

`total-recall doctor` will then return to `OK`. No record content needs reconstruction — append-only means whole-line records are always self-contained JSON.

## Reporting upgrade problems

If `dotnet tool update` succeeds but `total-recall doctor` reports an error you can't resolve, open an issue with:

1. Output of `total-recall doctor` (full report).
2. Output of `total-recall --version`.
3. Contents of `.vscode/mcp.json` (redact paths if needed).
4. The first 20 lines of any JSONL file the doctor flagged.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for deeper diagnostics.

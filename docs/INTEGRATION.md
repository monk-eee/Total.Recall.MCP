# Total.Recall — Target Repo Integration Guide

How to make AI agents in your target workspace aware of Total.Recall. This is **standalone** — it doesn't depend on any specific skill, framework, or workflow. It works with any VS Code Copilot agent that supports MCP tools.

## The Problem

Total.Recall is an MCP server that runs alongside your editor. It exposes 15 tools for type lookup, coverage analysis, source serving, test scaffolding, and session tracking. But Copilot agents don't know these tools exist unless you tell them.

Two injection points make agents aware:

| Injection Point | What It Does | Who Reads It |
|-----------------|--------------|-------------|
| `.github/copilot-instructions.md` | Auto-injected into every Copilot conversation | VS Code Copilot (all modes) |
| `AGENTS.md` | Read by agents at task start (especially in agent mode) | Copilot agent mode, custom agents, skills |

**Neither file gets checked in to Total.Recall's repo.** They live in your target workspace — the repo you're writing tests for. This keeps Total.Recall independent and each consuming repo in control of its own agent instructions.

---

## 1. `.vscode/mcp.json` — Wire the Server

This is the only required file. Without it, nothing works.

Create `.vscode/mcp.json` in your target workspace root:

```json
{
  "servers": {
    "Total.Recall": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\Total.Recall\\src\\Total.Recall\\Total.Recall.csproj"
      ],
      "env": {
        "TOTAL_RECALL_DATA": "C:\\path\\to\\Total.Recall\\data",
        "TOTAL_RECALL_NAMESPACE": "your-namespace"
      }
    }
  }
}
```

### Environment variables

| Variable | Required | Description |
|----------|----------|-------------|
| `TOTAL_RECALL_DATA` | yes | Root data directory containing namespace folders |
| `TOTAL_RECALL_NAMESPACE` | recommended | Namespace subdirectory name (e.g., `linter`, `myproject`) |
| `TOTAL_RECALL_SOURCE_ROOT` | optional | Target repo source root — enables `get_source_snippet`. Can also be set via scanner `--source-root` flag, which persists it to `config.json`. |

### Gitignore considerations

`.vscode/mcp.json` contains machine-specific absolute paths. You probably want to either:
- Add it to `.gitignore` (each developer creates their own)
- Use environment variable expansion if your team standardizes paths
- Check it in with relative paths if Total.Recall is cloned to a known sibling location

---

## 2. `.github/copilot-instructions.md` — Copilot Awareness

This file is **auto-injected by VS Code into every Copilot conversation** — chat, inline, and agent mode. It's the broadest injection point.

Copy the template into your target workspace:

```bash
# From your target repo root:
copy C:\path\to\Total.Recall\docs\copilot-instructions-template.md .github\copilot-instructions.md
```

Or append it to an existing file. The full template is at **[copilot-instructions-template.md](copilot-instructions-template.md)** — it includes:

- The 5-step recommended workflow (targets → source → scaffold → test → log)
- All 15 tool names with usage examples and parameter hints
- Fallback guidance for when `read_file` is still appropriate
- Performance notes (sub-millisecond responses, O(1) lookups)

### What to customize

- **Parameter defaults**: Adjust `top: 5` vs `top: 10` etc. to match your team's preferences
- **Workflow order**: Reorder or add steps for your specific workflow
- **Fallback guidance**: Add project-specific notes about files not in the scanned assembly
- **Don't rename tools**: The tool names in the template match the MCP tool names exactly

---

## 3. `AGENTS.md` — Agent Mode Awareness

`AGENTS.md` is read by Copilot's agent mode at the start of tasks. It provides deeper context than `copilot-instructions.md` — tool reference tables, re-scan commands, and architectural guidance.

Copy the template section into your target repo's `AGENTS.md` (create the file if it doesn't exist):

```bash
# From your target repo root:
copy C:\path\to\Total.Recall\docs\agents-md-template.md AGENTS-mcp-section.md
# Then paste its contents into your AGENTS.md
```

The full template is at **[agents-md-template.md](agents-md-template.md)** — it includes:

- All 15 tools organized into v2 Decision Engine and v1 Lookup Index tables
- Complete parameter reference for each tool
- The 3-step coverage uplift workflow (targets → test → log)
- Full-scan and incremental re-scan command examples
- Data source table (8 files, what updates each, descriptions)
- Fallback guidance for when the MCP server is unavailable

### What to customize

The template has `<!-- CUSTOMIZE -->` markers at key locations:

- **Paths**: Replace `C:\path\to\...` with your actual Total.Recall clone location
- **Namespace**: Replace `your-namespace` with your project's namespace (e.g., `linter`)
- **Re-scan commands**: Update assembly/coverage/tests paths for your project
- **Additional context**: Add project-specific notes — which classes are untestable and why, preferred test patterns, team conventions

---

## 4. What NOT to Do

### Don't check MCP-specific files into Total.Recall's repo

The `.github/copilot-instructions.md` and `AGENTS.md` content above lives in **your target repo**, not in Total.Recall. Total.Recall is a generic server — it shouldn't contain instructions specific to any one consuming project.

### Don't modify skills to reference Total.Recall

If you use the `coverage-uplift` skill or similar, don't edit the skill to hard-code MCP tool calls. Instead, let the agent discover tools through the injection points above. The skill works without MCP; MCP makes it faster.

### Don't duplicate what the MCP tool descriptions already say

The `[Description(...)]` attributes on each MCP tool already tell the agent what the tool does, what parameters it takes, and when to use it. Your `copilot-instructions.md` should focus on **workflow order and preferences**, not re-documenting parameter lists.

---

## 5. Verification Checklist

After setting up integration:

- [ ] `.vscode/mcp.json` exists in target workspace root
- [ ] `TOTAL_RECALL_DATA` env var points to correct data directory
- [ ] `TOTAL_RECALL_NAMESPACE` env var matches your scanned namespace
- [ ] Data directory has `.jsonl` files (run a scan first if empty)
- [ ] VS Code restarted after adding `mcp.json`
- [ ] In Copilot chat: ask "get testable targets" — should return scored list
- [ ] `.github/copilot-instructions.md` exists with MCP section
- [ ] `AGENTS.md` exists with MCP integration section (for agent mode)
- [ ] Source root configured (env var or scanner `--source-root`) if using `get_source_snippet`

---

## 6. Architecture: How Injection Works

```
┌─────────────────────────────────────────────────────────┐
│ Your Target Repo (e.g., Linter)                         │
│                                                         │
│  .vscode/mcp.json              ← wires MCP server       │
│  .github/copilot-instructions.md  ← auto-injected       │
│  AGENTS.md                     ← read by agent mode      │
│                                                         │
│  (none of these are in Total.Recall's repo)              │
└────────────┬────────────────────────────────────────────┘
             │ stdio
             ▼
┌─────────────────────────────────────────────────────────┐
│ Total.Recall MCP Server                                  │
│  (generic — knows nothing about your specific repo)      │
│  (reads JSONL data from $TOTAL_RECALL_DATA/{namespace})  │
└─────────────────────────────────────────────────────────┘
```

The server is generic. The consuming repo controls:
- **What data exists** (via scanner runs)
- **How agents use the tools** (via injection files)
- **Which namespace to query** (via env vars)

This separation means:
- Multiple repos can use the same Total.Recall server with different namespaces
- Each repo can customize agent instructions for its own conventions
- The server can be updated without touching any consuming repo
- Consuming repos can remove MCP integration by deleting 2-3 files

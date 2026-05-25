# Security Policy

## Supported versions

Total.Recall is pre-1.0 and ships from `main`. Only the latest
commit on the default branch is supported. There is no LTS branch.

## Reporting a vulnerability

If you find a security issue — credentials leaking through the
`get_source_snippet` tool, stdout corruption breaking the MCP transport,
a JSONL store being writeable outside the configured data directory, a
path-traversal in scanner inputs, or anything else with a security
flavour — please **do not** open a public issue.

Preferred channel: GitHub's private vulnerability reporting.

1. Go to the repository's **Security** tab on GitHub.
2. Click **Report a vulnerability**.
3. Fill in what you observed, where (file + symbol or tool name), and
   the smallest reproduction you have.

If GitHub's flow is unavailable, email **magic.monkee.magic@gmail.com**
with the same content and `[Total.Recall security]` in the subject line.

I aim to acknowledge reports within 7 days and to ship a fix or a public
advisory within 30 days, depending on severity.

## Threat model (what Total.Recall is and isn't)

Total.Recall is a **local-only**, single-user side-car process. It speaks
MCP over stdio to a parent process (VS Code) on the same machine. It has
no network listener, no authentication layer, and no multi-tenant
isolation beyond filesystem permissions.

Implications:

- The MCP server trusts the process that spawned it. There is no
  agent-vs-agent authorisation.
- The scanner reads paths supplied by the operator (`--assembly`,
  `--coverage`, `--tests`, `--source-root`). It performs reflection-only
  loading via `MetadataLoadContext`, but a hostile assembly path could
  still trigger denial-of-service via large/malformed inputs. Do not
  point the scanner at untrusted binaries.
- The `data/` directory is not encrypted. Do not store secrets in
  gotchas, assessments, or session logs. `AGENTS.md` covers this rule
  for contributors; the same applies to operators.

Out of scope:

- Hardening against a malicious local user with shell access on the
  same machine.
- Multi-tenant deployments. The namespace mechanism isolates data sets,
  not security domains.

## What gets fixed

In-scope (will be patched):

- Credential or source content leaking from `get_source_snippet`,
  `resolve_type`, or any other tool into telemetry, logs, or stdout.
- Path traversal in scanner inputs (`--assembly`, `--output`,
  `--source-root`, `--namespace`).
- JSONL store writes escaping the configured data directory.
- stdout corruption breaking the MCP JSON-RPC protocol (any
  `Console.WriteLine`-shaped bug).
- Crashes triggered by malformed Cobertura XML, malformed JSONL, or
  unexpected assembly metadata.

Out-of-scope (will be triaged and likely declined):

- "An attacker who can already write to `data/*.jsonl` can mislead the
  agent." That's the threat model — the data directory is trusted.
- "An operator pointed the source-root tool at a directory with
  unredacted `appsettings.Production.json`." That's an operator gitignore
  bug, documented in `AGENTS.md`.

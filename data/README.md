# Total.Recall data directory

This directory holds Total.Recall's per-namespace JSONL stores (type registry, coverage gaps, gotchas, assessments, sessions, telemetry, tasks, evals, etc.).

**It is gitignored.** Each namespace subdirectory (e.g. `data/myproject/`) is generated locally by the scanner and may contain real type names, internal class metadata, and agent session history that should never be committed.

## Generating data

```bash
dotnet run --project src/Total.Recall -- scan \
  --assembly path/to/YourProject.dll \
  --coverage path/to/coverage.cobertura.xml \
  --tests    path/to/YourProject.Tests \
  --namespace myproject --enrich
```

Output lands in `data/myproject/`. See [../docs/QUICKSTART.md](../docs/QUICKSTART.md).

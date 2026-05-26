# total-recall-scan-py

Python source scanner for [Total.Recall](https://github.com/monk-eee/Total.Recall.MCP). Walks a Python codebase, parses each file with `ast`, and emits the canonical JSONL records consumed by the Total.Recall MCP server.

This is one of three scanners in the Total.Recall ecosystem:

| Language | Package | Entry point |
|---|---|---|
| .NET | NuGet [`TotalRecall.Mcp`](https://www.nuget.org/packages/TotalRecall.Mcp) | `total-recall scan` |
| **Python** | **PyPI `total-recall-scan-py`** *(this package)* | **`total-recall-py`** |
| TypeScript | npm `@total-recall/scan` *(planned)* | `total-recall-ts` |

All three produce identical schema-shaped JSONL — the Total.Recall MCP server is language-agnostic and reads whatever lands in `data/<namespace>/`. See [`docs/SCANNER_SCHEMA.md`](https://github.com/monk-eee/Total.Recall.MCP/blob/main/docs/SCANNER_SCHEMA.md) for the contract.

## Install

```bash
pipx install total-recall-scan-py
# or
uv tool install total-recall-scan-py
```

## Usage

```bash
total-recall-py scan \
  --source-root src \
  --coverage coverage.xml \
  --tests tests \
  --namespace myproject \
  --output /path/to/Total.Recall/data
```

Inputs:

- `--source-root` — directory containing your production Python source.
- `--coverage` *(optional)* — path to a Cobertura XML file (coverage.py emits this with `coverage xml`).
- `--tests` *(optional)* — directory containing pytest test files.
- `--namespace` — namespace subdirectory under `data/`.
- `--output` — root data directory (matches `TOTAL_RECALL_DATA` env var used by the MCP server).

Outputs in `<output>/<namespace>/`:

- `type-registry.jsonl` — every public class / function / dataclass / Protocol / ABC.
- `coverage-gaps.jsonl` — uncovered lines per class (when `--coverage` is supplied).
- `test-inventory.jsonl` — pytest test methods by class under test (when `--tests` is supplied).
- `config.json` — persisted scan inputs for `total-recall doctor`.

## Schema

This scanner emits records conforming to [`schemaVersion: 1`](https://github.com/monk-eee/Total.Recall.MCP/blob/main/docs/SCANNER_SCHEMA.md). The `lang` discriminator on every `type-registry.jsonl` record is `python`, with these extension fields:

```json
{ "kind": "python", "isDataclass": true, "isFrozen": false,
  "isAbc": false, "isProtocol": false, "decorators": ["@dataclass"] }
```

## Development

```bash
git clone https://github.com/monk-eee/Total.Recall.MCP
cd Total.Recall.MCP/src/Total.Recall.Scanners.Python
python -m venv .venv
.venv\Scripts\activate    # Windows
# source .venv/bin/activate  # macOS / Linux
pip install -e ".[dev]"
pytest
```

## License

MIT — see [LICENSE](https://github.com/monk-eee/Total.Recall.MCP/blob/main/LICENSE) at the repo root.

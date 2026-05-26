# @total-recall/scan

TypeScript/JavaScript source scanner for [Total.Recall MCP](https://github.com/monk-eee/Total.Recall.MCP).

Emits canonical JSONL files (`type-registry.jsonl`, `coverage-gaps.jsonl`, `test-inventory.jsonl`) consumed by the Total.Recall MCP server. Identical schema to the .NET and Python scanners — see [`docs/SCANNER_SCHEMA.md`](https://github.com/monk-eee/Total.Recall.MCP/blob/main/docs/SCANNER_SCHEMA.md).

## Install

```bash
npm install -g @total-recall/scan
# or
npx @total-recall/scan scan --source-root ./src --namespace my-app
```

## Usage

```bash
total-recall-ts scan \
  --source-root ./src \
  --tests ./tests \
  --coverage ./coverage/cobertura-coverage.xml \
  --namespace my-app \
  --output ./.total-recall/data
```

## Schema discriminator

Records carry `lang.kind: "typescript"` with the following extension fields:

- `isExported` — `export` keyword present
- `isAmbient` — `declare` keyword present
- `isReadonlyClass` — every member is `readonly`
- `generics` — type parameter names

## Output files

Per namespace under `<output>/<namespace>/`:

- `type-registry.jsonl` — one record per top-level class / interface / function / enum / type alias
- `coverage-gaps.jsonl` — Cobertura coverage gaps when `--coverage` provided
- `test-inventory.jsonl` — vitest / jest / mocha test discovery when `--tests` provided
- `config.json` — scanner identity + input paths + timestamp

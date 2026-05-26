#!/usr/bin/env node
/**
 * Total.Recall TypeScript scanner CLI.
 *
 * Sub-commands: `scan`, `version`. Mirrors the .NET / Python scanners.
 */

import { existsSync, statSync, writeFileSync, mkdirSync } from "node:fs";
import { resolve, join } from "node:path";
import { parseArgs } from "node:util";
import { writeJsonl } from "./jsonl.js";
import { scanSourceRoot } from "./registry.js";
import { parseCobertura } from "./coverage.js";
import { scanTests } from "./tests-inv.js";
import { SCANNER_VERSION, SCANNER_NAME, SCHEMA_VERSION } from "./index.js";

export function main(argv: string[]): number {
  const [sub, ...rest] = argv;
  if (!sub || sub === "--help" || sub === "-h" || sub === "help") {
    printUsage();
    return 0;
  }
  if (sub === "version" || sub === "--version" || sub === "-v") {
    process.stdout.write(`${SCANNER_NAME} ${SCANNER_VERSION}\n`);
    return 0;
  }
  if (sub === "scan") {
    return runScan(rest);
  }
  process.stderr.write(`Unknown command: ${sub}\n`);
  printUsage();
  return 1;
}

function printUsage(): void {
  process.stdout.write(
    [
      "total-recall-ts — TypeScript scanner for Total.Recall MCP",
      "",
      "Usage:",
      "  total-recall-ts scan --source-root <dir> [--tests <dir>] [--coverage <xml>]",
      "                      [--namespace <name>] [--output <dir>] [--repo-root <dir>]",
      "  total-recall-ts version",
      "",
    ].join("\n"),
  );
}

interface ScanArgs {
  sourceRoot?: string;
  tests?: string;
  coverage?: string;
  namespace?: string;
  output?: string;
  repoRoot?: string;
}

function runScan(args: string[]): number {
  let parsed;
  try {
    parsed = parseArgs({
      args,
      options: {
        "source-root": { type: "string" },
        tests: { type: "string" },
        coverage: { type: "string" },
        namespace: { type: "string" },
        output: { type: "string" },
        "repo-root": { type: "string" },
      },
      strict: true,
      allowPositionals: false,
    });
  } catch (e: any) {
    process.stderr.write(`Error: ${e?.message ?? String(e)}\n`);
    return 2;
  }

  const opts: ScanArgs = {
    sourceRoot: parsed.values["source-root"] as string | undefined,
    tests: parsed.values.tests as string | undefined,
    coverage: parsed.values.coverage as string | undefined,
    namespace: parsed.values.namespace as string | undefined,
    output: parsed.values.output as string | undefined,
    repoRoot: parsed.values["repo-root"] as string | undefined,
  };

  if (!opts.sourceRoot) {
    process.stderr.write("Error: --source-root is required\n");
    return 2;
  }
  const sourceRoot = resolve(opts.sourceRoot);
  if (!existsSync(sourceRoot)) {
    process.stderr.write(`Error: source root does not exist: ${sourceRoot}\n`);
    return 2;
  }

  const namespace =
    opts.namespace ?? process.env.TOTAL_RECALL_NAMESPACE ?? "default";
  const dataRoot = resolveDataRoot(opts.output);
  const nsDir = join(dataRoot, namespace);
  mkdirSync(nsDir, { recursive: true });

  const repoRoot = opts.repoRoot ? resolve(opts.repoRoot) : sourceRoot;
  const records = scanSourceRoot({ sourceRoot, repoRoot });
  writeJsonl(join(nsDir, "type-registry.jsonl"), records);

  let warnings = 0;
  if (records.length === 0) warnings++;

  if (opts.coverage) {
    const cov = parseCobertura(resolve(opts.coverage));
    writeJsonl(join(nsDir, "coverage-gaps.jsonl"), cov);
  }

  if (opts.tests) {
    const testsRoot = resolve(opts.tests);
    if (!existsSync(testsRoot)) {
      process.stderr.write(`Warning: tests path does not exist: ${testsRoot}\n`);
      warnings++;
    } else {
      const inv = scanTests(testsRoot, repoRoot);
      writeJsonl(join(nsDir, "test-inventory.jsonl"), inv);
    }
  }

  const config = {
    schemaVersion: SCHEMA_VERSION,
    scanner: SCANNER_NAME,
    scannerVersion: SCANNER_VERSION,
    lang: "typescript",
    sourceRoot,
    repoRoot,
    coveragePath: opts.coverage ? resolve(opts.coverage) : null,
    testsPath: opts.tests ? resolve(opts.tests) : null,
    lastScanUtc: new Date().toISOString(),
  };
  writeFileSync(join(nsDir, "config.json"), JSON.stringify(config, null, 2));

  return warnings > 0 ? 1 : 0;
}

function resolveDataRoot(explicit: string | undefined): string {
  if (explicit) return resolve(explicit);
  const envRoot = process.env.TOTAL_RECALL_DATA;
  if (envRoot) return resolve(envRoot);
  return resolve("data");
}

// Entry point when invoked as a bin script.
if (
  import.meta.url === `file://${process.argv[1]}` ||
  import.meta.url.endsWith(process.argv[1]?.replace(/\\/g, "/") ?? "")
) {
  process.exit(main(process.argv.slice(2)));
}

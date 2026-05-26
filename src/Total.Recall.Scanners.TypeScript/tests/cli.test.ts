import { describe, it, expect } from "vitest";
import { mkdtempSync, existsSync, readFileSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

import { main } from "../src/cli.js";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "..", "..", "..");
const fixtureSrc = resolve(
  repoRoot,
  "tests",
  "conformance",
  "fixtures",
  "typescript-sample",
  "src",
);

describe("cli", () => {
  it("version sub-command exits 0", () => {
    const rc = main(["version"]);
    expect(rc).toBe(0);
  });

  it("scan writes type-registry.jsonl and config.json", () => {
    const out = mkdtempSync(join(tmpdir(), "trts-cli-"));
    const rc = main([
      "scan",
      "--source-root",
      fixtureSrc,
      "--namespace",
      "fixture",
      "--output",
      out,
      "--repo-root",
      repoRoot,
    ]);
    expect(rc).toBe(0);
    const nsDir = join(out, "fixture");
    expect(existsSync(join(nsDir, "type-registry.jsonl"))).toBe(true);
    expect(existsSync(join(nsDir, "config.json"))).toBe(true);
    const cfg = JSON.parse(readFileSync(join(nsDir, "config.json"), "utf8"));
    expect(cfg.scanner).toBe("total-recall-scan-ts");
    expect(cfg.lang).toBe("typescript");
    expect(cfg.schemaVersion).toBe(1);
    const lines = readFileSync(join(nsDir, "type-registry.jsonl"), "utf8").split("\n");
    expect(lines.length).toBeGreaterThan(0);
    for (const line of lines) {
      const rec = JSON.parse(line);
      expect(rec.schemaVersion).toBe(1);
      expect(rec.lang.kind).toBe("typescript");
    }
  });

  it("scan with a missing source root exits 2", () => {
    const out = mkdtempSync(join(tmpdir(), "trts-cli-"));
    const rc = main([
      "scan",
      "--source-root",
      join(out, "does-not-exist"),
      "--namespace",
      "x",
      "--output",
      out,
    ]);
    expect(rc).toBe(2);
  });

  it("scan over an empty directory exits 1 with a zero-byte registry", () => {
    const empty = mkdtempSync(join(tmpdir(), "trts-empty-src-"));
    const out = mkdtempSync(join(tmpdir(), "trts-cli-"));
    const rc = main([
      "scan",
      "--source-root",
      empty,
      "--namespace",
      "empty",
      "--output",
      out,
    ]);
    expect(rc).toBe(1);
    const reg = join(out, "empty", "type-registry.jsonl");
    expect(existsSync(reg)).toBe(true);
    expect(statSync(reg).size).toBe(0);
  });
});

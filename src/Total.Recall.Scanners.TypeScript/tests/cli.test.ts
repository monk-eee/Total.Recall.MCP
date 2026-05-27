import { describe, it, expect, vi } from "vitest";
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

  it("scan over an empty directory exits 0, writes a zero-byte registry and prints a warning", () => {
    // Regression: 0.1.0 returned exit 1 here, which broke CI bootstrap on
    // fresh repos where the scanner runs before any source has landed.
    // Contract (0.1.1+): exit 0 on success, warnings surfaced on stderr but
    // do not fail the command.
    const empty = mkdtempSync(join(tmpdir(), "trts-empty-src-"));
    const out = mkdtempSync(join(tmpdir(), "trts-cli-"));
    const errSpy = vi.spyOn(process.stderr, "write").mockImplementation(() => true);
    try {
      const rc = main([
        "scan",
        "--source-root",
        empty,
        "--namespace",
        "empty",
        "--output",
        out,
      ]);
      expect(rc).toBe(0);
      const reg = join(out, "empty", "type-registry.jsonl");
      expect(existsSync(reg)).toBe(true);
      expect(statSync(reg).size).toBe(0);
      const writes = errSpy.mock.calls.map((c) => String(c[0])).join("");
      expect(writes).toMatch(/no TypeScript files found/i);
    } finally {
      errSpy.mockRestore();
    }
  });

  it("scan with a missing --tests dir exits 0 and prints a warning", () => {
    // Regression: 0.1.0 returned exit 1 when --tests pointed at a path that
    // didn't exist (common when scanning a repo whose test dir is named
    // differently or hasn't been created yet). Contract (0.1.1+): warnings
    // print on stderr but exit code stays 0; reserve non-zero for actual
    // filesystem errors (missing --source-root) and validation errors.
    const out = mkdtempSync(join(tmpdir(), "trts-cli-"));
    const errSpy = vi.spyOn(process.stderr, "write").mockImplementation(() => true);
    try {
      const rc = main([
        "scan",
        "--source-root",
        fixtureSrc,
        "--tests",
        join(out, "does-not-exist"),
        "--namespace",
        "missing-tests",
        "--output",
        out,
        "--repo-root",
        repoRoot,
      ]);
      expect(rc).toBe(0);
      // Type registry still written from the real source-root.
      expect(existsSync(join(out, "missing-tests", "type-registry.jsonl"))).toBe(true);
      const writes = errSpy.mock.calls.map((c) => String(c[0])).join("");
      expect(writes).toMatch(/tests path does not exist/i);
    } finally {
      errSpy.mockRestore();
    }
  });
});

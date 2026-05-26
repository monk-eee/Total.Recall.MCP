/**
 * Test inventory walker — finds `*.test.ts`, `*.test.tsx`, `*.spec.ts`,
 * `*.spec.tsx`, extracts `describe(...)` / `it(...)` / `test(...)` blocks,
 * infers the class-under-test from the file basename.
 */

import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep, extname, basename } from "node:path";

const SCHEMA_VERSION = 1;
const SKIP_DIRS = new Set(["node_modules", "dist", "build", "coverage", ".git"]);

export interface TestInventoryRecord {
  schemaVersion: number;
  className: string;
  testFiles: string[];
  testMethods: string[];
  testFramework: "vitest";
}

export function scanTests(testsRoot: string, repoRoot?: string): TestInventoryRecord[] {
  let stat;
  try {
    stat = statSync(testsRoot);
  } catch {
    return [];
  }
  if (!stat.isDirectory()) return [];

  const repo = repoRoot ?? testsRoot;
  const grouped = new Map<string, TestInventoryRecord>();

  for (const file of walkTests(testsRoot)) {
    const stem = basename(file).replace(/\.(test|spec)\.tsx?$/i, "");
    const className = toPascal(stem);
    const text = readFileSync(file, "utf8");
    const tests = extractTests(text);

    const relPath = relative(repo, file).split(sep).join("/");
    const existing = grouped.get(className);
    if (existing) {
      if (!existing.testFiles.includes(relPath)) existing.testFiles.push(relPath);
      for (const t of tests) if (!existing.testMethods.includes(t)) existing.testMethods.push(t);
    } else {
      grouped.set(className, {
        schemaVersion: SCHEMA_VERSION,
        className,
        testFiles: [relPath],
        testMethods: tests,
        testFramework: "vitest",
      });
    }
  }

  const records = Array.from(grouped.values());
  records.sort((a, b) => a.className.localeCompare(b.className));
  return records;
}

function* walkTests(root: string): Generator<string> {
  let entries: import("node:fs").Dirent[];
  try {
    entries = readdirSync(root, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    const full = join(root, entry.name);
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name)) continue;
      yield* walkTests(full);
    } else if (entry.isFile() && /\.(test|spec)\.tsx?$/i.test(entry.name)) {
      yield full;
    }
  }
}

function extractTests(text: string): string[] {
  const names: string[] = [];
  // Match it("...") / test("...") / it('...') etc. Best-effort, regex-based.
  const re = /\b(it|test)\s*\(\s*(['"`])((?:\\\2|(?!\2).)+?)\2/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    names.push(m[3]!);
  }
  return names;
}

function toPascal(name: string): string {
  return name
    .split(/[-_.]/g)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join("");
}

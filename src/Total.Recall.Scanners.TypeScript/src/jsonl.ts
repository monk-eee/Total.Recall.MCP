/**
 * JSONL writer — byte-identical to the .NET and Python scanners.
 *
 * Compact JSON (no spaces), one record per line, joined with "\n",
 * no trailing newline. Empty input produces a zero-byte file.
 */

import { writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";

export function writeJsonl(path: string, records: ReadonlyArray<unknown>): number {
  mkdirSync(dirname(path), { recursive: true });
  if (records.length === 0) {
    writeFileSync(path, "");
    return 0;
  }
  const body = records.map((r) => JSON.stringify(r)).join("\n");
  writeFileSync(path, body);
  return records.length;
}

/**
 * Cobertura coverage parser — matches the Python / .NET scanner shape.
 *
 * Tolerant of missing / malformed XML (returns []). When both <lines>
 * and <methods> are present, <lines> is the source of truth for class
 * totals (matches coverage.py / istanbul output); <methods> only
 * contributes the per-symbol uncovered-line breakdown.
 */

import { readFileSync } from "node:fs";
import { XMLParser } from "fast-xml-parser";

const SCHEMA_VERSION = 1;

export interface CoverageRecord {
  schemaVersion: number;
  className: string;
  linesCovered: number;
  linesTotal: number;
  coveragePercent: number;
  uncoveredMethods: { methodName: string; uncoveredLines: number[]; totalLines: number }[];
  existingTests: null;
  testabilityScore: null;
}

export function parseCobertura(xmlPath: string): CoverageRecord[] {
  let raw: string;
  try {
    raw = readFileSync(xmlPath, "utf8");
  } catch {
    return [];
  }
  let parsed: any;
  try {
    const parser = new XMLParser({
      ignoreAttributes: false,
      attributeNamePrefix: "@_",
      allowBooleanAttributes: true,
      isArray: (name) => ["package", "class", "method", "line"].includes(name),
    });
    parsed = parser.parse(raw);
  } catch {
    return [];
  }

  const packages = parsed?.coverage?.packages?.package ?? [];
  const records: CoverageRecord[] = [];
  for (const pkg of packages) {
    const classes = pkg?.classes?.class ?? [];
    for (const cls of classes) {
      records.push(classRecord(cls));
    }
  }
  records.sort((a, b) => a.className.localeCompare(b.className));
  return records;
}

function classRecord(cls: any): CoverageRecord {
  const className = cls?.["@_name"] ?? "";
  let linesTotal = 0;
  let linesCovered = 0;
  const uncoveredMethods: CoverageRecord["uncoveredMethods"] = [];

  const linesNode = cls?.lines?.line;
  if (Array.isArray(linesNode)) {
    for (const line of linesNode) {
      linesTotal++;
      const hits = Number(line?.["@_hits"] ?? 0);
      if (hits > 0) linesCovered++;
    }
  }

  const methodsNode = cls?.methods?.method;
  if (Array.isArray(methodsNode)) {
    for (const method of methodsNode) {
      const methodName = method?.["@_name"] ?? "";
      const methodLines = method?.lines?.line ?? [];
      const lines = Array.isArray(methodLines) ? methodLines : [methodLines].filter(Boolean);
      const uncovered: number[] = [];
      let total = 0;
      for (const line of lines) {
        total++;
        const hits = Number(line?.["@_hits"] ?? 0);
        if (hits === 0) uncovered.push(Number(line?.["@_number"] ?? 0));
      }
      if (methodName) {
        uncoveredMethods.push({ methodName, uncoveredLines: uncovered, totalLines: total });
        if (!Array.isArray(linesNode)) {
          linesTotal += total;
          linesCovered += total - uncovered.length;
        }
      }
    }
  }

  const coveragePercent =
    linesTotal === 0 ? 0 : Math.round((linesCovered / linesTotal) * 10000) / 100;

  return {
    schemaVersion: SCHEMA_VERSION,
    className,
    linesCovered,
    linesTotal,
    coveragePercent,
    uncoveredMethods,
    existingTests: null,
    testabilityScore: null,
  };
}

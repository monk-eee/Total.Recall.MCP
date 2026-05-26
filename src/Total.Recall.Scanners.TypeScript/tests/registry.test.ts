import { describe, it, expect } from "vitest";
import { fileURLToPath } from "node:url";
import { dirname, resolve, join } from "node:path";
import { readFileSync, mkdtempSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";

import { scanSourceRoot, type TypeRecord } from "../src/registry.js";
import { writeJsonl } from "../src/jsonl.js";
import { parseCobertura } from "../src/coverage.js";
import { scanTests } from "../src/tests-inv.js";

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

function byName(records: TypeRecord[], name: string): TypeRecord {
  const r = records.find((x) => x.name === name);
  if (!r) throw new Error(`record not found: ${name}`);
  return r;
}

describe("registry", () => {
  const records = scanSourceRoot({ sourceRoot: fixtureSrc, repoRoot });

  it("emits records for the expected top-level symbols", () => {
    const names = records.map((r) => `${r.name}:${r.kind}`).sort();
    expect(names).toContain("Order:class");
    expect(names).toContain("Money:class");
    expect(names).toContain("OrderStatus:enum");
    expect(names).toContain("User:interface");
    expect(names).toContain("OrderRepo:interface");
    expect(names).toContain("UserRepo:interface");
    expect(names).toContain("OrderService:class");
    expect(names).toContain("OrderServiceBase:class");
    expect(names).toContain("calculateDiscount:function");
    expect(names).toContain("OrderId:type-alias");
    expect(names).toContain("_InternalCache:class");
  });

  it("every record carries schemaVersion=1 and lang.kind=typescript", () => {
    for (const r of records) {
      expect(r.schemaVersion).toBe(1);
      expect(r.lang.kind).toBe("typescript");
    }
  });

  it("detects exports vs non-exports for isInternal", () => {
    expect(byName(records, "Order").isInternal).toBe(false);
    expect(byName(records, "Order").lang.isExported).toBe(true);
    expect(byName(records, "_InternalCache").isInternal).toBe(true);
    expect(byName(records, "_InternalCache").lang.isExported).toBeFalsy();
  });

  it("detects abstract classes", () => {
    expect(byName(records, "OrderServiceBase").isAbstract).toBe(true);
    expect(byName(records, "OrderService").isAbstract).toBe(false);
  });

  it("captures heritage: baseType and interfaces", () => {
    const svc = byName(records, "OrderService");
    expect(svc.baseType).toBe("OrderServiceBase");
    expect(svc.interfaces).toEqual(["UserRepo"]);
  });

  it("captures generics on interface and function", () => {
    expect(byName(records, "OrderRepo").lang.generics).toEqual(["T"]);
    expect(byName(records, "OrderRepo").kind).toBe("interface");
  });

  it("captures enum values", () => {
    const e = byName(records, "OrderStatus");
    expect(e.enumValues).toEqual(["Pending", "Approved", "Shipped", "Cancelled"]);
    expect(e.isEnum).toBe(true);
  });

  it("emits constructor params with types and defaults", () => {
    const money = byName(records, "Money");
    expect(money.constructors.length).toBe(1);
    expect(money.constructors[0]!.params).toContain("amount: number");
    expect(money.constructors[0]!.params).toContain("currency: string");
    expect(money.constructors[0]!.params).toContain('= "USD"');
  });

  it("captures parameter-properties as class properties", () => {
    const order = byName(records, "Order");
    const propNames = order.properties.map((p) => p.name);
    expect(propNames).toContain("orderId");
    expect(propNames).toContain("user");
    expect(propNames).toContain("status");
  });

  it("function records carry their signature via constructors[0].params", () => {
    const fn = byName(records, "calculateDiscount");
    expect(fn.kind).toBe("function");
    expect(fn.constructors.length).toBe(1);
    expect(fn.constructors[0]!.params).toContain("amount: number");
    expect(fn.constructors[0]!.params).toContain("percent: number = 10");
  });

  it("filePath is forward-slashed and repo-relative", () => {
    for (const r of records) {
      expect(r.filePath).not.toContain("\\");
      expect(r.filePath.startsWith("tests/conformance/fixtures/typescript-sample/")).toBe(true);
    }
  });

  it("fullUsing references the file without extension", () => {
    const svc = byName(records, "OrderService");
    expect(svc.fullUsing).toBe(
      'import { OrderService } from "tests/conformance/fixtures/typescript-sample/src/api";',
    );
  });

  it("sort order is deterministic by (namespace, name, kind)", () => {
    const sorted = [...records].sort((a, b) => {
      const c1 = a.namespace.localeCompare(b.namespace);
      if (c1 !== 0) return c1;
      const c2 = a.name.localeCompare(b.name);
      if (c2 !== 0) return c2;
      return a.kind.localeCompare(b.kind);
    });
    expect(records.map((r) => r.name)).toEqual(sorted.map((r) => r.name));
  });
});

describe("jsonl writer", () => {
  it("writes compact JSONL without trailing newline", () => {
    const dir = mkdtempSync(join(tmpdir(), "trts-"));
    const path = join(dir, "x.jsonl");
    writeJsonl(path, [{ a: 1 }, { b: 2 }]);
    const buf = readFileSync(path);
    expect(buf).toEqual(Buffer.from('{"a":1}\n{"b":2}'));
  });

  it("writes a zero-byte file for empty input", () => {
    const dir = mkdtempSync(join(tmpdir(), "trts-"));
    const path = join(dir, "empty.jsonl");
    writeJsonl(path, []);
    expect(statSync(path).size).toBe(0);
  });
});

describe("coverage parser", () => {
  it("returns [] for a missing file", () => {
    expect(parseCobertura("/does/not/exist.xml")).toEqual([]);
  });

  it("parses a minimal Cobertura document", () => {
    const dir = mkdtempSync(join(tmpdir(), "trts-cov-"));
    const xmlPath = join(dir, "coverage.xml");
    const xml = `<?xml version="1.0" ?>
<coverage>
  <packages>
    <package name="sample">
      <classes>
        <class name="sample.models" filename="src/models.ts">
          <methods>
            <method name="Order.total">
              <lines>
                <line number="40" hits="0"/>
                <line number="41" hits="0"/>
              </lines>
            </method>
          </methods>
          <lines>
            <line number="40" hits="0"/>
            <line number="41" hits="0"/>
            <line number="42" hits="3"/>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;
    writeFileSync(xmlPath, xml);
    const records = parseCobertura(xmlPath);
    expect(records).toHaveLength(1);
    const rec = records[0]!;
    expect(rec.schemaVersion).toBe(1);
    expect(rec.className).toBe("sample.models");
    expect(rec.linesCovered).toBe(1);
    expect(rec.linesTotal).toBe(3);
    expect(rec.coveragePercent).toBeCloseTo(33.33, 1);
    expect(rec.uncoveredMethods[0]!.uncoveredLines).toEqual([40, 41]);
  });
});

describe("test inventory", () => {
  it("returns [] for a missing directory", () => {
    expect(scanTests("/does/not/exist")).toEqual([]);
  });
});

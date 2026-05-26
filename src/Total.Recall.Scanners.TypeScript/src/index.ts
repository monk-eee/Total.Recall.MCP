export { writeJsonl } from "./jsonl.js";
export { scanSourceRoot, type TypeRecord, type ScanOptions } from "./registry.js";
export { parseCobertura, type CoverageRecord } from "./coverage.js";
export { scanTests, type TestInventoryRecord } from "./tests-inv.js";

export const SCHEMA_VERSION = 1;
export const SCANNER_VERSION = "0.1.0";
export const SCANNER_NAME = "total-recall-scan-ts";

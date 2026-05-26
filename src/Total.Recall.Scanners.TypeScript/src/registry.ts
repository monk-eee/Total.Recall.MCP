/**
 * TypeScript source scanner.
 *
 * Walks a source root with the TypeScript compiler API (single-file
 * mode — no project resolution) and emits canonical Total.Recall
 * `type-registry.jsonl` records. One record per top-level class,
 * interface, enum, function, and type alias. Records are sorted by
 * `(namespace, name, kind)` for deterministic golden-file diffs.
 */

import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep, extname, basename } from "node:path";
import ts from "typescript";

const SCHEMA_VERSION = 1;
const SCANNER_VERSION = "0.1.0";

const SKIP_DIRS = new Set([
  "node_modules",
  "dist",
  "build",
  "out",
  ".next",
  ".nuxt",
  ".turbo",
  "coverage",
  ".git",
  ".hg",
  ".vscode",
  ".idea",
]);

const SOURCE_EXTS = new Set([".ts", ".tsx"]);

export interface ScanOptions {
  sourceRoot: string;
  repoRoot?: string;
}

export interface TypeRecord {
  schemaVersion: number;
  name: string;
  namespace: string;
  kind: string;
  filePath: string;
  baseType?: string;
  interfaces: string[];
  constructors: { params: string }[];
  properties: { name: string; type?: string; hasInit: boolean }[];
  enumValues?: string[];
  isAbstract: boolean;
  isInterface: boolean;
  isEnum: boolean;
  isStatic: boolean;
  isInternal: boolean;
  fullUsing: string;
  lang: {
    kind: "typescript";
    isExported?: boolean;
    isAmbient?: boolean;
    isReadonlyClass?: boolean;
    generics?: string[];
  };
}

export function scanSourceRoot(opts: ScanOptions): TypeRecord[] {
  const records: TypeRecord[] = [];
  const repoRoot = opts.repoRoot ?? opts.sourceRoot;

  for (const file of walkSources(opts.sourceRoot)) {
    const text = readFileSync(file, "utf8");
    const sf = ts.createSourceFile(file, text, ts.ScriptTarget.ES2022, true, fileScriptKind(file));
    const relPath = relative(repoRoot, file).split(sep).join("/");
    const namespace = inferNamespace(opts.sourceRoot, file);

    for (const stmt of sf.statements) {
      const rec = recordFor(stmt, relPath, namespace);
      if (rec) records.push(rec);
    }
  }

  records.sort((a, b) => {
    const c1 = a.namespace.localeCompare(b.namespace);
    if (c1 !== 0) return c1;
    const c2 = a.name.localeCompare(b.name);
    if (c2 !== 0) return c2;
    return a.kind.localeCompare(b.kind);
  });
  return records;
}

function fileScriptKind(file: string): ts.ScriptKind {
  const ext = extname(file).toLowerCase();
  if (ext === ".tsx") return ts.ScriptKind.TSX;
  return ts.ScriptKind.TS;
}

function* walkSources(root: string): Generator<string> {
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
      yield* walkSources(full);
    } else if (entry.isFile()) {
      const ext = extname(entry.name).toLowerCase();
      if (!SOURCE_EXTS.has(ext)) continue;
      if (entry.name.endsWith(".d.ts")) continue;
      yield full;
    }
  }
}

function inferNamespace(sourceRoot: string, file: string): string {
  const rel = relative(sourceRoot, file).split(sep);
  rel.pop(); // drop the filename
  return rel.join(".");
}

function recordFor(stmt: ts.Statement, filePath: string, namespace: string): TypeRecord | null {
  if (ts.isClassDeclaration(stmt) && stmt.name) {
    return classRecord(stmt, filePath, namespace);
  }
  if (ts.isInterfaceDeclaration(stmt)) {
    return interfaceRecord(stmt, filePath, namespace);
  }
  if (ts.isEnumDeclaration(stmt)) {
    return enumRecord(stmt, filePath, namespace);
  }
  if (ts.isFunctionDeclaration(stmt) && stmt.name) {
    return functionRecord(stmt, filePath, namespace);
  }
  if (ts.isTypeAliasDeclaration(stmt)) {
    return typeAliasRecord(stmt, filePath, namespace);
  }
  return null;
}

function hasModifier(node: ts.Node, kind: ts.SyntaxKind): boolean {
  const mods = ts.canHaveModifiers(node) ? ts.getModifiers(node) : undefined;
  return !!mods?.some((m) => m.kind === kind);
}

function classRecord(node: ts.ClassDeclaration, filePath: string, namespace: string): TypeRecord {
  const name = node.name!.text;
  const isExported = hasModifier(node, ts.SyntaxKind.ExportKeyword);
  const isAmbient = hasModifier(node, ts.SyntaxKind.DeclareKeyword);
  const isAbstract = hasModifier(node, ts.SyntaxKind.AbstractKeyword);

  let baseType: string | undefined;
  const interfaces: string[] = [];
  for (const h of node.heritageClauses ?? []) {
    if (h.token === ts.SyntaxKind.ExtendsKeyword && h.types.length > 0) {
      baseType = h.types[0]!.expression.getText();
    } else if (h.token === ts.SyntaxKind.ImplementsKeyword) {
      for (const t of h.types) interfaces.push(t.expression.getText());
    }
  }

  const ctors: { params: string }[] = [];
  const properties: { name: string; type?: string; hasInit: boolean }[] = [];
  let everyMemberReadonly = node.members.length > 0;
  let isStatic = node.members.length > 0;

  for (const m of node.members) {
    if (ts.isConstructorDeclaration(m)) {
      ctors.push({ params: renderParameters(m.parameters) });
      // Parameter properties also become properties.
      for (const p of m.parameters) {
        if (
          p.modifiers?.some(
            (mod) =>
              mod.kind === ts.SyntaxKind.PublicKeyword ||
              mod.kind === ts.SyntaxKind.PrivateKeyword ||
              mod.kind === ts.SyntaxKind.ProtectedKeyword ||
              mod.kind === ts.SyntaxKind.ReadonlyKeyword,
          )
        ) {
          properties.push({
            name: paramName(p),
            type: p.type?.getText(),
            hasInit: !!p.initializer,
          });
        }
      }
    } else if (ts.isPropertyDeclaration(m) && m.name) {
      properties.push({
        name: m.name.getText(),
        type: m.type?.getText(),
        hasInit: !!m.initializer,
      });
      if (!hasModifier(m, ts.SyntaxKind.ReadonlyKeyword)) everyMemberReadonly = false;
      if (!hasModifier(m, ts.SyntaxKind.StaticKeyword)) isStatic = false;
    } else {
      // Methods etc — they disqualify "all readonly" and "all static".
      everyMemberReadonly = false;
      if (!hasModifier(m, ts.SyntaxKind.StaticKeyword)) isStatic = false;
    }
  }

  const generics = (node.typeParameters ?? []).map((tp) => tp.name.text);

  return {
    schemaVersion: SCHEMA_VERSION,
    name,
    namespace,
    kind: "class",
    filePath,
    baseType,
    interfaces,
    constructors: ctors,
    properties,
    isAbstract,
    isInterface: false,
    isEnum: false,
    isStatic,
    isInternal: !isExported,
    fullUsing: makeFullUsing(filePath, name),
    lang: {
      kind: "typescript",
      isExported,
      isAmbient,
      isReadonlyClass: everyMemberReadonly && properties.length > 0,
      generics: generics.length > 0 ? generics : undefined,
    },
  };
}

function interfaceRecord(node: ts.InterfaceDeclaration, filePath: string, namespace: string): TypeRecord {
  const name = node.name.text;
  const isExported = hasModifier(node, ts.SyntaxKind.ExportKeyword);
  const isAmbient = hasModifier(node, ts.SyntaxKind.DeclareKeyword);

  const interfaces: string[] = [];
  for (const h of node.heritageClauses ?? []) {
    if (h.token === ts.SyntaxKind.ExtendsKeyword) {
      for (const t of h.types) interfaces.push(t.expression.getText());
    }
  }

  const properties: { name: string; type?: string; hasInit: boolean }[] = [];
  for (const m of node.members) {
    if (ts.isPropertySignature(m) && m.name) {
      properties.push({
        name: m.name.getText(),
        type: m.type?.getText(),
        hasInit: false,
      });
    }
  }

  const generics = (node.typeParameters ?? []).map((tp) => tp.name.text);

  return {
    schemaVersion: SCHEMA_VERSION,
    name,
    namespace,
    kind: "interface",
    filePath,
    interfaces,
    constructors: [],
    properties,
    isAbstract: true,
    isInterface: true,
    isEnum: false,
    isStatic: false,
    isInternal: !isExported,
    fullUsing: makeFullUsing(filePath, name),
    lang: {
      kind: "typescript",
      isExported,
      isAmbient,
      generics: generics.length > 0 ? generics : undefined,
    },
  };
}

function enumRecord(node: ts.EnumDeclaration, filePath: string, namespace: string): TypeRecord {
  const name = node.name.text;
  const isExported = hasModifier(node, ts.SyntaxKind.ExportKeyword);
  const isAmbient = hasModifier(node, ts.SyntaxKind.DeclareKeyword);

  const enumValues = node.members.map((m) => (m.name as ts.Identifier).text ?? m.name.getText());

  return {
    schemaVersion: SCHEMA_VERSION,
    name,
    namespace,
    kind: "enum",
    filePath,
    interfaces: [],
    constructors: [],
    properties: [],
    enumValues,
    isAbstract: false,
    isInterface: false,
    isEnum: true,
    isStatic: false,
    isInternal: !isExported,
    fullUsing: makeFullUsing(filePath, name),
    lang: { kind: "typescript", isExported, isAmbient },
  };
}

function functionRecord(node: ts.FunctionDeclaration, filePath: string, namespace: string): TypeRecord {
  const name = node.name!.text;
  const isExported = hasModifier(node, ts.SyntaxKind.ExportKeyword);
  const isAmbient = hasModifier(node, ts.SyntaxKind.DeclareKeyword);
  const generics = (node.typeParameters ?? []).map((tp) => tp.name.text);

  return {
    schemaVersion: SCHEMA_VERSION,
    name,
    namespace,
    kind: "function",
    filePath,
    interfaces: [],
    constructors: [{ params: renderParameters(node.parameters) }],
    properties: [],
    isAbstract: false,
    isInterface: false,
    isEnum: false,
    isStatic: false,
    isInternal: !isExported,
    fullUsing: makeFullUsing(filePath, name),
    lang: {
      kind: "typescript",
      isExported,
      isAmbient,
      generics: generics.length > 0 ? generics : undefined,
    },
  };
}

function typeAliasRecord(node: ts.TypeAliasDeclaration, filePath: string, namespace: string): TypeRecord {
  const name = node.name.text;
  const isExported = hasModifier(node, ts.SyntaxKind.ExportKeyword);
  const isAmbient = hasModifier(node, ts.SyntaxKind.DeclareKeyword);
  const generics = (node.typeParameters ?? []).map((tp) => tp.name.text);

  return {
    schemaVersion: SCHEMA_VERSION,
    name,
    namespace,
    kind: "type-alias",
    filePath,
    interfaces: [],
    constructors: [],
    properties: [],
    isAbstract: false,
    isInterface: false,
    isEnum: false,
    isStatic: false,
    isInternal: !isExported,
    fullUsing: makeFullUsing(filePath, name),
    lang: {
      kind: "typescript",
      isExported,
      isAmbient,
      generics: generics.length > 0 ? generics : undefined,
    },
  };
}

function renderParameters(params: ts.NodeArray<ts.ParameterDeclaration>): string {
  return params
    .map((p) => {
      const n = paramName(p);
      const t = p.type ? `: ${p.type.getText()}` : "";
      const q = p.questionToken ? "?" : "";
      const d = p.initializer ? ` = ${p.initializer.getText()}` : "";
      const r = p.dotDotDotToken ? "..." : "";
      return `${r}${n}${q}${t}${d}`;
    })
    .join(", ");
}

function paramName(p: ts.ParameterDeclaration): string {
  if (ts.isIdentifier(p.name)) return p.name.text;
  return p.name.getText();
}

function makeFullUsing(filePath: string, name: string): string {
  const noExt = filePath.replace(/\.(tsx?|jsx?)$/i, "");
  return `import { ${name} } from "${noExt}";`;
}

export const __internal = { SCHEMA_VERSION, SCANNER_VERSION };

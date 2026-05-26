"""AST-based Python symbol scanner.

Walks every .py file under a source root, parses it with ``ast``, and emits
canonical ``type-registry.jsonl`` records. Schema is documented in
``docs/SCANNER_SCHEMA.md`` (sibling repo).

Design notes:
- Pure stdlib (``ast`` only). No third-party deps. No import-time side
  effects on the target code — we parse source, we do not execute it.
- Files in ``__pycache__``, ``.venv``, ``venv``, ``site-packages``,
  ``build``, ``dist``, ``.tox``, ``.pytest_cache``, ``.mypy_cache``,
  ``.ruff_cache`` and any directory beginning with ``.`` are skipped.
- ``namespace`` is the dotted module path relative to the source root
  (e.g. ``app/services/users.py`` -> ``app.services.users``). For
  ``__init__.py`` we use the package path (``app/services/__init__.py``
  -> ``app.services``).
- ``filePath`` is forward-slashed and repo-relative (relative to the
  source root unless ``repo_root`` is supplied, in which case relative
  to the repo root). This matches the contract in SCANNER_SCHEMA.md.
- The ``lang`` block is always ``{"kind": "python", ...}`` so cross-
  language MCP tools can discriminate cleanly.
"""

from __future__ import annotations

import ast
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Iterator

from . import __schema_version__

_SKIP_DIRS = {
    "__pycache__",
    ".venv",
    "venv",
    "site-packages",
    "build",
    "dist",
    ".tox",
    ".pytest_cache",
    ".mypy_cache",
    ".ruff_cache",
    ".git",
    ".hg",
    "node_modules",
}


@dataclass
class ScanContext:
    """Resolved roots for a single scan invocation."""

    source_root: Path
    repo_root: Path  # for filePath calculation; defaults to source_root


def scan_source_root(
    source_root: str | Path,
    repo_root: str | Path | None = None,
) -> list[dict]:
    """Scan ``source_root`` and return a list of TypeRecord-shaped dicts.

    Records are returned sorted by (namespace, name) for deterministic output
    so conformance tests can diff against golden files line-by-line.
    """
    src = Path(source_root).resolve()
    repo = Path(repo_root).resolve() if repo_root is not None else src
    ctx = ScanContext(source_root=src, repo_root=repo)

    records: list[dict] = []
    for py_file in _iter_python_files(src):
        records.extend(_scan_file(py_file, ctx))

    records.sort(key=lambda r: (r["namespace"], r["name"], r["kind"]))
    return records


def _iter_python_files(root: Path) -> Iterator[Path]:
    """Yield every .py file under ``root``, skipping vendored / cache dirs."""
    for path in root.rglob("*.py"):
        # Skip if any ancestor directory is in the skip list or starts with '.'
        rel_parts = path.relative_to(root).parts[:-1]
        if any(
            part in _SKIP_DIRS or (part.startswith(".") and part not in {".", ".."})
            for part in rel_parts
        ):
            continue
        yield path


def _module_namespace(file_path: Path, source_root: Path) -> str:
    """Convert a file path to its dotted module namespace."""
    rel = file_path.relative_to(source_root)
    parts = list(rel.parts)
    if parts[-1] == "__init__.py":
        parts = parts[:-1]
    else:
        parts[-1] = parts[-1].removesuffix(".py")
    return ".".join(parts)


def _rel_file_path(file_path: Path, repo_root: Path) -> str:
    """Forward-slashed repo-relative path."""
    try:
        rel = file_path.relative_to(repo_root)
    except ValueError:
        rel = file_path
    return rel.as_posix()


def _scan_file(file_path: Path, ctx: ScanContext) -> Iterable[dict]:
    """Parse one file and yield TypeRecord-shaped dicts for each top-level symbol."""
    try:
        source = file_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return

    try:
        tree = ast.parse(source, filename=str(file_path))
    except SyntaxError:
        return

    namespace = _module_namespace(file_path, ctx.source_root)
    rel_path = _rel_file_path(file_path, ctx.repo_root)

    for node in tree.body:
        if isinstance(node, ast.ClassDef):
            yield _record_for_class(node, namespace, rel_path)
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            yield _record_for_function(node, namespace, rel_path)


def _record_for_class(
    node: ast.ClassDef,
    namespace: str,
    rel_path: str,
) -> dict:
    """Render a TypeRecord-shaped dict for a class / Protocol / ABC / dataclass / enum."""
    decorators = [_render_decorator(d) for d in node.decorator_list]
    bases = [_render_expr(b) for b in node.bases]

    is_dataclass = any(d in {"@dataclass", "@dataclasses.dataclass"} for d in decorators) or any(
        d.startswith("@dataclass(") or d.startswith("@dataclasses.dataclass(")
        for d in decorators
    )
    is_frozen = any(
        ("frozen=True" in d) and (d.startswith("@dataclass(") or d.startswith("@dataclasses.dataclass("))
        for d in decorators
    )
    is_protocol = any(b in {"Protocol", "typing.Protocol", "typing_extensions.Protocol"} for b in bases)
    is_abc = any(b in {"ABC", "abc.ABC", "ABCMeta", "abc.ABCMeta"} for b in bases)
    is_enum = any(
        b in {"Enum", "IntEnum", "StrEnum", "Flag", "IntFlag", "enum.Enum", "enum.IntEnum"}
        for b in bases
    )

    # Kind discriminator: Protocol -> protocol, ABC or has abstractmethod -> class+isAbstract
    # Enum -> enum. Plain class -> class.
    has_abstract_method = _has_abstractmethod(node)
    if is_enum:
        kind = "enum"
    elif is_protocol:
        kind = "protocol"
    else:
        kind = "class"

    is_abstract = is_abc or has_abstract_method or is_protocol
    is_internal = node.name.startswith("_")

    constructors = _extract_constructors(node) if not is_enum else []
    properties = _extract_properties(node)
    enum_values = _extract_enum_values(node) if is_enum else None

    # Base type vs interfaces: Python allows multiple inheritance.
    # Strategy: first base becomes `baseType`, remaining go in `interfaces`,
    # but Protocol/ABC bases route to `interfaces` so the discriminator stays
    # meaningful. If the first base IS a Protocol/ABC marker, baseType is null.
    base_type, interfaces = _split_bases(bases)

    record: dict = {
        "schemaVersion": __schema_version__,
        "name": node.name,
        "namespace": namespace,
        "kind": kind,
        "filePath": rel_path,
        "fullUsing": _render_full_using(namespace, node.name),
        "isAbstract": is_abstract,
        "isStatic": False,
        "isInternal": is_internal,
        "isInterface": kind == "protocol",
        "isEnum": is_enum,
        "constructors": constructors,
        "properties": properties,
        "baseType": base_type,
        "interfaces": interfaces,
        "lang": {
            "kind": "python",
            "isDataclass": is_dataclass,
            "isFrozen": is_frozen,
            "isAbc": is_abc,
            "isProtocol": is_protocol,
            "decorators": decorators,
        },
    }
    if enum_values is not None:
        record["enumValues"] = enum_values
    return record


def _record_for_function(
    node: ast.FunctionDef | ast.AsyncFunctionDef,
    namespace: str,
    rel_path: str,
) -> dict:
    """Render a TypeRecord-shaped dict for a top-level function."""
    decorators = [_render_decorator(d) for d in node.decorator_list]
    is_internal = node.name.startswith("_")
    params = _render_arguments(node.args)

    # Functions register as a single "constructor" with the call signature
    # so existing MCP tooling (which reasons over `constructors[0].params`)
    # works without per-kind branching.
    return {
        "schemaVersion": __schema_version__,
        "name": node.name,
        "namespace": namespace,
        "kind": "function",
        "filePath": rel_path,
        "fullUsing": _render_full_using(namespace, node.name),
        "isAbstract": False,
        "isStatic": True,
        "isInternal": is_internal,
        "isInterface": False,
        "isEnum": False,
        "constructors": [{"params": params}],
        "properties": [],
        "baseType": None,
        "interfaces": [],
        "lang": {
            "kind": "python",
            "isDataclass": False,
            "isFrozen": False,
            "isAbc": False,
            "isProtocol": False,
            "decorators": decorators,
        },
    }


def _render_full_using(namespace: str, name: str) -> str:
    if not namespace:
        return f"import {name}"
    return f"from {namespace} import {name}"


def _split_bases(bases: list[str]) -> tuple[str | None, list[str]]:
    """Split rendered base names into (baseType, interfaces[]).

    Protocol/ABC/Enum markers always go in interfaces; remaining first base
    becomes baseType. If only marker bases exist, baseType is None.
    """
    marker_names = {
        "Protocol", "typing.Protocol", "typing_extensions.Protocol",
        "ABC", "abc.ABC", "ABCMeta", "abc.ABCMeta",
        "Enum", "IntEnum", "StrEnum", "Flag", "IntFlag",
        "enum.Enum", "enum.IntEnum", "enum.StrEnum", "enum.Flag", "enum.IntFlag",
    }
    base_type: str | None = None
    interfaces: list[str] = []
    for b in bases:
        if b in marker_names:
            interfaces.append(b)
        elif base_type is None:
            base_type = b
        else:
            interfaces.append(b)
    return base_type, interfaces


def _has_abstractmethod(node: ast.ClassDef) -> bool:
    for child in node.body:
        if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)):
            for d in child.decorator_list:
                rendered = _render_decorator(d)
                if rendered in {"@abstractmethod", "@abc.abstractmethod"}:
                    return True
    return False


def _extract_constructors(node: ast.ClassDef) -> list[dict]:
    """Find __init__ and dataclass-synthesised __init__ signatures."""
    for child in node.body:
        if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)) and child.name == "__init__":
            return [{"params": _render_arguments(child.args, skip_self=True)}]

    # Dataclass: synthesise from class-level annotated assignments.
    decorators = [_render_decorator(d) for d in node.decorator_list]
    is_dataclass = any(
        d == "@dataclass"
        or d == "@dataclasses.dataclass"
        or d.startswith("@dataclass(")
        or d.startswith("@dataclasses.dataclass(")
        for d in decorators
    )
    if is_dataclass:
        params: list[str] = []
        for child in node.body:
            if isinstance(child, ast.AnnAssign) and isinstance(child.target, ast.Name):
                annotation = _render_expr(child.annotation)
                default = f" = {_render_expr(child.value)}" if child.value is not None else ""
                params.append(f"{child.target.id}: {annotation}{default}")
        return [{"params": params}]

    return []


def _extract_properties(node: ast.ClassDef) -> list[dict]:
    """Extract public-API properties.

    Heuristic: class-level annotated assignments (`x: int = 1`) and
    instance attributes assigned in __init__ (`self.x = ...`). Returns
    one record per distinct property name, deduplicated.
    """
    seen: dict[str, dict] = {}

    for child in node.body:
        if isinstance(child, ast.AnnAssign) and isinstance(child.target, ast.Name):
            name = child.target.id
            if name in seen or name.startswith("_"):
                continue
            seen[name] = {
                "name": name,
                "clrType": _render_expr(child.annotation),
                "hasSet": True,
                "hasInit": True,
            }
        elif isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)) and child.name == "__init__":
            for stmt in child.body:
                if not isinstance(stmt, (ast.Assign, ast.AnnAssign)):
                    continue
                targets = stmt.targets if isinstance(stmt, ast.Assign) else [stmt.target]
                for target in targets:
                    if (
                        isinstance(target, ast.Attribute)
                        and isinstance(target.value, ast.Name)
                        and target.value.id == "self"
                        and not target.attr.startswith("_")
                        and target.attr not in seen
                    ):
                        annotation = (
                            _render_expr(stmt.annotation)
                            if isinstance(stmt, ast.AnnAssign)
                            else ""
                        )
                        seen[target.attr] = {
                            "name": target.attr,
                            "clrType": annotation,
                            "hasSet": True,
                            "hasInit": True,
                        }

    return list(seen.values())


def _extract_enum_values(node: ast.ClassDef) -> list[str]:
    values: list[str] = []
    for child in node.body:
        if isinstance(child, ast.Assign):
            for target in child.targets:
                if isinstance(target, ast.Name) and not target.id.startswith("_"):
                    values.append(target.id)
        elif isinstance(child, ast.AnnAssign) and isinstance(child.target, ast.Name):
            if not child.target.id.startswith("_"):
                values.append(child.target.id)
    return values


def _render_arguments(args: ast.arguments, *, skip_self: bool = False) -> list[str]:
    """Render an ``ast.arguments`` as a list of param strings."""
    rendered: list[str] = []
    positional = list(args.posonlyargs) + list(args.args)
    defaults = list(args.defaults)
    # Align defaults to the tail of positional args.
    default_offset = len(positional) - len(defaults)

    for i, arg in enumerate(positional):
        if skip_self and i == 0 and arg.arg == "self":
            continue
        rendered.append(_render_arg(arg, defaults, i - default_offset))

    if args.vararg is not None:
        rendered.append(f"*{_render_arg(args.vararg, [], -1, type_only=True)}")

    kw_defaults = list(args.kw_defaults)
    for i, arg in enumerate(args.kwonlyargs):
        default = kw_defaults[i] if i < len(kw_defaults) else None
        rendered.append(_render_arg(arg, [default] if default is not None else [], 0))

    if args.kwarg is not None:
        rendered.append(f"**{_render_arg(args.kwarg, [], -1, type_only=True)}")

    return rendered


def _render_arg(
    arg: ast.arg,
    defaults: list[ast.expr | None],
    default_index: int,
    *,
    type_only: bool = False,
) -> str:
    name = arg.arg
    annotation = f": {_render_expr(arg.annotation)}" if arg.annotation is not None else ""
    if type_only:
        return f"{name}{annotation}"
    if 0 <= default_index < len(defaults) and defaults[default_index] is not None:
        return f"{name}{annotation} = {_render_expr(defaults[default_index])}"
    return f"{name}{annotation}"


def _render_decorator(node: ast.expr) -> str:
    return f"@{_render_expr(node)}"


def _render_expr(node: ast.expr | None) -> str:
    """Render an AST expression back to source-ish text.

    Uses ``ast.unparse`` (3.9+). Falls back to a minimal best-effort render.
    Wrapped so we never raise on weird AST shapes — scanners must be lenient.
    """
    if node is None:
        return ""
    try:
        return ast.unparse(node)
    except Exception:
        return getattr(node, "id", "") or ""

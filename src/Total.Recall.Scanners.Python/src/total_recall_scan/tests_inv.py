"""pytest test-file walker -> test-inventory.jsonl.

Walks a tests directory, parses each file with ``ast``, and emits one
record per file. Each test function (``def test_*`` at module scope OR
inside a ``class Test*``) becomes a ``TestEntry``.

Class-under-test inference: filename minus ``_test`` / ``test_`` prefix
and ``.py`` suffix, with snake_case -> PascalCase normalisation. This is
intentionally lossy (matches xUnit's convention of one test class per
SUT class); agents can refine via ``get_test_inventory``.

Schema documented in docs/SCANNER_SCHEMA.md section 3.
"""

from __future__ import annotations

import ast
from pathlib import Path
from typing import Iterator

from . import __schema_version__


def scan_tests(
    tests_root: str | Path,
    repo_root: str | Path | None = None,
) -> list[dict]:
    """Scan ``tests_root`` and return TestInventoryEntry-shaped dicts."""
    root = Path(tests_root).resolve()
    if not root.exists() or not root.is_dir():
        return []
    repo = Path(repo_root).resolve() if repo_root is not None else root

    records: list[dict] = []
    for test_file in _iter_test_files(root):
        record = _record_for_file(test_file, repo)
        if record is not None and record["tests"]:
            records.append(record)

    records.sort(key=lambda r: (r["className"], r["testFilePath"]))
    return records


def _iter_test_files(root: Path) -> Iterator[Path]:
    for path in root.rglob("*.py"):
        name = path.name
        if name.startswith("test_") or name.endswith("_test.py"):
            yield path


def _record_for_file(file_path: Path, repo_root: Path) -> dict | None:
    try:
        source = file_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    try:
        tree = ast.parse(source, filename=str(file_path))
    except SyntaxError:
        return None

    tests: list[dict] = []

    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if node.name.startswith("test_"):
                tests.append({
                    "name": node.name,
                    "kind": "test",
                    "lineNumber": node.lineno,
                })
        elif isinstance(node, ast.ClassDef) and node.name.startswith("Test"):
            for child in node.body:
                if (
                    isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef))
                    and child.name.startswith("test_")
                ):
                    tests.append({
                        "name": f"{node.name}.{child.name}",
                        "kind": "test",
                        "lineNumber": child.lineno,
                    })

    if not tests:
        return None

    return {
        "schemaVersion": __schema_version__,
        "className": _infer_class_under_test(file_path),
        "testFilePath": _rel_path(file_path, repo_root),
        "testFramework": "pytest",
        "tests": tests,
    }


def _infer_class_under_test(file_path: Path) -> str:
    stem = file_path.stem
    if stem.startswith("test_"):
        stem = stem[len("test_"):]
    elif stem.endswith("_test"):
        stem = stem[: -len("_test")]
    return _snake_to_pascal(stem)


def _snake_to_pascal(name: str) -> str:
    parts = [p for p in name.split("_") if p]
    return "".join(p[:1].upper() + p[1:] for p in parts)


def _rel_path(file_path: Path, repo_root: Path) -> str:
    try:
        rel = file_path.relative_to(repo_root)
    except ValueError:
        rel = file_path
    return rel.as_posix()

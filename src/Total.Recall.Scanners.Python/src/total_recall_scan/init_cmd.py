"""Auto-discovery + scaffolding for ``total-recall-py init``.

Walks a target Python repo and works out:

- the production source root (``src/<pkg>`` or top-level ``<pkg>``)
- the tests directory (``tests/`` / ``test/``)
- the newest Cobertura coverage XML (``coverage.xml`` / ``coverage.cobertura.xml``)
- a suggested namespace (the repo folder name, sanitised)

Writes ``<data-root>/<namespace>/config.json`` and prints a ready-to-paste
``.vscode/mcp.json`` block plus the exact ``scan`` command the user should
run next. Mirrors the .NET ``total-recall init`` UX so cross-language users
get the same shape of output.
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import TextIO

from . import __version__


@dataclass(frozen=True)
class Discovery:
    """The result of walking a target repo. Any field may be ``None`` if the
    artefact could not be located — callers should warn but not fail."""

    repo_root: Path
    source_root: Path
    tests_path: Path | None
    coverage_path: Path | None
    pyproject_path: Path | None
    package_name: str | None
    suggested_namespace: str
    notes: list[str] = field(default_factory=list)


_NAMESPACE_RE = re.compile(r"[^a-zA-Z0-9._-]+")
_SKIP_DIRS = {
    ".git", ".hg", ".svn",
    ".venv", "venv", "env",
    "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache",
    "node_modules", "dist", "build", ".tox", ".nox",
    "site-packages", ".eggs",
}


def discover(repo_path: Path) -> Discovery:
    """Walk ``repo_path`` and infer the canonical layout."""

    repo_root = repo_path.resolve()
    if not repo_root.exists() or not repo_root.is_dir():
        raise FileNotFoundError(f"repo path does not exist: {repo_root}")

    pyproject = repo_root / "pyproject.toml"
    package_name = _read_package_name(pyproject) if pyproject.exists() else None

    source_root = _find_source_root(repo_root, package_name)
    tests_path = _find_tests(repo_root)
    coverage_path = _find_coverage(repo_root)
    suggested_ns = _suggest_namespace(repo_root, package_name)

    notes: list[str] = []
    if source_root == repo_root:
        notes.append(
            "Could not infer a narrower source root than the repo root. "
            "Pass --source-root explicitly if that picks up too much."
        )
    if tests_path is None:
        notes.append("No tests/ or test/ directory found.")
    if coverage_path is None:
        notes.append(
            "No coverage.xml found. Run "
            "`pytest --cov=<pkg> --cov-report=xml` to generate one."
        )

    return Discovery(
        repo_root=repo_root,
        source_root=source_root,
        tests_path=tests_path,
        coverage_path=coverage_path,
        pyproject_path=pyproject if pyproject.exists() else None,
        package_name=package_name,
        suggested_namespace=suggested_ns,
        notes=notes,
    )


def _find_source_root(repo_root: Path, package_name: str | None) -> Path:
    # Preferred: src-layout. `src/<pkg>/__init__.py` or just `src/`.
    src_dir = repo_root / "src"
    if src_dir.is_dir():
        return src_dir

    # Flat layout: `<repo>/<pkg>/__init__.py`. Use the package dir if we know it.
    if package_name:
        candidate = repo_root / package_name.replace("-", "_")
        if (candidate / "__init__.py").exists():
            return candidate

    # Fallback: scan for any top-level dir containing __init__.py, skipping junk.
    top_level_pkgs = [
        d for d in repo_root.iterdir()
        if d.is_dir()
        and d.name not in _SKIP_DIRS
        and not d.name.startswith(".")
        and (d / "__init__.py").exists()
    ]
    if len(top_level_pkgs) == 1:
        return top_level_pkgs[0]

    # Last resort: the repo root itself. Caller will warn.
    return repo_root


def _find_tests(repo_root: Path) -> Path | None:
    for name in ("tests", "test"):
        candidate = repo_root / name
        if candidate.is_dir():
            return candidate
    return None


def _find_coverage(repo_root: Path) -> Path | None:
    """Pick the newest Cobertura XML anywhere under the repo, skipping junk dirs."""
    candidates: list[Path] = []
    for name in ("coverage.cobertura.xml", "coverage.xml"):
        candidates.extend(_walk_for(repo_root, name))
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def _walk_for(root: Path, filename: str) -> list[Path]:
    found: list[Path] = []
    stack = [root]
    while stack:
        current = stack.pop()
        try:
            for entry in current.iterdir():
                if entry.is_dir():
                    if entry.name in _SKIP_DIRS or entry.name.startswith("."):
                        continue
                    stack.append(entry)
                elif entry.name == filename:
                    found.append(entry)
        except PermissionError:
            continue
    return found


def _read_package_name(pyproject: Path) -> str | None:
    try:
        text = pyproject.read_text(encoding="utf-8")
    except OSError:
        return None
    # Cheap regex over `name = "..."` under [project] — avoids a tomllib import
    # cycle on 3.10 and dodges malformed-toml crashes.
    match = re.search(r'(?m)^\s*name\s*=\s*"([^"]+)"', text)
    return match.group(1) if match else None


def _suggest_namespace(repo_root: Path, package_name: str | None) -> str:
    raw = package_name or repo_root.name
    cleaned = _NAMESPACE_RE.sub("-", raw).strip("-._")
    return cleaned.lower() or "default"


def write_config(
    data_dir: Path,
    discovery: Discovery,
) -> Path:
    """Persist ``config.json`` for the namespace. Returns the written path."""
    data_dir.mkdir(parents=True, exist_ok=True)
    config_path = data_dir / "config.json"

    existing: dict[str, object] = {}
    if config_path.exists():
        try:
            existing = json.loads(config_path.read_text(encoding="utf-8"))
            if not isinstance(existing, dict):
                existing = {}
        except (OSError, json.JSONDecodeError):
            existing = {}

    payload: dict[str, object] = {
        "schemaVersion": 1,
        "scanner": "total-recall-scan-py",
        "scannerVersion": __version__,
        "lang": "python",
        "sourceRoot": str(discovery.source_root),
        "repoRoot": str(discovery.repo_root),
        "coveragePath": str(discovery.coverage_path) if discovery.coverage_path else None,
        "testsPath": str(discovery.tests_path) if discovery.tests_path else None,
        "lastScanUtc": existing.get("lastScanUtc"),
        "writtenByInitUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    config_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
    return config_path


def build_mcp_json(namespace: str, data_root: Path, source_root: Path) -> str:
    block = {
        "servers": {
            "Total.Recall": {
                "type": "stdio",
                "command": "total-recall",
                "env": {
                    "TOTAL_RECALL_DATA": str(data_root),
                    "TOTAL_RECALL_NAMESPACE": namespace,
                    "TOTAL_RECALL_LOG_LEVEL": "info",
                    "TOTAL_RECALL_SOURCE_ROOT": str(source_root),
                },
            }
        }
    }
    return json.dumps(block, indent=2)


def build_scan_command(discovery: Discovery, namespace: str) -> str:
    parts = [
        "total-recall-py scan",
        f'--source-root "{discovery.source_root}"',
    ]
    if discovery.coverage_path:
        parts.append(f'--coverage "{discovery.coverage_path}"')
    if discovery.tests_path:
        parts.append(f'--tests "{discovery.tests_path}"')
    parts.append(f"--namespace {namespace}")
    return " ".join(parts)


def print_report(
    out: TextIO,
    discovery: Discovery,
    namespace: str,
    data_root: Path,
    data_dir: Path,
    config_path: Path,
) -> None:
    def _show(p: Path | None) -> str:
        return str(p) if p else "(not found)"

    out.write(f"Total.Recall Python init v{__version__}\n\n")
    out.write("-- Discovered --\n")
    out.write(f"  repo root      : {discovery.repo_root}\n")
    out.write(f"  source root    : {discovery.source_root}\n")
    out.write(f"  package        : {discovery.package_name or '(none from pyproject)'}\n")
    out.write(f"  tests directory: {_show(discovery.tests_path)}\n")
    out.write(f"  coverage XML   : {_show(discovery.coverage_path)}\n")
    out.write(f"  pyproject.toml : {_show(discovery.pyproject_path)}\n\n")
    out.write("-- Resolved --\n")
    out.write(f"  namespace      : {namespace}\n")
    out.write(f"  data root      : {data_root}\n")
    out.write(f"  data dir       : {data_dir}\n")
    out.write(f"  config.json    : {config_path} (written)\n\n")

    if discovery.notes:
        out.write("-- Warnings --\n")
        for note in discovery.notes:
            out.write(f"  ! {note}\n")
        out.write("\n")

    out.write("-- Suggested .vscode/mcp.json --\n\n")
    out.write(build_mcp_json(namespace, data_root, discovery.source_root))
    out.write("\n\n")

    out.write("-- Next steps --\n")
    out.write("  1. Run the scanner to populate data:\n")
    out.write(f"       {build_scan_command(discovery, namespace)}\n")
    out.write("  2. Paste the JSON block above into .vscode/mcp.json in your target workspace.\n")
    out.write("  3. Restart VS Code, then ask Copilot: \"get testable targets, top 5\".\n")
    out.write("  4. Re-run scans on demand, or use `--watch` to keep data fresh:\n")
    out.write(f"       {build_scan_command(discovery, namespace)} --watch\n")


_UNSAFE_NS = re.compile(r"[/\\]|\.\.")


def is_safe_namespace(ns: str) -> bool:
    if not ns or ns in (".", ".."):
        return False
    if _UNSAFE_NS.search(ns):
        return False
    return bool(re.fullmatch(r"[A-Za-z0-9._-]+", ns))


def run_init(
    repo_path: Path,
    *,
    namespace_override: str | None,
    data_root: Path,
    out: TextIO = sys.stdout,
) -> int:
    """Drive the init flow end-to-end. Returns a CLI exit code."""
    try:
        discovery = discover(repo_path)
    except FileNotFoundError as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 2

    namespace = (namespace_override or discovery.suggested_namespace).strip()
    if not is_safe_namespace(namespace):
        print(
            f"error: namespace '{namespace}' is not a safe path segment. "
            "Use only letters, digits, '-', '_', '.'.",
            file=sys.stderr,
        )
        return 1

    data_dir = (data_root / namespace).resolve()
    config_path = write_config(data_dir, discovery)
    print_report(out, discovery, namespace, data_root.resolve(), data_dir, config_path)

    # Init succeeded: config.json is written and the mcp.json block is printed.
    # Discovery warnings (e.g. missing tests/ or coverage.xml) are surfaced in
    # the printed report but do NOT fail the command — those are the normal
    # state of a fresh repo and CI bootstrap steps depend on init returning 0.
    # Reserve exit 1 for genuine errors (validation failures) and exit 2 for
    # filesystem errors. See docs/TODO.md history and CHANGELOG 0.1.1.
    return 0

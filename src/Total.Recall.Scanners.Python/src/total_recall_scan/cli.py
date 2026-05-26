"""Total.Recall Python scanner CLI.

Sub-commands:
- ``scan`` — full source / coverage / tests scan, writes JSONL to
  ``<output>/<namespace>/``.
- ``version`` — print package version.

Output dir resolution mirrors the .NET scanner: if ``--output`` is
supplied it wins; otherwise the env var ``TOTAL_RECALL_DATA`` is
consulted; otherwise ``./data`` is used. The final data directory is
``<output>/<namespace>``.

Exit codes:
- 0 success
- 1 partial success (warnings — e.g. no test files found)
- 2 hard error (source root missing / unparseable)
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

from . import __version__
from .coverage import parse_cobertura
from .jsonl import write_jsonl
from .registry import scan_source_root
from .tests_inv import scan_tests


def main(argv: list[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)

    if args.command == "version":
        print(__version__)
        return 0

    if args.command == "scan":
        return _run_scan(args)

    parser.print_help()
    return 0


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="total-recall-py",
        description=(
            "Python source scanner for Total.Recall. Emits canonical JSONL "
            "records (type-registry, coverage-gaps, test-inventory) for the "
            "Total.Recall MCP server. See docs/SCANNER_SCHEMA.md for the contract."
        ),
    )
    sub = parser.add_subparsers(dest="command")

    scan = sub.add_parser("scan", help="Run a scan and write JSONL output.")
    scan.add_argument("--source-root", required=True, help="Directory containing production Python source.")
    scan.add_argument("--coverage", default=None, help="Path to a Cobertura coverage XML file (optional).")
    scan.add_argument("--tests", default=None, help="Directory containing pytest test files (optional).")
    scan.add_argument(
        "--namespace",
        default=os.environ.get("TOTAL_RECALL_NAMESPACE", "default"),
        help="Namespace subdirectory under the data root.",
    )
    scan.add_argument(
        "--output",
        default=None,
        help="Root data directory (defaults to TOTAL_RECALL_DATA env var, then ./data).",
    )
    scan.add_argument(
        "--repo-root",
        default=None,
        help="Repo root for filePath calculation (defaults to --source-root).",
    )

    sub.add_parser("version", help="Print version and exit.")

    return parser


def _run_scan(args: argparse.Namespace) -> int:
    source_root = Path(args.source_root).resolve()
    if not source_root.exists() or not source_root.is_dir():
        print(f"error: --source-root does not exist: {source_root}", file=sys.stderr)
        return 2

    output_root = _resolve_output_root(args.output)
    data_dir = output_root / args.namespace
    data_dir.mkdir(parents=True, exist_ok=True)

    repo_root = Path(args.repo_root).resolve() if args.repo_root else source_root

    # 1. Symbol registry
    registry_records = scan_source_root(source_root, repo_root=repo_root)
    write_jsonl(data_dir / "type-registry.jsonl", registry_records)
    print(f"type-registry.jsonl: {len(registry_records)} records")

    # 2. Coverage gaps (optional)
    coverage_count = 0
    if args.coverage:
        coverage_records = parse_cobertura(args.coverage)
        write_jsonl(data_dir / "coverage-gaps.jsonl", coverage_records)
        coverage_count = len(coverage_records)
        print(f"coverage-gaps.jsonl: {coverage_count} records")

    # 3. Test inventory (optional)
    tests_count = 0
    if args.tests:
        tests_path = Path(args.tests).resolve()
        if tests_path.exists():
            test_records = scan_tests(tests_path, repo_root=repo_root)
            write_jsonl(data_dir / "test-inventory.jsonl", test_records)
            tests_count = len(test_records)
            print(f"test-inventory.jsonl: {tests_count} records")
        else:
            print(f"warning: --tests path does not exist: {tests_path}", file=sys.stderr)

    # 4. Persist config.json so `total-recall doctor` can validate later.
    _write_config(
        data_dir / "config.json",
        source_root=source_root,
        coverage_path=args.coverage,
        tests_path=args.tests,
        repo_root=repo_root,
    )

    # Warning exit if scanner produced an empty registry — likely misconfigured.
    if len(registry_records) == 0:
        print("warning: no Python symbols found under --source-root", file=sys.stderr)
        return 1
    return 0


def _resolve_output_root(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).resolve()
    env = os.environ.get("TOTAL_RECALL_DATA")
    if env:
        return Path(env).resolve()
    return Path("data").resolve()


def _write_config(
    config_path: Path,
    *,
    source_root: Path,
    coverage_path: str | None,
    tests_path: str | None,
    repo_root: Path,
) -> None:
    payload = {
        "schemaVersion": 1,
        "scanner": "total-recall-scan-py",
        "scannerVersion": __version__,
        "lang": "python",
        "sourceRoot": str(source_root),
        "repoRoot": str(repo_root),
        "coveragePath": coverage_path,
        "testsPath": tests_path,
        "lastScanUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    config_path.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

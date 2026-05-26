"""CLI smoke tests — exercise `total-recall-py scan` end-to-end."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from total_recall_scan.cli import main


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _fixture_src() -> Path:
    return _repo_root() / "tests" / "conformance" / "fixtures" / "python-sample" / "src"


def test_version_command(capsys):
    rc = main(["version"])
    out = capsys.readouterr().out.strip()
    assert rc == 0
    assert out  # something printed


def test_scan_writes_expected_files(tmp_path: Path, capsys):
    output = tmp_path / "data"
    rc = main([
        "scan",
        "--source-root", str(_fixture_src()),
        "--namespace", "fixture",
        "--output", str(output),
        "--repo-root", str(_repo_root()),
    ])
    assert rc == 0, capsys.readouterr().err

    ns_dir = output / "fixture"
    assert (ns_dir / "type-registry.jsonl").exists()
    assert (ns_dir / "config.json").exists()

    # Config persists the inputs and stamps the scanner identity.
    cfg = json.loads((ns_dir / "config.json").read_text(encoding="utf-8"))
    assert cfg["scanner"] == "total-recall-scan-py"
    assert cfg["lang"] == "python"
    assert cfg["schemaVersion"] == 1
    assert cfg["sourceRoot"]

    # Type registry has well-formed JSONL.
    lines = (ns_dir / "type-registry.jsonl").read_text(encoding="utf-8").splitlines()
    assert lines
    for line in lines:
        record = json.loads(line)
        assert record["schemaVersion"] == 1
        assert record["lang"]["kind"] == "python"


def test_scan_missing_source_root_returns_exit_code_2(tmp_path: Path, capsys):
    rc = main([
        "scan",
        "--source-root", str(tmp_path / "does-not-exist"),
        "--namespace", "x",
        "--output", str(tmp_path / "data"),
    ])
    assert rc == 2
    assert "does not exist" in capsys.readouterr().err


def test_scan_empty_source_root_returns_exit_code_1(tmp_path: Path):
    empty_src = tmp_path / "empty-src"
    empty_src.mkdir()
    rc = main([
        "scan",
        "--source-root", str(empty_src),
        "--namespace", "empty",
        "--output", str(tmp_path / "data"),
    ])
    assert rc == 1
    # Even when there are no symbols, type-registry.jsonl is created (empty file).
    assert (tmp_path / "data" / "empty" / "type-registry.jsonl").exists()
    assert (tmp_path / "data" / "empty" / "type-registry.jsonl").stat().st_size == 0

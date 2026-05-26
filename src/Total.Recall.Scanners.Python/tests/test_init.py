"""Tests for ``total_recall_scan.init_cmd`` — discovery + report shape."""

from __future__ import annotations

import io
import json
from pathlib import Path

from total_recall_scan.init_cmd import (
    build_mcp_json,
    build_scan_command,
    discover,
    is_safe_namespace,
    run_init,
    write_config,
)


def _make_pyproject(repo: Path, name: str) -> None:
    (repo / "pyproject.toml").write_text(
        f'[project]\nname = "{name}"\nversion = "0.0.1"\n',
        encoding="utf-8",
    )


def test_discover_src_layout(tmp_path: Path) -> None:
    (tmp_path / "src" / "myproj").mkdir(parents=True)
    (tmp_path / "src" / "myproj" / "__init__.py").write_text("")
    (tmp_path / "tests").mkdir()
    _make_pyproject(tmp_path, "myproj")

    result = discover(tmp_path)

    assert result.source_root == tmp_path / "src"
    assert result.tests_path == tmp_path / "tests"
    assert result.package_name == "myproj"
    assert result.suggested_namespace == "myproj"
    assert result.coverage_path is None


def test_discover_flat_layout_uses_package_dir(tmp_path: Path) -> None:
    (tmp_path / "my_pkg").mkdir()
    (tmp_path / "my_pkg" / "__init__.py").write_text("")
    _make_pyproject(tmp_path, "my-pkg")

    result = discover(tmp_path)

    assert result.source_root == tmp_path / "my_pkg"


def test_discover_picks_newest_coverage_xml(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    (tmp_path / "old_results").mkdir()
    old = tmp_path / "old_results" / "coverage.xml"
    new = tmp_path / "coverage.xml"
    old.write_text("<coverage/>")
    new.write_text("<coverage/>")
    import os
    import time
    # Make new strictly newer; mtime resolution on Windows is coarse.
    past = time.time() - 60
    os.utime(old, (past, past))

    result = discover(tmp_path)

    assert result.coverage_path == new


def test_discover_skips_junk_dirs_when_searching_coverage(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    venv = tmp_path / ".venv"
    venv.mkdir()
    (venv / "coverage.xml").write_text("<coverage/>")

    result = discover(tmp_path)

    assert result.coverage_path is None


def test_discover_missing_repo_raises(tmp_path: Path) -> None:
    missing = tmp_path / "no-such-dir"
    try:
        discover(missing)
    except FileNotFoundError:
        pass
    else:
        raise AssertionError("expected FileNotFoundError")


def test_namespace_validation() -> None:
    assert is_safe_namespace("myproj")
    assert is_safe_namespace("my-proj_v2.0")
    assert not is_safe_namespace("../escape")
    assert not is_safe_namespace("/abs")
    assert not is_safe_namespace("a\\b")
    assert not is_safe_namespace("..")
    assert not is_safe_namespace("")
    assert not is_safe_namespace("has space")


def test_build_mcp_json_has_expected_env_keys(tmp_path: Path) -> None:
    block = build_mcp_json("ns1", tmp_path / "data", tmp_path / "src")
    parsed = json.loads(block)
    env = parsed["servers"]["Total.Recall"]["env"]

    assert env["TOTAL_RECALL_NAMESPACE"] == "ns1"
    assert env["TOTAL_RECALL_DATA"] == str(tmp_path / "data")
    assert env["TOTAL_RECALL_SOURCE_ROOT"] == str(tmp_path / "src")
    assert parsed["servers"]["Total.Recall"]["command"] == "total-recall"


def test_build_scan_command_quotes_paths_and_includes_optional_flags(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    (tmp_path / "tests").mkdir()
    cov = tmp_path / "coverage.xml"
    cov.write_text("<coverage/>")
    (tmp_path / "pyproject.toml").write_text('[project]\nname="x"\n')

    d = discover(tmp_path)
    cmd = build_scan_command(d, "ns1")

    assert cmd.startswith("total-recall-py scan ")
    assert f'--source-root "{d.source_root}"' in cmd
    assert f'--coverage "{cov}"' in cmd
    assert "--namespace ns1" in cmd


def test_write_config_round_trip_and_preserves_last_scan_utc(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    (tmp_path / "tests").mkdir()
    _make_pyproject(tmp_path, "myproj")
    d = discover(tmp_path)
    data_dir = tmp_path / "data" / "myproj"

    # First write: no previous lastScanUtc.
    path1 = write_config(data_dir, d)
    payload1 = json.loads(path1.read_text())
    assert payload1["lang"] == "python"
    assert payload1["scanner"] == "total-recall-scan-py"
    assert payload1["lastScanUtc"] is None

    # Simulate a prior scan stamp, then re-init: should be preserved.
    payload1["lastScanUtc"] = "2025-01-01T00:00:00Z"
    path1.write_text(json.dumps(payload1))

    path2 = write_config(data_dir, d)
    payload2 = json.loads(path2.read_text())
    assert payload2["lastScanUtc"] == "2025-01-01T00:00:00Z"
    assert "writtenByInitUtc" in payload2


def test_write_config_overwrites_corrupt_existing(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    _make_pyproject(tmp_path, "myproj")
    d = discover(tmp_path)
    data_dir = tmp_path / "data" / "myproj"
    data_dir.mkdir(parents=True)
    (data_dir / "config.json").write_text("{not json")

    path = write_config(data_dir, d)
    payload = json.loads(path.read_text())
    assert payload["scanner"] == "total-recall-scan-py"


def test_run_init_writes_config_and_returns_zero_on_full_repo(tmp_path: Path) -> None:
    (tmp_path / "src" / "myproj").mkdir(parents=True)
    (tmp_path / "src" / "myproj" / "__init__.py").write_text("")
    (tmp_path / "tests").mkdir()
    (tmp_path / "coverage.xml").write_text("<coverage/>")
    _make_pyproject(tmp_path, "myproj")
    data_root = tmp_path / "data"
    buf = io.StringIO()

    code = run_init(tmp_path, namespace_override=None, data_root=data_root, out=buf)

    assert code == 0  # no warnings
    config = json.loads((data_root / "myproj" / "config.json").read_text())
    assert config["lang"] == "python"
    output = buf.getvalue()
    assert "Suggested .vscode/mcp.json" in output
    assert "total-recall-py scan" in output


def test_run_init_returns_one_when_coverage_missing(tmp_path: Path) -> None:
    (tmp_path / "src" / "myproj").mkdir(parents=True)
    (tmp_path / "src" / "myproj" / "__init__.py").write_text("")
    (tmp_path / "tests").mkdir()
    _make_pyproject(tmp_path, "myproj")
    buf = io.StringIO()

    code = run_init(tmp_path, namespace_override=None, data_root=tmp_path / "data", out=buf)

    assert code == 1
    assert "coverage.xml" in buf.getvalue()


def test_run_init_rejects_unsafe_namespace(tmp_path: Path, capsys) -> None:
    (tmp_path / "src").mkdir()
    _make_pyproject(tmp_path, "x")
    code = run_init(
        tmp_path,
        namespace_override="../escape",
        data_root=tmp_path / "data",
        out=io.StringIO(),
    )
    captured = capsys.readouterr()
    assert code == 1
    assert "not a safe path segment" in captured.err


def test_run_init_returns_two_for_missing_repo(tmp_path: Path, capsys) -> None:
    code = run_init(
        tmp_path / "missing",
        namespace_override=None,
        data_root=tmp_path / "data",
        out=io.StringIO(),
    )
    captured = capsys.readouterr()
    assert code == 2
    assert "does not exist" in captured.err

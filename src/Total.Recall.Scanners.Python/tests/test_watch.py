"""Tests for ``total_recall_scan.watch`` — file watcher diff + loop."""

from __future__ import annotations

from pathlib import Path

from total_recall_scan.watch import diff_snapshots, snapshot_mtimes, watch


def test_snapshot_collects_python_files_recursively(tmp_path: Path) -> None:
    (tmp_path / "a.py").write_text("")
    (tmp_path / "sub").mkdir()
    (tmp_path / "sub" / "b.py").write_text("")
    (tmp_path / "sub" / "c.txt").write_text("")  # ignored

    snap = snapshot_mtimes([tmp_path])

    keys = {Path(k).name for k in snap}
    assert keys == {"a.py", "b.py"}


def test_snapshot_skips_junk_dirs(tmp_path: Path) -> None:
    (tmp_path / "a.py").write_text("")
    junk = tmp_path / "__pycache__"
    junk.mkdir()
    (junk / "noise.py").write_text("")
    venv = tmp_path / ".venv"
    venv.mkdir()
    (venv / "site.py").write_text("")

    snap = snapshot_mtimes([tmp_path])

    assert {Path(k).name for k in snap} == {"a.py"}


def test_snapshot_handles_missing_roots(tmp_path: Path) -> None:
    missing = tmp_path / "no-such"
    snap = snapshot_mtimes([missing])
    assert snap == {}


def test_snapshot_includes_extra_files(tmp_path: Path) -> None:
    cov = tmp_path / "coverage.xml"
    cov.write_text("<coverage/>")
    snap = snapshot_mtimes([], extra_files=[cov])
    assert str(cov) in snap


def test_diff_detects_modified_created_deleted() -> None:
    before = {"a": 1.0, "b": 2.0}
    after = {"a": 1.0, "b": 99.0, "c": 3.0}
    changes = set(diff_snapshots(before, after))
    assert changes == {"b", "c"}

    after_with_delete = {"a": 1.0}
    assert set(diff_snapshots(before, after_with_delete)) == {"b"}


def test_diff_no_changes_returns_empty() -> None:
    snap = {"a": 1.0, "b": 2.0}
    assert diff_snapshots(snap, dict(snap)) == []


def test_watch_invokes_rescan_on_change(tmp_path: Path) -> None:
    src = tmp_path / "src"
    src.mkdir()
    file_a = src / "a.py"
    file_a.write_text("v1")

    snapshots = iter([
        {str(file_a): 1.0},  # initial snapshot
        {str(file_a): 2.0},  # poll 1: changed
        {str(file_a): 2.0},  # debounce re-snapshot
        {str(file_a): 2.0},  # poll 2: no change
    ])

    rescans: list[list[str]] = []

    watch(
        [src],
        rescan=rescans.append,
        iterations=2,
        sleep=lambda _: None,
        snapshot=lambda *_args, **_kwargs: next(snapshots),
    )

    assert len(rescans) == 1
    assert rescans[0] == [str(file_a)]


def test_watch_skips_rescan_when_quiet(tmp_path: Path) -> None:
    src = tmp_path / "src"
    src.mkdir()
    stable = {str(src / "a.py"): 1.0}

    rescans: list[list[str]] = []
    watch(
        [src],
        rescan=rescans.append,
        iterations=3,
        sleep=lambda _: None,
        snapshot=lambda *_args, **_kwargs: dict(stable),
    )

    assert rescans == []

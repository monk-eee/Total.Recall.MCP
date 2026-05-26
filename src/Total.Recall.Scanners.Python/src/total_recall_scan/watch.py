"""Polling-based file watcher for ``total-recall-py scan --watch``.

Zero runtime dependencies — uses ``os.stat`` mtime snapshots of every
``.py`` file under the source root (and tests dir, if given) plus the
coverage XML if configured. When any mtime changes (or a file appears /
disappears), the supplied ``rescan`` callback is fired. A short debounce
window collapses bursts (e.g. an editor's "save all" or a multi-file
formatter run) into a single rescan.

Polling is fine here: the scanner runs in well under a second on typical
repos, and the watcher only walks file metadata, not contents. For very
large repos that find polling too slow, swap in ``watchfiles`` behind the
same interface.
"""

from __future__ import annotations

import time
from collections.abc import Callable, Iterable
from pathlib import Path

# These are duplicated from ``init_cmd`` deliberately — keeping the watcher
# zero-import from siblings makes it trivially testable in isolation.
_SKIP_DIRS = {
    ".git", ".hg", ".svn",
    ".venv", "venv", "env",
    "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache",
    "node_modules", "dist", "build", ".tox", ".nox",
    "site-packages", ".eggs",
}


def snapshot_mtimes(roots: Iterable[Path], extra_files: Iterable[Path] = ()) -> dict[str, float]:
    """Return a ``{path: mtime}`` map of every ``.py`` file under ``roots``
    plus any explicit ``extra_files`` that currently exist."""
    snap: dict[str, float] = {}
    for root in roots:
        if root is None or not root.exists():
            continue
        if root.is_file():
            try:
                snap[str(root)] = root.stat().st_mtime
            except OSError:
                pass
            continue
        stack = [root]
        while stack:
            current = stack.pop()
            try:
                for entry in current.iterdir():
                    if entry.is_dir():
                        if entry.name in _SKIP_DIRS or entry.name.startswith("."):
                            continue
                        stack.append(entry)
                    elif entry.suffix == ".py":
                        try:
                            snap[str(entry)] = entry.stat().st_mtime
                        except OSError:
                            continue
            except (PermissionError, OSError):
                continue
    for path in extra_files:
        if path and path.exists():
            try:
                snap[str(path)] = path.stat().st_mtime
            except OSError:
                pass
    return snap


def diff_snapshots(before: dict[str, float], after: dict[str, float]) -> list[str]:
    """Return paths that changed: created, deleted, or modified."""
    changes: list[str] = []
    for path, mtime in after.items():
        if before.get(path) != mtime:
            changes.append(path)
    for path in before:
        if path not in after:
            changes.append(path)
    return changes


def watch(
    roots: Iterable[Path],
    rescan: Callable[[list[str]], None],
    *,
    extra_files: Iterable[Path] = (),
    poll_seconds: float = 1.5,
    debounce_seconds: float = 0.5,
    iterations: int | None = None,
    sleep: Callable[[float], None] = time.sleep,
    snapshot: Callable[..., dict[str, float]] = snapshot_mtimes,
) -> None:
    """Block, polling for changes, firing ``rescan(changed_paths)`` on each
    detected burst.

    ``iterations`` exists for the test suite — pass an int to cap loop
    turns; pass ``None`` (default) for the CLI's "run forever" behaviour.
    ``sleep`` and ``snapshot`` are injectable for the same reason.
    """
    roots = list(roots)
    extras = list(extra_files)
    previous = snapshot(roots, extras)
    turn = 0
    while iterations is None or turn < iterations:
        sleep(poll_seconds)
        current = snapshot(roots, extras)
        changes = diff_snapshots(previous, current)
        if changes:
            # Debounce: wait briefly and re-snapshot so a burst of writes
            # (e.g. "save all", `ruff --fix`) collapses into one rescan.
            sleep(debounce_seconds)
            current = snapshot(roots, extras)
            changes = diff_snapshots(previous, current)
            rescan(changes)
            previous = current
        turn += 1

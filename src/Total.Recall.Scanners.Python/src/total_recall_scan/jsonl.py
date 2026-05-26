"""JSONL writer matching the Total.Recall canonical contract.

Per docs/SCANNER_SCHEMA.md:
- One JSON object per line.
- UTF-8, no BOM.
- NO trailing newline after the last record.
- Compact separators (no insignificant whitespace) — matches the .NET writer.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable


def write_jsonl(path: str | Path, records: Iterable[dict[str, Any]]) -> int:
    """Write ``records`` to ``path`` as canonical JSONL. Returns count written.

    Overwrites the target file. Records are serialised with ``ensure_ascii=False``
    so non-ASCII identifiers (Python supports them) survive round-trip, and with
    compact separators to match the .NET ``System.Text.Json`` default.
    """
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)

    materialised = list(records)
    if not materialised:
        # Per spec: empty store is a zero-byte file, not absent.
        path.write_bytes(b"")
        return 0

    lines = [
        json.dumps(rec, ensure_ascii=False, separators=(",", ":"), sort_keys=False)
        for rec in materialised
    ]
    # No trailing newline — the .NET writer omits it on the final record.
    path.write_text("\n".join(lines), encoding="utf-8", newline="")
    return len(lines)

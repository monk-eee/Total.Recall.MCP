"""pytest config — adds the package src dir to sys.path for test discovery.

The package uses the src/ layout (`src/total_recall_scan/`). For `pytest`
runs without `pip install -e .` we need to make `total_recall_scan`
importable; this conftest.py does that explicitly so tests run cleanly
in CI without a prior install step.
"""

from __future__ import annotations

import sys
from pathlib import Path

_SRC = Path(__file__).resolve().parent.parent / "src"
if str(_SRC) not in sys.path:
    sys.path.insert(0, str(_SRC))

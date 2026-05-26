"""Cobertura XML -> coverage-gaps.jsonl converter.

The coverage.py tool emits Cobertura via ``coverage xml``. We parse the
``<class>`` elements (which represent Python modules in coverage.py's
output) and collapse line-level hit counts into a per-class summary plus
``uncoveredMethods`` listing methods whose every line is uncovered.

Schema documented in docs/SCANNER_SCHEMA.md section 2.
"""

from __future__ import annotations

import xml.etree.ElementTree as ET
from pathlib import Path

from . import __schema_version__


def parse_cobertura(xml_path: str | Path) -> list[dict]:
    """Parse a Cobertura XML file and return CoverageGap-shaped dicts.

    Returns an empty list if the file is missing or unparseable — scanners
    should never abort the whole scan on a malformed coverage report.
    """
    path = Path(xml_path)
    if not path.exists():
        return []

    try:
        tree = ET.parse(path)
    except ET.ParseError:
        return []

    root = tree.getroot()
    records: list[dict] = []

    for cls in root.iter("class"):
        record = _record_for_class(cls)
        if record is not None:
            records.append(record)

    records.sort(key=lambda r: r["className"])
    return records


def _record_for_class(cls: ET.Element) -> dict | None:
    name = cls.attrib.get("name") or ""
    filename = cls.attrib.get("filename") or ""
    if not name:
        return None

    lines_total = 0
    lines_covered = 0
    method_uncovered: dict[str, list[int]] = {}
    method_total: dict[str, int] = {}

    # Prefer the class-level <lines> block for totals (it is the source of
    # truth in coverage.py output and includes lines that don't belong to a
    # method). Use <methods> only to attribute uncovered lines to symbols.
    lines_node = cls.find("lines")
    if lines_node is not None:
        for line in lines_node.findall("line"):
            lines_total += 1
            hits = int(line.attrib.get("hits", "0"))
            if hits > 0:
                lines_covered += 1

    methods_node = cls.find("methods")
    if methods_node is not None:
        for method in methods_node.findall("method"):
            method_name = method.attrib.get("name") or ""
            uncovered: list[int] = []
            total = 0
            for line in method.iter("line"):
                total += 1
                hits = int(line.attrib.get("hits", "0"))
                if hits == 0:
                    uncovered.append(int(line.attrib.get("number", "0")))
            if method_name:
                method_uncovered[method_name] = uncovered
                method_total[method_name] = total
                # Fall back to method-level totals only if <lines> was absent.
                if lines_node is None:
                    lines_total += total
                    lines_covered += total - len(uncovered)

    coverage_pct = (
        round((lines_covered / lines_total) * 100.0, 2) if lines_total > 0 else 0.0
    )

    uncovered_methods = [
        {
            "name": method_name,
            "signature": method_name,
            "uncoveredLines": uncovered_lines,
            "totalLines": method_total[method_name],
        }
        for method_name, uncovered_lines in method_uncovered.items()
        if uncovered_lines
    ]

    return {
        "schemaVersion": __schema_version__,
        "className": name,
        "filePath": filename.replace("\\", "/") if filename else "",
        "linesCovered": lines_covered,
        "linesTotal": lines_total,
        "coveragePercent": coverage_pct,
        "uncoveredMethods": uncovered_methods,
        "existingTests": None,
        "testabilityScore": None,
    }

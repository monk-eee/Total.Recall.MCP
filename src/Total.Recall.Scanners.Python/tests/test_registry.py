"""Smoke tests for the Python scanner registry walker.

These tests use the shared conformance fixture under
``tests/conformance/fixtures/python-sample/`` (relative to the repo root)
so that the .NET and TypeScript scanners can diff against identical
source. The fixture path is resolved walking up from this test file.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from total_recall_scan.coverage import parse_cobertura
from total_recall_scan.jsonl import write_jsonl
from total_recall_scan.registry import scan_source_root
from total_recall_scan.tests_inv import scan_tests


def _repo_root() -> Path:
    # tests/test_registry.py -> Total.Recall.Scanners.Python -> src -> repo root
    return Path(__file__).resolve().parents[3]


def _fixture_src() -> Path:
    return _repo_root() / "tests" / "conformance" / "fixtures" / "python-sample" / "src"


def test_scan_fixture_produces_expected_symbols():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    names = {(r["namespace"], r["name"], r["kind"]) for r in records}

    # Spot-check a handful of expected symbols. The full golden file diff
    # lives in tests/conformance/ — here we just enforce shape.
    assert ("sample.models", "User", "class") in names
    assert ("sample.models", "Money", "class") in names
    assert ("sample.models", "OrderStatus", "enum") in names
    assert ("sample.models", "Order", "class") in names
    assert ("sample.api", "UserRepo", "protocol") in names
    assert ("sample.api", "OrderRepo", "class") in names
    assert ("sample.api", "OrderService", "class") in names
    assert ("sample.api", "calculate_discount", "function") in names


def test_every_record_carries_schema_v1_and_python_lang():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    assert records, "fixture produced no records"
    for r in records:
        assert r["schemaVersion"] == 1, f"missing schemaVersion on {r['name']}"
        assert r["lang"]["kind"] == "python", f"wrong lang.kind on {r['name']}"
        # Every record must carry the required canonical fields.
        required = {
            "name", "namespace", "kind", "filePath", "fullUsing",
            "isAbstract", "isStatic", "isInternal", "isInterface", "isEnum",
            "constructors", "properties", "baseType", "interfaces", "lang",
        }
        missing = required - r.keys()
        assert not missing, f"{r['name']} missing fields: {missing}"


def test_dataclass_detection():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    user = by_name[("sample.models", "User")]
    assert user["lang"]["isDataclass"] is True
    assert user["lang"]["isFrozen"] is False

    money = by_name[("sample.models", "Money")]
    assert money["lang"]["isDataclass"] is True
    assert money["lang"]["isFrozen"] is True


def test_protocol_kind_and_flags():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    user_repo = by_name[("sample.api", "UserRepo")]
    assert user_repo["kind"] == "protocol"
    assert user_repo["isInterface"] is True
    assert user_repo["isAbstract"] is True
    assert user_repo["lang"]["isProtocol"] is True
    assert user_repo["lang"]["isAbc"] is False


def test_abc_detection():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    order_repo = by_name[("sample.api", "OrderRepo")]
    assert order_repo["kind"] == "class"
    assert order_repo["isAbstract"] is True
    assert order_repo["lang"]["isAbc"] is True
    assert order_repo["lang"]["isProtocol"] is False


def test_enum_values_emitted():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    status = by_name[("sample.models", "OrderStatus")]
    assert status["kind"] == "enum"
    assert status["isEnum"] is True
    assert status["enumValues"] == ["PENDING", "APPROVED", "SHIPPED", "CANCELLED"]


def test_internal_class_flagged():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    internal = by_name[("sample.models", "_InternalCache")]
    assert internal["isInternal"] is True


def test_constructor_params_rendered_with_types_and_defaults():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    svc = by_name[("sample.api", "OrderService")]
    assert svc["constructors"], "OrderService should have a constructor"
    params = svc["constructors"][0]["params"]
    assert "users: UserRepo" in params
    assert "orders: OrderRepo" in params
    assert any(p.startswith("default_currency: str") and "USD" in p for p in params)


def test_function_record_uses_function_kind():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    fn = by_name[("sample.api", "calculate_discount")]
    assert fn["kind"] == "function"
    assert fn["constructors"], "function should expose its signature via constructors[0]"
    params = fn["constructors"][0]["params"]
    assert "amount: int" in params
    assert any(p.startswith("percent: float") and "10.0" in p for p in params)


def test_filepath_is_forward_slashed_and_repo_relative():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    for r in records:
        assert "\\" not in r["filePath"], f"backslash in filePath on {r['name']}: {r['filePath']}"
        assert r["filePath"].startswith("tests/conformance/fixtures/python-sample/")


def test_full_using_renders_python_import():
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {(r["namespace"], r["name"]): r for r in records}

    svc = by_name[("sample.api", "OrderService")]
    assert svc["fullUsing"] == "from sample.api import OrderService"


def test_records_are_deterministically_sorted():
    records_a = scan_source_root(_fixture_src(), repo_root=_repo_root())
    records_b = scan_source_root(_fixture_src(), repo_root=_repo_root())
    assert records_a == records_b, "scan output must be deterministic across invocations"


def test_jsonl_writer_no_trailing_newline(tmp_path: Path):
    target = tmp_path / "out.jsonl"
    write_jsonl(target, [{"a": 1}, {"b": 2}])
    blob = target.read_bytes()
    assert blob == b'{"a":1}\n{"b":2}', "writer must omit trailing newline"


def test_jsonl_writer_empty_creates_zero_byte_file(tmp_path: Path):
    target = tmp_path / "empty.jsonl"
    written = write_jsonl(target, [])
    assert written == 0
    assert target.exists()
    assert target.stat().st_size == 0


def test_jsonl_round_trip_via_scanner(tmp_path: Path):
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    target = tmp_path / "type-registry.jsonl"
    written = write_jsonl(target, records)
    assert written == len(records)

    lines = target.read_text(encoding="utf-8").splitlines()
    assert len(lines) == len(records)
    for line in lines:
        record = json.loads(line)
        assert record["schemaVersion"] == 1
        assert record["lang"]["kind"] == "python"


def test_coverage_parser_handles_missing_file_gracefully():
    assert parse_cobertura("/no/such/file.xml") == []


def test_coverage_parser_parses_cobertura(tmp_path: Path):
    xml = """<?xml version="1.0" ?>
<coverage>
  <packages>
    <package name="sample">
      <classes>
        <class name="sample.models" filename="src/sample/models.py">
          <methods>
            <method name="Order.total">
              <lines>
                <line number="40" hits="0"/>
                <line number="41" hits="0"/>
              </lines>
            </method>
          </methods>
          <lines>
            <line number="40" hits="0"/>
            <line number="41" hits="0"/>
            <line number="42" hits="3"/>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"""
    xml_path = tmp_path / "coverage.xml"
    xml_path.write_text(xml, encoding="utf-8")

    records = parse_cobertura(xml_path)
    assert len(records) == 1
    rec = records[0]
    assert rec["schemaVersion"] == 1
    assert rec["className"] == "sample.models"
    assert rec["linesCovered"] == 1
    assert rec["linesTotal"] == 3
    assert rec["coveragePercent"] == pytest.approx(33.33, rel=0.01)
    assert rec["uncoveredMethods"][0]["uncoveredLines"] == [40, 41]


def test_tests_inv_walks_pytest_files(tmp_path: Path):
    tests_dir = tmp_path / "tests"
    tests_dir.mkdir()
    (tests_dir / "test_order.py").write_text(
        "def test_total_zero():\n    assert True\n\n"
        "class TestOrder:\n    def test_when_pending(self):\n        assert True\n",
        encoding="utf-8",
    )
    records = scan_tests(tests_dir, repo_root=tmp_path)
    assert len(records) == 1
    rec = records[0]
    assert rec["schemaVersion"] == 1
    assert rec["testFramework"] == "pytest"
    assert rec["className"] == "Order"
    names = {t["name"] for t in rec["tests"]}
    assert "test_total_zero" in names
    assert "TestOrder.test_when_pending" in names


def test_tests_inv_handles_missing_dir():
    assert scan_tests("/no/such/dir") == []


@pytest.mark.parametrize(
    "underscored,is_internal",
    [("User", False), ("Order", False), ("_InternalCache", True)],
)
def test_internal_flag_matches_leading_underscore_convention(underscored, is_internal):
    records = scan_source_root(_fixture_src(), repo_root=_repo_root())
    by_name = {r["name"]: r for r in records}
    assert by_name[underscored]["isInternal"] is is_internal

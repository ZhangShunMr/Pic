"""Independent numerical anchors plus adversarial domain and workflow tests."""

import copy
import csv
import io
import json
import math
import unittest

from acoustic.analysis import analyze, energy_mean, energy_sum, paired_summary
from acoustic.commands import execute
from acoustic.demo import demonstration
from acoustic.imports import csv_preview, measurements_csv, results_csv
from acoustic.models import ValidationError, empty_project, validate_project
from acoustic.report import report_html
from acoustic.server import dispatch, load_json


class NumericalTests(unittest.TestCase):
    def test_known_difference_70_67(self):
        result = analyze(demonstration())["pairs"][0]
        self.assertEqual(result["bands"][0]["difference"], 3)

    def test_independent_energy_anchors(self):
        self.assertAlmostEqual(energy_mean([60, 70]), 67.403626895, places=9)
        self.assertAlmostEqual(energy_sum([60, 60]), 63.010299957, places=9)

    def test_empty_and_zero(self):
        self.assertIsNone(energy_mean([]))
        self.assertIsNone(energy_sum([]))
        self.assertEqual(energy_mean([0]), 0)
        self.assertAlmostEqual(energy_sum([0, 0]), 3.01029995664)
        self.assertEqual(energy_mean([-100, -100]), -100)

    def test_arithmetic_paired_difference_not_energy_group_difference(self):
        project = demonstration()
        trial_map = {trial["id"]: trial for trial in project["trials"]}
        for trial_id, value in [("b1", 60), ("b2", 70), ("s1", 60), ("s2", 60)]:
            trial_map[trial_id]["bands"] = {"125": value}
        result = analyze(project)["effects"][0]["bands"][0]
        self.assertEqual(result["meanDifference"], 5)
        self.assertAlmostEqual(result["groupEnergyMeanDifference"], 7.40362689494243)
        self.assertNotEqual(result["meanDifference"], result["groupEnergyMeanDifference"])

    def test_n_one_and_reused_pairs_suppress_ci(self):
        self.assertIsNone(paired_summary([3])["ci95"])
        self.assertIsNone(paired_summary([3, 4], False)["ci95"])
        project = demonstration()
        project["pairs"][1]["baselineId"] = "b1"
        result = analyze(project)["effects"][0]
        self.assertFalse(result["independentPairs"])
        self.assertIsNone(result["bands"][0]["ci95"])

    def test_known_student_interval(self):
        result = paired_summary([2, 4])
        self.assertEqual(result["meanDifference"], 3)
        self.assertAlmostEqual(result["ci95"][0], 3 - 12.706204736)
        self.assertAlmostEqual(result["ci95"][1], 3 + 12.706204736)

    def test_combination_uses_own_measured_values(self):
        result = analyze(demonstration())["pairs"]
        self.assertEqual(result[0]["bands"][0]["difference"], 3)
        self.assertEqual(result[2]["bands"][0]["difference"], 2)
        self.assertEqual(result[3]["bands"][0]["difference"], 4.5)
        self.assertNotEqual(result[3]["bands"][0]["difference"], 5)

    def test_negative_difference_preserved(self):
        project = demonstration()
        project["trials"][2]["bands"]["125"] = 72
        self.assertEqual(analyze(project)["pairs"][0]["bands"][0]["difference"], -2)


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.project = demonstration()

    def test_empty_project(self):
        project = validate_project(empty_project())
        self.assertEqual(analyze(project)["counts"]["trials"], 0)

    def test_missing_not_zero(self):
        self.project["trials"][2]["bands"]["125"] = None
        result = analyze(self.project)["pairs"][0]
        self.assertIsNone(result["bands"][0]["difference"])
        self.assertEqual(result["bands"][0]["status"], "missing")
        self.assertIsNone(result["totalDifference"])

    def test_different_band_sets_disable_total(self):
        del self.project["trials"][2]["bands"]["125"]
        result = analyze(self.project)["pairs"][0]
        self.assertIsNone(result["totalDifference"])
        self.assertIn("集合不同", result["totalNote"])

    def test_total_requires_declared_nonoverlapping(self):
        self.project["conditions"][0]["independentBands"] = False
        result = analyze(self.project)["pairs"][0]
        self.assertIsNone(result["baselineTotal"])
        self.assertIsNone(result["totalDifference"])

    def test_all_condition_fields_block_when_missing(self):
        for field in ["vehicle", "microphone", "calibration", "timeWeighting", "unit"]:
            with self.subTest(field=field):
                project = copy.deepcopy(self.project)
                project["conditions"][0][field] = ""
                result = analyze(project)["pairs"][0]
                self.assertEqual(result["status"], "blocked")
                self.assertIsNone(result["bands"][0]["difference"])

    def test_condition_mismatch_and_tolerance_boundary(self):
        alternate = copy.deepcopy(self.project["conditions"][0])
        alternate["id"] = "alternate"
        alternate["speed"] = 0.5
        self.project["conditions"].append(alternate)
        self.project["trials"][2]["conditionId"] = "alternate"
        self.assertEqual(analyze(self.project)["pairs"][0]["status"], "valid")
        alternate["speed"] = 0.5001
        result = analyze(self.project)["pairs"][0]
        self.assertEqual(result["status"], "blocked")
        self.assertIn("车速", result["reasons"][0])

    def test_mixed_sources_block(self):
        self.project["trials"][2]["sourceKind"] = "measured"
        self.assertEqual(analyze(self.project)["pairs"][0]["status"], "blocked")

    def test_invalid_values(self):
        for value in [float("nan"), float("inf"), 201, -101, True, "70"]:
            with self.subTest(value=value):
                project = copy.deepcopy(self.project)
                project["trials"][0]["bands"]["125"] = value
                with self.assertRaises(ValidationError):
                    validate_project(project)

    def test_unknown_version_and_bool_version(self):
        for value in [2, True, "1", None]:
            with self.subTest(value=value):
                self.project["schemaVersion"] = value
                with self.assertRaises(ValidationError):
                    validate_project(self.project)

    def test_invalid_references_and_duplicate_ids(self):
        self.project["trials"][0]["conditionId"] = "missing"
        with self.assertRaises(ValidationError):
            validate_project(self.project)
        project = demonstration()
        project["sites"][1]["id"] = project["sites"][0]["id"]
        with self.assertRaises(ValidationError):
            validate_project(project)

    def test_duplicate_normalized_frequencies(self):
        self.project["trials"][0]["bands"]["125.0"] = 69
        with self.assertRaises(ValidationError):
            validate_project(self.project)

    def test_pair_types_and_timezone(self):
        self.project["pairs"][0]["purpose"] = "recovery"
        with self.assertRaises(ValidationError):
            validate_project(self.project)
        project = demonstration()
        project["trials"][0]["timestamp"] = "2026-01-01T10:00:00"
        with self.assertRaises(ValidationError):
            validate_project(project)

    def test_combination_needs_two_sites(self):
        self.project["trials"][5]["siteIds"] = ["site-door"]
        with self.assertRaises(ValidationError):
            validate_project(self.project)

    def test_json_duplicate_and_nonfinite_rejected(self):
        for text in ['{"x":1,"x":2}', '{"x":NaN}', '{"x":Infinity}', '{bad']:
            with self.subTest(text=text), self.assertRaises(ValidationError):
                load_json(text)


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.project = demonstration()

    def test_reference_deletion_protection(self):
        for collection, item_id in [("sites", "site-door"), ("conditions", "condition-demo"), ("trials", "b1")]:
            with self.subTest(collection=collection), self.assertRaises(ValidationError):
                execute(self.project, "delete", {"collection": collection, "id": item_id})
        self.assertEqual(len(self.project["sites"]), 2)

    def test_save_reopen_and_migration_independence(self):
        result = dispatch("/api/export", {"project": self.project, "kind": "project"})
        reopened = dispatch("/api/open-text", {"content": result["content"]})
        self.assertEqual(reopened["project"], validate_project(self.project))
        self.assertEqual(reopened["analysis"]["pairs"][0]["bands"][0]["difference"], 3)

    def test_archived_readonly_restore_and_clone(self):
        archived = execute(self.project, "archive")["project"]
        with self.assertRaises(ValidationError):
            execute(archived, "metadata", {"name": "changed"})
        restored = execute(archived, "restore")["project"]
        self.assertEqual(restored["status"], "active")
        cloned = execute(archived, "clone")["project"]
        self.assertNotEqual(cloned["id"], self.project["id"])
        self.assertEqual(cloned["status"], "active")

    def test_duplicate_trial_does_not_duplicate_measurements(self):
        result = execute(self.project, "duplicateTrial", {"id": "s1"})
        trial = result["project"]["trials"][-1]
        self.assertEqual(trial["bands"], {})
        self.assertEqual(trial["repeatGroup"], "")
        self.assertEqual(len(result["project"]["pairs"]), 6)

    def test_csv_roundtrip_preserves_missing(self):
        self.project["trials"][0]["bands"]["125"] = None
        text = measurements_csv(self.project)
        preview = csv_preview(self.project, text, "replace")
        self.assertEqual(preview["missing"], 1)
        result = execute(self.project, "import", {"content": text, "mode": "replace"})
        self.assertEqual(result["project"]["trials"][0]["bands"], self.project["trials"][0]["bands"])

    def test_csv_transaction_and_exact_location(self):
        before = copy.deepcopy(self.project)
        text = "trial_id,frequency_hz,level_db\nb1,125,80\nb1,250,NaN\n"
        with self.assertRaisesRegex(ValidationError, r"line\[3\]"):
            execute(self.project, "import", {"content": text, "mode": "merge"})
        self.assertEqual(self.project, before)

    def test_csv_duplicates_unknown_and_bad_header(self):
        samples = [
            "trial_id,frequency_hz,level_db\nb1,125,70\nb1,125.0,67",
            "trial_id,frequency_hz,level_db\nmissing,125,70",
            "trial_id;frequency_hz;level_db\nb1;125;70",
        ]
        for text in samples:
            with self.subTest(text=text), self.assertRaises(ValidationError):
                csv_preview(self.project, text, "merge")

    def test_merge_vs_replace(self):
        text = "trial_id,frequency_hz,level_db\nb1,1000,50"
        merged = execute(self.project, "import", {"content": text, "mode": "merge"})["project"]
        replaced = execute(self.project, "import", {"content": text, "mode": "replace"})["project"]
        self.assertEqual(len(merged["trials"][0]["bands"]), 4)
        self.assertEqual(replaced["trials"][0]["bands"], {"1000": 50})

    def test_drift_recovery_threshold_and_repeatability(self):
        analysis = analyze(self.project)
        self.assertEqual(analysis["pairs"][4]["screening"]["status"], "within")
        self.project["trials"][7]["bands"]["125"] = 73
        self.assertEqual(analyze(self.project)["pairs"][5]["screening"]["status"], "alert")
        self.project["parameters"]["repeatabilityThreshold"] = 0.01
        self.assertEqual(analyze(self.project)["repeatability"][0]["bands"][0]["status"], "alert")

    def test_analysis_audit_includes_blocking_reason(self):
        self.project["conditions"][0]["vehicle"] = ""
        result = execute(self.project, "recordAnalysis")
        self.assertIn("车辆标识缺失", result["project"]["audit"][-1]["detail"])

    def test_report_contains_source_units_block_and_escaped_text(self):
        self.project["name"] = '<script>alert("x")</script>'
        self.project["conditions"][0]["vehicle"] = ""
        report = report_html(self.project)
        self.assertNotIn('<script>alert', report)
        for expected in ["模拟", "非实测", "dB re 20 µPa", "Hz", "车辆标识缺失", "95%", "路径独立能量贡献", "试验时间线"]:
            self.assertIn(expected, report)
        csv_text = results_csv(self.project, analyze(self.project))
        rows = list(csv.reader(io.StringIO(csv_text.lstrip("\ufeff"))))
        self.assertIn("measured_difference_db", rows[0])
        self.assertIn("software_version", rows[0])
        self.assertIn("parameters_json", rows[0])
        self.assertEqual(rows[1][rows[0].index("software_version")], "1.0.0")
        self.assertEqual(rows[1][12], "")

    def test_matrix_and_coverage_keep_missing_distinct(self):
        self.project["trials"][0]["bands"]["125"] = None
        del self.project["trials"][1]["bands"]["125"]
        result = analyze(self.project)
        coverage = result["coverage"]["frequencies"][0]
        self.assertEqual(coverage["missing"], 1)
        self.assertEqual(coverage["absent"], 1)
        self.assertEqual(result["matrix"]["rows"][0]["cells"][0]["status"], "missing")


if __name__ == "__main__":
    unittest.main(verbosity=2)

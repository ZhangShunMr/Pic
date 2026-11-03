"""Coverage, completeness and measured scenario matrix for review screens."""

from collections import defaultdict
from datetime import datetime

from acoustic.models import CONDITION_NUMBERS, CONDITION_TEXT, SOURCES, TRIAL_TYPES


def condition_completeness(condition):
    missing = []
    for key, label in CONDITION_TEXT.items():
        if not condition[key]:
            missing.append(label)
    for key, (_, _, label) in CONDITION_NUMBERS.items():
        if condition[key] is None:
            missing.append(label)
    return {
        "id": condition["id"],
        "name": condition["name"],
        "complete": not missing,
        "missing": missing,
        "trialCount": 0,
    }


def project_coverage(project, results):
    """Coverage is descriptive and never an experimental sufficiency guarantee."""
    paired_targets = defaultdict(list)
    for pair in project["pairs"]:
        paired_targets[pair["targetId"]].append(pair["id"])
    result_map = {result["id"]: result for result in results}
    condition_rows = [condition_completeness(condition) for condition in project["conditions"]]
    condition_rows_map = {condition["id"]: condition for condition in condition_rows}
    type_counts = {key: 0 for key in TRIAL_TYPES}
    source_counts = {key: 0 for key in SOURCES}
    frequency_counts = defaultdict(lambda: {"trials": 0, "missing": 0})
    timeline = []
    for trial in project["trials"]:
        type_counts[trial["type"]] += 1
        source_counts[trial["sourceKind"]] += 1
        condition_rows_map[trial["conditionId"]]["trialCount"] += 1
        for frequency, value in trial["bands"].items():
            frequency_counts[frequency]["trials"] += 1
            if value is None:
                frequency_counts[frequency]["missing"] += 1
        valid = sum(value is not None for value in trial["bands"].values())
        pair_ids = paired_targets[trial["id"]]
        timeline.append({
            "id": trial["id"],
            "name": trial["name"],
            "timestamp": trial["timestamp"],
            "type": trial["type"],
            "sourceKind": trial["sourceKind"],
            "conditionId": trial["conditionId"],
            "validBands": valid,
            "declaredBands": len(trial["bands"]),
            "targetPairs": len(pair_ids),
            "validTargetPairs": sum(result_map[pair_id]["status"] == "valid" for pair_id in pair_ids),
        })
    timeline.sort(key=lambda trial: datetime.fromisoformat(trial["timestamp"].replace("Z", "+00:00")))
    sites = []
    for site in project["sites"]:
        single = [trial for trial in project["trials"] if trial["type"] == "single" and site["id"] in trial["siteIds"]]
        combination = [trial for trial in project["trials"] if trial["type"] == "combination" and site["id"] in trial["siteIds"]]
        valid_effects = [result for result in results if result["purpose"] == "effect" and result["status"] == "valid" and site["id"] in result["siteIds"]]
        sites.append({
            "id": site["id"],
            "name": site["name"],
            "singleTrials": len(single),
            "combinationTrials": len(combination),
            "validEffectPairs": len(valid_effects),
            "missingSingle": not single,
            "missingCombination": not combination,
        })
    frequencies = []
    for frequency, counts in sorted(frequency_counts.items(), key=lambda item: float(item[0])):
        frequencies.append({
            "frequency": float(frequency),
            "declared": counts["trials"],
            "missing": counts["missing"],
            "absent": len(project["trials"]) - counts["trials"],
            "valid": counts["trials"] - counts["missing"],
        })
    return {
        "typeCounts": type_counts,
        "sourceCounts": source_counts,
        "conditions": condition_rows,
        "sites": sites,
        "frequencies": frequencies,
        "timeline": timeline,
        "note": "覆盖统计只描述已录入记录，不能证明样本充分、工况有效或封堵可复现。",
    }


def scenario_matrix(results):
    """Keep per-pair results separate: no additive prediction or pooled ranking."""
    effects = [result for result in results if result["purpose"] == "effect"]
    frequencies = sorted({band["frequency"] for result in effects for band in result["bands"]})
    columns = []
    for result in effects:
        columns.append({
            "pairId": result["id"],
            "baselineName": result["baselineName"],
            "targetName": result["targetName"],
            "sourceKind": result["sourceKind"],
            "targetType": result["targetType"],
            "siteIds": result["siteIds"],
            "status": result["status"],
            "reasons": result["reasons"],
        })
    rows = []
    for frequency in frequencies:
        cells = []
        for result in effects:
            band = next((item for item in result["bands"] if item["frequency"] == frequency), None)
            cells.append({
                "pairId": result["id"],
                "difference": band["difference"] if band else None,
                "status": band["status"] if band else "missing",
            })
        rows.append({"frequency": frequency, "cells": cells})
    return {
        "columns": columns,
        "rows": rows,
        "note": "横向展示各显式配对的测得声级差；各列基线、工况和来源可能不同，不作独立贡献排序。",
    }

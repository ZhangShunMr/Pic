"""Measured level differences and engineering screening, with no path inference."""

from __future__ import annotations

import math
import statistics
from collections import defaultdict
from datetime import datetime

from acoustic.models import CONDITION_TEXT, validate_project
from acoustic.review import project_coverage, scenario_matrix

# Two-sided Student t 0.975 quantiles, degrees of freedom 1 through 30.
# Algorithm reference is documented in docs/算法与文件格式.md.
T975 = (
    12.706204736, 4.30265273, 3.182446305, 2.776445105, 2.570581836,
    2.446911851, 2.364624252, 2.306004135, 2.262157163, 2.228138852,
    2.20098516, 2.17881283, 2.160368656, 2.144786688, 2.131449546,
    2.119905299, 2.109815578, 2.10092204, 2.093024054, 2.085963447,
    2.079613845, 2.073873068, 2.06865761, 2.063898562, 2.059538553,
    2.055529439, 2.051830516, 2.048407142, 2.045229642, 2.042272456,
)
LIMITATION = (
    "结果为封堵前后测得声级差，受试验条件、测量不确定性和路径耦合影响；"
    "不是路径独立能量贡献。单项分贝不可相加预测组合，组合效果须使用组合实测。"
)


def energy_mean(levels):
    """Equal-weight energetic mean, stable across the allowed level range."""
    if not levels:
        return None
    peak = max(levels)
    scaled = math.fsum(10 ** ((level - peak) / 10) for level in levels)
    return peak + 10 * math.log10(scaled / len(levels))


def energy_sum(levels):
    """Sum of nonoverlapping band energies; never a sum of effect differences."""
    if not levels:
        return None
    peak = max(levels)
    return peak + 10 * math.log10(math.fsum(10 ** ((level - peak) / 10) for level in levels))


def describe(values):
    count = len(values)
    if not count:
        return {
            "n": 0,
            "mean": None,
            "energyMean": None,
            "sd": None,
            "range": None,
            "minimum": None,
            "maximum": None,
        }
    return {
        "n": count,
        "mean": statistics.mean(values),
        "energyMean": energy_mean(values),
        "sd": statistics.stdev(values) if count > 1 else None,
        "range": max(values) - min(values),
        "minimum": min(values),
        "maximum": max(values),
    }


def paired_summary(differences, independent=True):
    count = len(differences)
    mean = statistics.mean(differences) if count else None
    sd = statistics.stdev(differences) if count > 1 else None
    result = {
        "n": count,
        "meanDifference": mean,
        "sdDifference": sd,
        "ci95": None,
        "ciNote": "n < 2，不生成置信区间",
    }
    if count > 1 and not independent:
        result["ciNote"] = "配对复用试验，独立性不满足，不生成置信区间"
    elif count > 1:
        # For df > 30 use the conservative df=30 critical value explicitly.
        critical = T975[min(count - 1, 30) - 1]
        half = critical * sd / math.sqrt(count)
        result["ci95"] = [mean - half, mean + half]
        result["ciNote"] = (
            "基于独立配对差近似正态的双侧 95% Student t 区间；"
            + ("自由度大于30时保守使用df=30" if count > 31 else f"df={count - 1}")
            + "；未计仪器系统不确定性"
        )
    return result


def compatible(left, right, parameters):
    reasons = []
    for key, label in CONDITION_TEXT.items():
        a = left[key]
        b = right[key]
        if not a or not b:
            reasons.append(f"{label}缺失，无法确认可比性")
        elif a != b:
            reasons.append(f"{label}不同：{a} / {b}")
    tolerances = {
        "speed": ("speedTolerance", "车速", "km/h"),
        "rpm": ("rpmTolerance", "转速", "r/min"),
        "temperature": ("temperatureTolerance", "温度", "°C"),
        "duration": ("durationTolerance", "积分时长", "s"),
    }
    for field, (parameter, label, unit) in tolerances.items():
        a = left[field]
        b = right[field]
        if a is None or b is None:
            reasons.append(f"{label}缺失，无法确认可比性")
        elif abs(a - b) > parameters[parameter] + 1e-10:
            reasons.append(f"{label}差 {abs(a - b):.6g} {unit} 超过容差 {parameters[parameter]:.6g}")
    if left["independentBands"] != right["independentBands"]:
        reasons.append("互不重叠频带声明不同")
    return reasons


def sorted_bands(*trials):
    keys = set()
    for trial in trials:
        keys.update(trial["bands"])
    return sorted(keys, key=float)


def trial_total(trial, condition):
    bands = trial["bands"]
    if not condition["independentBands"]:
        return None, "未声明互不重叠频带，不计算频带合成声级"
    if not bands:
        return None, "无频带数据"
    if any(value is None for value in bands.values()):
        return None, "存在缺失频带，不计算频带合成声级"
    return energy_sum(list(bands.values())), "已录入频带范围的能量和，并非全频总声级"


def compare_pair(pair, trial_map, condition_map, parameters):
    baseline = trial_map[pair["baselineId"]]
    target = trial_map[pair["targetId"]]
    left_condition = condition_map[baseline["conditionId"]]
    right_condition = condition_map[target["conditionId"]]
    reasons = compatible(left_condition, right_condition, parameters)
    source_warning = None
    if baseline["sourceKind"] != target["sourceKind"]:
        reasons.append("来源类别不同，禁止将模拟、人工和实测混合作为同一配对")
    if baseline["sourceKind"] != "measured":
        source_warning = "包含模拟或人工填写数据，不能证明实际应用效果"
    bands = []
    valid_count = 0
    for frequency in sorted_bands(baseline, target):
        before = baseline["bands"].get(frequency)
        after = target["bands"].get(frequency)
        missing = before is None or after is None
        difference = None
        if not missing and not reasons:
            difference = before - after
            valid_count += 1
        bands.append({
            "frequency": float(frequency),
            "baseline": before,
            "target": after,
            "difference": difference,
            "status": "blocked" if reasons else "missing" if missing else "valid",
        })
    base_total, base_note = trial_total(baseline, left_condition)
    target_total, target_note = trial_total(target, right_condition)
    total_difference = None
    total_note = base_note
    if set(baseline["bands"]) != set(target["bands"]):
        total_note = "两次试验频带集合不同，不比较合成声级"
    elif reasons:
        total_note = "工况或来源不可比，不比较合成声级"
    elif base_total is not None and target_total is not None:
        total_difference = base_total - target_total
    elif target_total is None:
        total_note = target_note
    timestamp_order = (
        datetime.fromisoformat(target["timestamp"].replace("Z", "+00:00"))
        - datetime.fromisoformat(baseline["timestamp"].replace("Z", "+00:00"))
    ).total_seconds()
    warnings = []
    if source_warning:
        warnings.append(source_warning)
    if timestamp_order < 0:
        warnings.append("目标时间早于基线，检查试验顺序")
    if any(band["status"] == "missing" for band in bands):
        warnings.append("缺失频带已保留为空；仅计算共同有效频带")
    if not valid_count and not reasons:
        warnings.append("没有共同有效频带")
    screening = None
    if pair["purpose"] in ("drift", "recovery"):
        threshold_key = "driftThreshold" if pair["purpose"] == "drift" else "recoveryThreshold"
        values = [abs(band["difference"]) for band in bands if band["difference"] is not None]
        screening = {
            "threshold": parameters[threshold_key],
            "maximumAbsoluteDifference": max(values) if values else None,
            "status": "unavailable" if not values else "alert" if max(values) > parameters[threshold_key] else "within",
            "meaning": "用户工程筛查阈值，不是标准合格判定",
        }
    return {
        "id": pair["id"],
        "baselineId": baseline["id"],
        "targetId": target["id"],
        "baselineName": baseline["name"],
        "targetName": target["name"],
        "targetType": target["type"],
        "siteIds": target["siteIds"],
        "purpose": pair["purpose"],
        "sourceKind": baseline["sourceKind"],
        "baselineSource": baseline["sourceNote"],
        "targetSource": target["sourceNote"],
        "status": "blocked" if reasons else "valid" if valid_count else "empty",
        "reasons": reasons,
        "warnings": warnings,
        "bands": bands,
        "baselineTotal": base_total,
        "targetTotal": target_total,
        "totalDifference": total_difference,
        "totalNote": total_note,
        "elapsedSeconds": timestamp_order,
        "screening": screening,
    }


def summarize_effects(results):
    groups = defaultdict(list)
    for result in results:
        if result["purpose"] == "effect" and result["status"] == "valid":
            # Different source kinds cannot be pooled. Conditions must also be
            # checked across pairs before a repeated effect summary is accepted.
            key = (tuple(sorted(result["siteIds"])), result["sourceKind"])
            groups[key].append(result)
    return groups


def effect_statistics(results, trial_map, condition_map, parameters):
    summaries = []
    for (site_ids, source), group in summarize_effects(results).items():
        first = group[0]
        anchor = condition_map[trial_map[first["baselineId"]]["conditionId"]]
        reasons = []
        for result in group[1:]:
            other = condition_map[trial_map[result["baselineId"]]["conditionId"]]
            reasons.extend(compatible(anchor, other, parameters))
        used_ids = [entry[key] for entry in group for key in ("baselineId", "targetId")]
        independent = len(set(used_ids)) == len(used_ids)
        band_keys = sorted({band["frequency"] for result in group for band in result["bands"]})
        band_results = []
        for frequency in band_keys:
            before = []
            after = []
            differences = []
            for result in group:
                for band in result["bands"]:
                    if band["frequency"] == frequency and band["difference"] is not None:
                        before.append(band["baseline"])
                        after.append(band["target"])
                        differences.append(band["difference"])
            summary = paired_summary(differences, independent)
            summary["frequency"] = frequency
            summary["groupEnergyMeanDifference"] = (
                energy_mean(before) - energy_mean(after) if before else None
            )
            if reasons:
                summary["meanDifference"] = None
                summary["sdDifference"] = None
                summary["ci95"] = None
                summary["groupEnergyMeanDifference"] = None
                summary["ciNote"] = "跨配对工况不一致，禁止汇总"
            band_results.append(summary)
        summaries.append({
            "siteIds": list(site_ids),
            "sourceKind": source,
            "pairIds": [result["id"] for result in group],
            "status": "blocked" if reasons else "valid",
            "reasons": sorted(set(reasons)),
            "independentPairs": independent,
            "bands": band_results,
        })
    return summaries


def repeatability(project, condition_map):
    groups = defaultdict(list)
    for trial in project["trials"]:
        if trial["repeatGroup"]:
            key = (trial["repeatGroup"], trial["type"], tuple(sorted(trial["siteIds"])), trial["sourceKind"])
            groups[key].append(trial)
    output = []
    for (name, kind, sites, source), trials in groups.items():
        reference = condition_map[trials[0]["conditionId"]]
        reasons = []
        for trial in trials[1:]:
            reasons.extend(compatible(reference, condition_map[trial["conditionId"]], project["parameters"]))
        rows = []
        for frequency in sorted_bands(*trials):
            values = [trial["bands"].get(frequency) for trial in trials]
            available = [value for value in values if value is not None]
            summary = describe(available)
            summary["frequency"] = float(frequency)
            summary["missing"] = len(values) - len(available)
            summary["status"] = (
                "blocked" if reasons else "insufficient" if len(available) < 2
                else "alert" if summary["sd"] > project["parameters"]["repeatabilityThreshold"] else "within"
            )
            if reasons:
                for key in ("mean", "energyMean", "sd", "range", "minimum", "maximum"):
                    summary[key] = None
            rows.append(summary)
        output.append({
            "name": name,
            "type": kind,
            "siteIds": list(sites),
            "sourceKind": source,
            "trialIds": [trial["id"] for trial in trials],
            "reasons": sorted(set(reasons)),
            "bands": rows,
            "threshold": project["parameters"]["repeatabilityThreshold"],
        })
    return output


def quality_issues(project, results):
    issues = []
    paired = {pair[key] for pair in project["pairs"] for key in ("baselineId", "targetId")}
    for trial in project["trials"]:
        if not trial["bands"] or all(value is None for value in trial["bands"].values()):
            issues.append({"level": "warning", "object": trial["id"], "message": f"{trial['name']} 没有有效频带"})
        if trial["id"] not in paired:
            issues.append({"level": "info", "object": trial["id"], "message": f"{trial['name']} 尚未显式配对"})
        if trial["sourceKind"] != "measured":
            issues.append({"level": "info", "object": trial["id"], "message": f"{trial['name']} 为模拟或人工填写"})
    for result in results:
        for reason in result["reasons"]:
            issues.append({"level": "error", "object": result["id"], "message": reason})
        for warning in result["warnings"]:
            issues.append({"level": "warning", "object": result["id"], "message": warning})
        if result["screening"] and result["screening"]["status"] == "alert":
            issues.append({"level": "warning", "object": result["id"], "message": f"{result['targetName']} 超过用户筛查阈值"})
    if not any(pair["purpose"] == "drift" for pair in project["pairs"]):
        issues.append({"level": "warning", "object": project["id"], "message": "未设置漂移配对，无法评估基线稳定性"})
    if not any(pair["purpose"] == "recovery" for pair in project["pairs"]):
        issues.append({"level": "warning", "object": project["id"], "message": "未设置恢复配对，无法检查解除封堵后的恢复情况"})
    return issues


def analyze(raw):
    project = validate_project(raw)
    trial_map = {trial["id"]: trial for trial in project["trials"]}
    condition_map = {condition["id"]: condition for condition in project["conditions"]}
    results = [
        compare_pair(pair, trial_map, condition_map, project["parameters"])
        for pair in project["pairs"]
    ]
    return {
        "projectId": project["id"],
        "revision": project["revision"],
        "limitation": LIMITATION,
        "pairs": results,
        "effects": effect_statistics(results, trial_map, condition_map, project["parameters"]),
        "repeatability": repeatability(project, condition_map),
        "issues": quality_issues(project, results),
        "coverage": project_coverage(project, results),
        "matrix": scenario_matrix(results),
        "counts": {
            "sites": len(project["sites"]),
            "conditions": len(project["conditions"]),
            "trials": len(project["trials"]),
            "pairs": len(results),
            "valid": sum(result["status"] == "valid" for result in results),
            "blocked": sum(result["status"] == "blocked" for result in results),
            "simulation": sum(trial["sourceKind"] == "simulation" for trial in project["trials"]),
        },
    }

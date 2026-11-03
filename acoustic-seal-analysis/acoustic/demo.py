"""Deterministic synthetic fixture. No real vehicle or registration facts."""

from acoustic.models import empty_project, validate_project


def demonstration():
    project = empty_project("模拟演示 · 车门密封与线束孔封堵")
    project["id"] = "demo-project"
    project["owner"] = "演示操作员（虚构）"
    project["description"] = "纯模拟数据，按固定数列构造用于验证流程，不来自车辆测量，不证明实际降噪效果。"
    project["sites"] = [
        {
            "id": "site-door",
            "name": "左前门上沿密封",
            "zone": "左前门",
            "material": "模拟胶带",
            "procedure": "演示：沿密封边缘临时封堵，记录覆盖范围。",
            "notes": "位置及工艺均为模拟。",
        },
        {
            "id": "site-harness",
            "name": "前围线束孔",
            "zone": "前围",
            "material": "模拟密封泥",
            "procedure": "演示：覆盖孔周围间隙，恢复时移除。",
            "notes": "不构成真实车辆作业指导。",
        },
    ]
    project["conditions"] = [{
        "id": "condition-demo",
        "name": "模拟静态激励工况",
        "vehicle": "SIM-001",
        "microphone": "模拟驾驶员耳旁固定测点",
        "excitation": "模拟宽带外部声源",
        "surface": "模拟半消声室",
        "gear": "P",
        "weighting": "Z",
        "timeWeighting": "Leq",
        "bandType": "octave",
        "instrument": "SIM-METER",
        "calibration": "模拟标定记录，仅验证字段一致性",
        "unit": "dB re 20 µPa",
        "speed": 0,
        "rpm": 0,
        "temperature": 23,
        "duration": 30,
        "independentBands": True,
        "notes": "所有数值模拟；频带预先声明为互不重叠。",
    }]
    records = [
        ("b1", "基线 1", "baseline", [], "baseline-series", [70, 68, 66], 0),
        ("b2", "基线 2", "baseline", [], "baseline-series", [70.2, 68.1, 66.2], 1),
        ("s1", "门密封单项 1", "single", ["site-door"], "door-series", [67, 66, 65], 2),
        ("s2", "门密封单项 2", "single", ["site-door"], "door-series", [67.1, 66.2, 65.1], 3),
        ("h1", "线束孔单项", "single", ["site-harness"], "", [68, 66.5, 65.5], 4),
        ("c1", "两位置组合测量值（模拟）", "combination", ["site-door", "site-harness"], "", [65.5, 64.5, 64], 5),
        ("b3", "结束基线", "baseline", [], "baseline-series", [70.4, 68.2, 66.3], 6),
        ("r1", "解除封堵恢复", "recovery", [], "", [70.1, 68.2, 66.1], 7),
    ]
    for trial_id, name, kind, sites, repeat_group, levels, minute in records:
        project["trials"].append({
            "id": trial_id,
            "name": name,
            "type": kind,
            "conditionId": "condition-demo",
            "siteIds": sites,
            "timestamp": f"2026-01-01T10:{minute:02d}:00+00:00",
            "repeatGroup": repeat_group,
            "sourceKind": "simulation",
            "sourceNote": "软件内置固定模拟数列；acoustic/demo.py；非实测。",
            "operator": "模拟操作员",
            "notes": "用于功能和数值验证。",
            "bands": {str(frequency): level for frequency, level in zip([125, 250, 500], levels)},
        })
    definitions = [
        ("p1", "b1", "s1", "effect"),
        ("p2", "b2", "s2", "effect"),
        ("p3", "b1", "h1", "effect"),
        ("p4", "b1", "c1", "effect"),
        ("p5", "b1", "b3", "drift"),
        ("p6", "b1", "r1", "recovery"),
    ]
    for pair_id, baseline, target, purpose in definitions:
        project["pairs"].append({
            "id": pair_id,
            "baselineId": baseline,
            "targetId": target,
            "purpose": purpose,
            "notes": "演示中显式指定，不按名称或顺序自动配对。",
        })
    return validate_project(project)

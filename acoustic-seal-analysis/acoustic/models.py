"""Domain validation boundary for projects, imports and every command."""

from __future__ import annotations

import copy
import math
import re
import uuid
from datetime import datetime, timezone

from acoustic import SCHEMA_VERSION, VERSION

TRIAL_TYPES = {
    "baseline": "基线",
    "single": "单项封堵",
    "combination": "组合封堵",
    "recovery": "恢复",
}
SOURCES = {
    "simulation": "模拟",
    "measured": "实测",
    "manual": "人工填写",
}
CONDITION_TEXT = {
    "vehicle": "车辆标识",
    "microphone": "测点及安装位置",
    "excitation": "声源或激励方式",
    "surface": "路面或试验场",
    "gear": "挡位",
    "weighting": "频率计权",
    "timeWeighting": "时间计权",
    "bandType": "频带制",
    "instrument": "仪器标识",
    "calibration": "标定链路及状态",
    "unit": "声级单位",
}
CONDITION_NUMBERS = {
    "speed": (0, 500, "车速 km/h"),
    "rpm": (0, 30000, "转速 r/min"),
    "temperature": (-80, 100, "温度 °C"),
    "duration": (0.001, 86400, "积分时长 s"),
}
PARAMETERS = {
    "speedTolerance": (0, 50, 0.5),
    "rpmTolerance": (0, 5000, 20),
    "temperatureTolerance": (0, 30, 2),
    "durationTolerance": (0, 3600, 0.01),
    "driftThreshold": (0, 30, 1),
    "recoveryThreshold": (0, 30, 1),
    "repeatabilityThreshold": (0, 30, 1),
}
MAX_TRIALS = 2000
MAX_BANDS = 200
MAX_PAIRS = 5000
ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]{1,80}$")


class ValidationError(ValueError):
    """An actionable input error with a stable field path."""

    def __init__(self, message, path="project"):
        self.path = path
        self.message = message
        super().__init__(f"{path}: {message}")

    def as_dict(self):
        return {
            "path": self.path,
            "message": self.message,
        }


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def identifier():
    return uuid.uuid4().hex


def object_value(value, path):
    if not isinstance(value, dict):
        raise ValidationError("必须为对象", path)
    return value


def array_value(value, path, maximum=MAX_TRIALS):
    if not isinstance(value, list):
        raise ValidationError("必须为数组", path)
    if len(value) > maximum:
        raise ValidationError(f"最多允许 {maximum} 项", path)
    return value


def text_value(value, path, required=True, limit=1000):
    if not isinstance(value, str):
        raise ValidationError("必须为文本", path)
    cleaned = value.strip()
    if required and not cleaned:
        raise ValidationError("不能为空", path)
    if len(cleaned) > limit:
        raise ValidationError(f"长度不得超过 {limit}", path)
    if any(ord(char) < 32 and char not in "\n\t\r" for char in cleaned):
        raise ValidationError("包含无效控制字符", path)
    return cleaned


def number_value(value, path, low, high, nullable=False):
    if nullable and value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValidationError("必须为有限数值", path)
    result = float(value)
    if not math.isfinite(result):
        raise ValidationError("必须为有限数值", path)
    if not low <= result <= high:
        raise ValidationError(f"允许范围 {low} 至 {high}", path)
    return result


def enum_value(value, options, path):
    if value not in options:
        raise ValidationError(f"允许值：{', '.join(options)}", path)
    return value


def bool_value(value, path):
    if not isinstance(value, bool):
        raise ValidationError("必须为布尔值", path)
    return value


def id_value(value, path):
    if not isinstance(value, str) or not ID_PATTERN.fullmatch(value):
        raise ValidationError("标识须为 1—80 位字母、数字、下划线或连字符", path)
    return value


def date_value(value, path):
    value = text_value(value, path, limit=80)
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValidationError("请输入 ISO 8601 日期时间", path) from error
    if parsed.tzinfo is None:
        raise ValidationError("日期时间须带时区，例如 +08:00 或 Z", path)
    return value


def fields_only(value, allowed, path):
    unexpected = set(value) - set(allowed)
    if unexpected:
        raise ValidationError(f"未知字段 {', '.join(sorted(unexpected))}", path)


def validate_site(raw, path="site"):
    raw = object_value(raw, path)
    fields_only(raw, ["id", "name", "zone", "material", "procedure", "notes"], path)
    return {
        "id": id_value(raw.get("id"), f"{path}.id"),
        "name": text_value(raw.get("name"), f"{path}.name", limit=120),
        "zone": text_value(raw.get("zone", ""), f"{path}.zone", False, 120),
        "material": text_value(raw.get("material", ""), f"{path}.material", False),
        "procedure": text_value(raw.get("procedure", ""), f"{path}.procedure", False, 4000),
        "notes": text_value(raw.get("notes", ""), f"{path}.notes", False, 4000),
    }


def validate_condition(raw, path="condition"):
    raw = object_value(raw, path)
    allowed = ["id", "name", "notes", "independentBands"]
    allowed.extend(CONDITION_TEXT)
    allowed.extend(CONDITION_NUMBERS)
    fields_only(raw, allowed, path)
    result = {
        "id": id_value(raw.get("id"), f"{path}.id"),
        "name": text_value(raw.get("name"), f"{path}.name", limit=120),
        "notes": text_value(raw.get("notes", ""), f"{path}.notes", False, 4000),
        "independentBands": bool_value(raw.get("independentBands", False), f"{path}.independentBands"),
    }
    for key in CONDITION_TEXT:
        result[key] = text_value(raw.get(key, ""), f"{path}.{key}", False, 200)
    for key, (low, high, _) in CONDITION_NUMBERS.items():
        result[key] = number_value(raw.get(key), f"{path}.{key}", low, high, True)
    if result["weighting"] not in ("", "A", "C", "Z"):
        raise ValidationError("频率计权请选择 A、C、Z", f"{path}.weighting")
    if result["unit"] not in ("", "dB re 20 µPa"):
        raise ValidationError("仅支持 dB re 20 µPa", f"{path}.unit")
    if result["bandType"] not in ("", "octave", "third-octave", "custom"):
        raise ValidationError("频带制无效", f"{path}.bandType")
    return result


def validate_trial(raw, site_ids, condition_ids, path="trial"):
    raw = object_value(raw, path)
    allowed = [
        "id", "name", "type", "conditionId", "siteIds", "timestamp",
        "repeatGroup", "sourceKind", "sourceNote", "operator", "notes", "bands",
    ]
    fields_only(raw, allowed, path)
    condition_id = id_value(raw.get("conditionId"), f"{path}.conditionId")
    if condition_id not in condition_ids:
        raise ValidationError("引用的工况不存在", f"{path}.conditionId")
    trial_type = enum_value(raw.get("type"), TRIAL_TYPES, f"{path}.type")
    sites = array_value(raw.get("siteIds"), f"{path}.siteIds", 200)
    for index, site in enumerate(sites):
        id_value(site, f"{path}.siteIds[{index}]")
    if len(set(sites)) != len(sites):
        raise ValidationError("封堵位置不可重复", f"{path}.siteIds")
    for site in sites:
        if site not in site_ids:
            raise ValidationError(f"封堵位置不存在：{site}", f"{path}.siteIds")
    if trial_type in ("baseline", "recovery") and sites:
        raise ValidationError("基线或恢复试验不得含封堵位置", f"{path}.siteIds")
    if trial_type == "single" and len(sites) != 1:
        raise ValidationError("单项封堵须选择一个位置", f"{path}.siteIds")
    if trial_type == "combination" and len(sites) < 2:
        raise ValidationError("组合封堵须至少选择两个位置", f"{path}.siteIds")
    bands = object_value(raw.get("bands"), f"{path}.bands")
    if len(bands) > MAX_BANDS:
        raise ValidationError(f"最多 {MAX_BANDS} 个频带", f"{path}.bands")
    normalized = {}
    for frequency, level in bands.items():
        try:
            hz = float(frequency)
        except (TypeError, ValueError) as error:
            raise ValidationError("频率键必须为 Hz 数值", f"{path}.bands") from error
        if not math.isfinite(hz) or not 1 <= hz <= 100000:
            raise ValidationError("频率允许 1—100000 Hz", f"{path}.bands.{frequency}")
        key = format(hz, ".12g")
        if key in normalized:
            raise ValidationError("规范化后的频率重复", f"{path}.bands.{frequency}")
        normalized[key] = number_value(level, f"{path}.bands.{frequency}", -100, 200, True)
    return {
        "id": id_value(raw.get("id"), f"{path}.id"),
        "name": text_value(raw.get("name"), f"{path}.name", limit=160),
        "type": trial_type,
        "conditionId": condition_id,
        "siteIds": sites,
        "timestamp": date_value(raw.get("timestamp"), f"{path}.timestamp"),
        "repeatGroup": text_value(raw.get("repeatGroup", ""), f"{path}.repeatGroup", False, 120),
        "sourceKind": enum_value(raw.get("sourceKind"), SOURCES, f"{path}.sourceKind"),
        "sourceNote": text_value(raw.get("sourceNote"), f"{path}.sourceNote", limit=4000),
        "operator": text_value(raw.get("operator", ""), f"{path}.operator", False, 120),
        "notes": text_value(raw.get("notes", ""), f"{path}.notes", False, 4000),
        "bands": normalized,
    }


def validate_pair(raw, trial_map, path="pair"):
    raw = object_value(raw, path)
    fields_only(raw, ["id", "baselineId", "targetId", "purpose", "notes"], path)
    base_id = id_value(raw.get("baselineId"), f"{path}.baselineId")
    target_id = id_value(raw.get("targetId"), f"{path}.targetId")
    if base_id not in trial_map or target_id not in trial_map:
        raise ValidationError("配对引用的试验不存在", path)
    if base_id == target_id:
        raise ValidationError("试验不能与自身配对", path)
    if trial_map[base_id]["type"] != "baseline":
        raise ValidationError("配对基准必须是基线试验", f"{path}.baselineId")
    purpose = enum_value(raw.get("purpose"), ["effect", "drift", "recovery"], f"{path}.purpose")
    target_type = trial_map[target_id]["type"]
    allowed = {
        "effect": ("single", "combination"),
        "drift": ("baseline",),
        "recovery": ("recovery",),
    }
    if target_type not in allowed[purpose]:
        raise ValidationError("目标试验类型与配对用途不一致", f"{path}.purpose")
    return {
        "id": id_value(raw.get("id"), f"{path}.id"),
        "baselineId": base_id,
        "targetId": target_id,
        "purpose": purpose,
        "notes": text_value(raw.get("notes", ""), f"{path}.notes", False, 4000),
    }


def unique_items(items, path):
    ids = [item["id"] for item in items]
    if len(ids) != len(set(ids)):
        raise ValidationError("对象标识重复", path)
    return {item["id"]: item for item in items}


def validate_project(raw):
    raw = object_value(raw, "project")
    allowed = [
        "schemaVersion", "softwareVersion", "id", "name", "owner", "description",
        "createdAt", "updatedAt", "revision", "status", "sites", "conditions",
        "trials", "pairs", "parameters", "audit",
    ]
    fields_only(raw, allowed, "project")
    if type(raw.get("schemaVersion")) is not int or raw.get("schemaVersion") != SCHEMA_VERSION:
        raise ValidationError("不支持的项目格式版本，请保留原文件并使用匹配版本", "schemaVersion")
    revision = raw.get("revision", 0)
    if isinstance(revision, bool) or not isinstance(revision, int) or revision < 0:
        raise ValidationError("修订号必须为非负整数", "revision")
    sites = [
        validate_site(item, f"sites[{index}]")
        for index, item in enumerate(array_value(raw.get("sites"), "sites", 200))
    ]
    site_map = unique_items(sites, "sites")
    conditions = [
        validate_condition(item, f"conditions[{index}]")
        for index, item in enumerate(array_value(raw.get("conditions"), "conditions", 500))
    ]
    condition_map = unique_items(conditions, "conditions")
    trials = [
        validate_trial(item, site_map, condition_map, f"trials[{index}]")
        for index, item in enumerate(array_value(raw.get("trials"), "trials"))
    ]
    trial_map = unique_items(trials, "trials")
    pairs = [
        validate_pair(item, trial_map, f"pairs[{index}]")
        for index, item in enumerate(array_value(raw.get("pairs"), "pairs", MAX_PAIRS))
    ]
    unique_items(pairs, "pairs")
    pair_keys = [(item["baselineId"], item["targetId"], item["purpose"]) for item in pairs]
    if len(pair_keys) != len(set(pair_keys)):
        raise ValidationError("同一用途的试验配对不可重复", "pairs")
    parameters = object_value(raw.get("parameters"), "parameters")
    fields_only(parameters, PARAMETERS, "parameters")
    normalized_parameters = {}
    for key, (low, high, default) in PARAMETERS.items():
        normalized_parameters[key] = number_value(parameters.get(key, default), f"parameters.{key}", low, high)
    audit = array_value(raw.get("audit", []), "audit", 10000)
    normalized_audit = []
    for index, entry in enumerate(audit):
        path = f"audit[{index}]"
        entry = object_value(entry, path)
        fields_only(entry, ["time", "action", "detail"], path)
        normalized_audit.append({
            "time": date_value(entry.get("time"), f"{path}.time"),
            "action": text_value(entry.get("action"), f"{path}.action", limit=120),
            "detail": text_value(entry.get("detail", ""), f"{path}.detail", False, 4000),
        })
    return {
        "schemaVersion": SCHEMA_VERSION,
        "softwareVersion": text_value(raw.get("softwareVersion"), "softwareVersion", limit=40),
        "id": id_value(raw.get("id"), "id"),
        "name": text_value(raw.get("name"), "name", limit=160),
        "owner": text_value(raw.get("owner", ""), "owner", False, 120),
        "description": text_value(raw.get("description", ""), "description", False, 8000),
        "createdAt": date_value(raw.get("createdAt"), "createdAt"),
        "updatedAt": date_value(raw.get("updatedAt"), "updatedAt"),
        "revision": revision,
        "status": enum_value(raw.get("status", "active"), ["active", "archived"], "status"),
        "sites": sites,
        "conditions": conditions,
        "trials": trials,
        "pairs": pairs,
        "parameters": normalized_parameters,
        "audit": normalized_audit,
    }


def empty_project(name="未命名声学项目"):
    timestamp = now()
    return {
        "schemaVersion": SCHEMA_VERSION,
        "softwareVersion": VERSION,
        "id": identifier(),
        "name": name,
        "owner": "",
        "description": "",
        "createdAt": timestamp,
        "updatedAt": timestamp,
        "revision": 0,
        "status": "active",
        "sites": [],
        "conditions": [],
        "trials": [],
        "pairs": [],
        "parameters": {key: bounds[2] for key, bounds in PARAMETERS.items()},
        "audit": [],
    }


def append_audit(project, action, detail):
    project["audit"].append({
        "time": now(),
        "action": action,
        "detail": str(detail)[:4000],
    })
    project["audit"] = project["audit"][-10000:]
    project["updatedAt"] = now()
    project["revision"] += 1
    project["softwareVersion"] = VERSION


def clone_project(raw):
    project = copy.deepcopy(validate_project(raw))
    project["id"] = identifier()
    project["name"] = (project["name"] + " 副本")[:160]
    project["status"] = "active"
    append_audit(project, "复制项目", "生成新项目标识，保留原始来源与配对")
    return project

"""Transactional, long-format UTF-8 CSV import with explicit missing values."""

import csv
import io
import json

from acoustic import VERSION
import math

from acoustic.models import ValidationError

COLUMNS = ("trial_id", "frequency_hz", "level_db")


def parse_csv(content, trial_map):
    if not isinstance(content, str):
        raise ValidationError("CSV 内容必须为文本", "csv")
    if len(content.encode("utf-8")) > 5 * 1024 * 1024:
        raise ValidationError("CSV 不得超过 5 MiB", "csv")
    stream = io.StringIO(content.lstrip("\ufeff"), newline="")
    reader = csv.DictReader(stream, strict=True)
    if reader.fieldnames != list(COLUMNS):
        raise ValidationError("表头必须严格为 trial_id,frequency_hz,level_db，使用逗号分隔", "csv.header")
    result = {}
    seen = set()
    missing = 0
    row_count = 0
    try:
        for row_number, row in enumerate(reader, start=2):
            row_count += 1
            if row_count > 100000:
                raise ValidationError("CSV 最多 100000 行", "csv")
            if None in row or any(value is None for value in row.values()):
                raise ValidationError("列数不匹配", f"csv.line[{row_number}]")
            trial_id = row["trial_id"].strip()
            if trial_id not in trial_map:
                raise ValidationError(f"试验标识不存在：{trial_id}，请先建试验", f"csv.line[{row_number}].trial_id")
            try:
                frequency = float(row["frequency_hz"].strip())
            except ValueError as error:
                raise ValidationError("频率必须为数值 Hz", f"csv.line[{row_number}].frequency_hz") from error
            if not math.isfinite(frequency) or not 1 <= frequency <= 100000:
                raise ValidationError("频率须在 1—100000 Hz", f"csv.line[{row_number}].frequency_hz")
            key = format(frequency, ".12g")
            row_key = (trial_id, key)
            if row_key in seen:
                raise ValidationError("同一试验频带重复，导入已整体取消", f"csv.line[{row_number}]")
            seen.add(row_key)
            value = row["level_db"].strip()
            if value in ("", "NA", "N/A", "null"):
                level = None
                missing += 1
            else:
                try:
                    level = float(value)
                except ValueError as error:
                    raise ValidationError("声级必须为数值或空白/NA/N/A/null", f"csv.line[{row_number}].level_db") from error
                if not math.isfinite(level) or not -100 <= level <= 200:
                    raise ValidationError("声级须在 -100—200 dB", f"csv.line[{row_number}].level_db")
            result.setdefault(trial_id, {})[key] = level
    except csv.Error as error:
        raise ValidationError(f"CSV 格式错误：{error}", f"csv.line[{reader.line_num}]") from error
    if not row_count:
        raise ValidationError("CSV 没有数据行", "csv")
    for trial_id, bands in result.items():
        if len(bands) > 200:
            raise ValidationError("每个试验最多 200 个频带", f"csv.{trial_id}")
    return {
        "updates": result,
        "rows": row_count,
        "trials": len(result),
        "missing": missing,
    }


def csv_preview(project, content, mode):
    if mode not in ("merge", "replace"):
        raise ValidationError("导入模式请选择 merge 或 replace", "mode")
    trial_map = {trial["id"]: trial for trial in project["trials"]}
    parsed = parse_csv(content, trial_map)
    overwritten = 0
    for trial_id, bands in parsed["updates"].items():
        original = trial_map[trial_id]["bands"]
        overwritten += sum(key in original for key in bands)
        merged = {} if mode == "replace" else original.copy()
        merged.update(bands)
        if len(merged) > 200:
            raise ValidationError("合并后超过每试验 200 个频带", f"csv.{trial_id}")
    return {
        **parsed,
        "mode": mode,
        "overwritten": overwritten,
        "meaning": "replace 清空受影响试验的旧频带后导入；merge 保留其他频带并覆盖同名频带",
    }


def measurements_csv(project):
    output = io.StringIO(newline="")
    writer = csv.writer(output)
    writer.writerow(COLUMNS)
    for trial in project["trials"]:
        for frequency, value in sorted(trial["bands"].items(), key=lambda item: float(item[0])):
            writer.writerow([trial["id"], frequency, "" if value is None else value])
    return "\ufeff" + output.getvalue()


def safe_csv_cell(value):
    if isinstance(value, str) and value.startswith(("=", "+", "-", "@", "\t", "\r")):
        return "'" + value
    return value


def results_csv(project, analysis):
    output = io.StringIO(newline="")
    writer = csv.writer(output)
    writer.writerow([
        "project", "revision", "pair_id", "purpose", "baseline", "target",
        "source_kind", "baseline_source", "target_source", "frequency_hz",
        "baseline_db", "target_db", "measured_difference_db", "status", "reason",
        "software_version", "schema_version", "project_id", "baseline_condition_id",
        "target_condition_id", "level_unit", "parameters_json",
    ])
    trial_map = {trial["id"]: trial for trial in project["trials"]}
    for pair in analysis["pairs"]:
        rows = pair["bands"] or [{
            "frequency": None,
            "baseline": None,
            "target": None,
            "difference": None,
            "status": pair["status"],
        }]
        for band in rows:
            row = [
                project["name"], project["revision"], pair["id"], pair["purpose"],
                pair["baselineName"], pair["targetName"], pair["sourceKind"],
                pair["baselineSource"], pair["targetSource"], band["frequency"],
                band["baseline"], band["target"], band["difference"], band["status"],
                "；".join(pair["reasons"] + pair["warnings"]),
                VERSION, project["schemaVersion"], project["id"],
                trial_map[pair["baselineId"]]["conditionId"],
                trial_map[pair["targetId"]]["conditionId"],
                "dB re 20 µPa", json.dumps(project["parameters"], ensure_ascii=False, sort_keys=True),
            ]
            writer.writerow([safe_csv_cell(value) for value in row])
    return "\ufeff" + output.getvalue()

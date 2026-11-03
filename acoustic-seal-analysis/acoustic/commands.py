"""Project mutations are isolated, validated transactions with reference guards."""

import copy

from acoustic.analysis import analyze
from acoustic.imports import csv_preview
from acoustic.models import (
    ValidationError,
    append_audit,
    clone_project,
    identifier,
    validate_project,
)

COLLECTIONS = ("sites", "conditions", "trials", "pairs")


def find_item(project, collection, item_id):
    for item in project[collection]:
        if item["id"] == item_id:
            return item
    raise ValidationError("对象不存在，可能已删除", f"{collection}.{item_id}")


def ensure_active(project):
    if project["status"] == "archived":
        raise ValidationError("归档项目只读，请先在项目页解除归档或复制项目", "status")


def upsert(project, collection, payload):
    if collection not in COLLECTIONS:
        raise ValidationError("对象集合无效", "collection")
    if not isinstance(payload, dict):
        raise ValidationError("对象必须为 JSON 对象", "payload")
    item = copy.deepcopy(payload)
    item_id = item.get("id") or identifier()
    item["id"] = item_id
    found = False
    changed = []
    for index, existing in enumerate(project[collection]):
        if existing["id"] == item_id:
            changed = [key for key in set(existing) | set(item) if existing.get(key) != item.get(key)]
            project[collection][index] = item
            found = True
            break
    if not found:
        project[collection].append(item)
    verb = "更新" if found else "新增"
    detail = str(item.get("name", item_id))
    if changed:
        detail += "；修改字段=" + ",".join(sorted(changed))
    append_audit(project, verb + collection, detail)
    return item_id


def remove(project, collection, item_id):
    if collection not in COLLECTIONS:
        raise ValidationError("对象集合无效", "collection")
    item = find_item(project, collection, item_id)
    if collection == "sites":
        related = [trial["name"] for trial in project["trials"] if item_id in trial["siteIds"]]
    elif collection == "conditions":
        related = [trial["name"] for trial in project["trials"] if trial["conditionId"] == item_id]
    elif collection == "trials":
        related = [pair["id"] for pair in project["pairs"] if item_id in (pair["baselineId"], pair["targetId"])]
    else:
        related = []
    if related:
        raise ValidationError("仍被引用，先修改或删除关联对象：" + "、".join(related[:10]), collection)
    project[collection] = [entry for entry in project[collection] if entry["id"] != item_id]
    append_audit(project, "删除" + collection, item.get("name", item_id))


def change_metadata(project, payload):
    if not isinstance(payload, dict):
        raise ValidationError("项目信息须为对象", "payload")
    allowed = {"name", "owner", "description"}
    if set(payload) - allowed:
        raise ValidationError("项目信息含未知字段", "payload")
    project.update(payload)
    append_audit(project, "修改项目信息", project["name"])


def change_parameters(project, payload):
    if not isinstance(payload, dict):
        raise ValidationError("分析参数须为对象", "payload")
    previous = copy.deepcopy(project["parameters"])
    project["parameters"].update(payload)
    append_audit(project, "修改分析参数", f"原值 {previous}；现值 {project['parameters']}；全部结果重新计算")


def import_measurements(project, payload):
    if not isinstance(payload, dict):
        raise ValidationError("导入请求须为对象", "payload")
    preview = csv_preview(project, payload.get("content"), payload.get("mode"))
    for trial in project["trials"]:
        update = preview["updates"].get(trial["id"])
        if update is not None:
            if preview["mode"] == "replace":
                trial["bands"] = {}
            trial["bands"].update(update)
    append_audit(
        project,
        "CSV频带导入",
        f"模式 {preview['mode']}；{preview['rows']} 行；{preview['trials']} 试验；"
        f"缺失 {preview['missing']}；同频带覆盖 {preview['overwritten']}；来源沿用试验元数据",
    )


def duplicate_trial(project, trial_id):
    original = find_item(project, "trials", trial_id)
    trial = copy.deepcopy(original)
    trial["id"] = identifier()
    trial["name"] = (trial["name"] + " 副本（未录入测量）")[:160]
    trial["bands"] = {}
    trial["repeatGroup"] = ""
    trial["notes"] = ("从试验复制结构；时间和来源须复核。\n" + trial["notes"])[:4000]
    project["trials"].append(trial)
    append_audit(project, "复制试验结构", f"{original['id']} → {trial['id']}；未复制测量和配对")
    return trial["id"]


def execute(raw, action, payload=None):
    project = copy.deepcopy(validate_project(raw))
    payload = {} if payload is None else payload
    if not isinstance(payload, dict):
        raise ValidationError("命令参数必须为对象", "payload")
    if not isinstance(action, str):
        raise ValidationError("命令名称必须为文本", "action")
    result_id = None
    if action == "clone":
        project = clone_project(project)
    elif action == "archive":
        if project["status"] != "active":
            raise ValidationError("项目已经归档", "status")
        project["status"] = "archived"
        append_audit(project, "归档项目", "项目进入只读状态")
    elif action == "restore":
        if project["status"] != "archived":
            raise ValidationError("项目未归档", "status")
        project["status"] = "active"
        append_audit(project, "解除归档", "恢复编辑")
    else:
        ensure_active(project)
        if action == "metadata":
            change_metadata(project, payload)
        elif action == "parameters":
            change_parameters(project, payload)
        elif action == "upsert":
            result_id = upsert(project, payload.get("collection"), payload.get("item"))
        elif action == "delete":
            remove(project, payload.get("collection"), payload.get("id"))
        elif action == "import":
            import_measurements(project, payload)
        elif action == "duplicateTrial":
            result_id = duplicate_trial(project, payload.get("id"))
        elif action == "recordAnalysis":
            analysis = analyze(project)
            blocked = [f"{item['id']}: {'；'.join(item['reasons'])}" for item in analysis["pairs"] if item["reasons"]]
            append_audit(project, "记录分析复核", "阻断原因：" + (" | ".join(blocked) or "无") + "; 参数=" + str(project["parameters"]))
        else:
            raise ValidationError("未知命令", "action")
    project = validate_project(project)
    return {
        "project": project,
        "analysis": analyze(project),
        "resultId": result_id,
    }

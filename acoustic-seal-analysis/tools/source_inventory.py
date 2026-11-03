"""Repeatable inventory of production sources; excludes QA and document builders."""

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = ["acoustic/*.py", "web/*.js", "web/*.css", "web/*.html"]
PURPOSES = {
    "__init__.py": "软件身份与格式版本",
    "models.py": "领域对象、输入边界与项目验证",
    "analysis.py": "声级差、重复统计和筛查",
    "review.py": "覆盖、时间线与测得差值矩阵",
    "commands.py": "项目事务、引用保护、归档和审计",
    "imports.py": "CSV预检、整体导入与明细输出",
    "report.py": "自包含报告、图表和溯源附录",
    "demo.py": "用户主动载入的固定模拟样例",
    "server.py": "本机HTTP接口、静态界面与请求边界",
    "service.js": "浏览器服务调用和文件下载边界",
    "viewmodel.js": "MVVM可观察状态及命令",
    "views.js": "工作页、表格、图表与表单视图",
    "app.js": "事件适配、表单解析与ViewModel绑定",
    "styles.css": "响应式布局、交互状态与图表样式",
    "index.html": "本地应用入口与无脚本提示",
}


def inventory():
    files = sorted({path for pattern in PATTERNS for path in ROOT.glob(pattern)})
    rows = []
    for path in files:
        data = path.read_bytes()
        lines = data.decode("utf-8").splitlines()
        rows.append({
            "path": path.relative_to(ROOT).as_posix(),
            "purpose": PURPOSES[path.name],
            "lines": len(lines),
            "nonblank": sum(bool(line.strip()) for line in lines),
            "sha256": hashlib.sha256(data).hexdigest(),
        })
    return rows


if __name__ == "__main__":
    rows = inventory()
    total = sum(row["nonblank"] for row in rows)
    summary = {
        "scope": PATTERNS,
        "baseline": 0,
        "minimum": 3500,
        "productionNonblank": total,
        "includes": "手写生产源码非空行（含必要注释及样式，不含测试、工具、文档、示例及生成物）",
        "files": rows,
    }
    (ROOT / "docs" / "source-inventory.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2))
    markdown = [
        "# 源码清单与规模复核 V1.0",
        "",
        "口径：手写生产源码非空行，范围 acoustic/*.py、web/*.js、web/*.css、web/*.html。排除测试、材料工具、文档、示例、生成物和截图。新项目基线0，最低目标3500。必要注释按非空行计入，未采用重复页面或无调用的占位代码。",
        "",
        "| 文件 | 用途 | 物理行 | 非空行 |",
        "| --- | --- | ---: | ---: |",
    ]
    for row in rows:
        markdown.append(f"| {row['path']} | {row['purpose']} | {row['lines']} | {row['nonblank']} |")
    markdown.extend([
        "",
        f"生产源码非空行合计：**{total}**；{'达到' if total >= 3500 else '未达到'}最低目标。",
        "",
        "逐文件SHA-256见source-inventory.json。可用 `python tools/source_inventory.py` 重算。run.sh/run.bat为启动辅助，未计入规模。",
        "",
        "完整源码排印稿由这些文件按顺序生成，未收入测试或材料生成工具。正式申报应由申请人按当次要求核定提交页数、前后页范围、页眉及签章；目前提供完整稿供复核，不冒充已获登记机构认可。",
    ])
    (ROOT / "docs" / "源码清单.md").write_text("\n".join(markdown))
    listing = []
    for row in rows:
        listing.extend(["", "=" * 80, row["path"], "=" * 80])
        for index, line in enumerate((ROOT / row["path"]).read_text().splitlines(), 1):
            listing.append(f"{index:04d}  {line}".rstrip())
    (ROOT / "docs" / "完整源码清单.txt").write_text("\n".join(listing))
    print(json.dumps({"files": len(rows), "productionNonblank": total, "minimum": 3500}, ensure_ascii=False))
    if total < 3500:
        raise SystemExit(1)

"""Self-contained report with provenance, conditions, blocked results and audit."""

import html

from acoustic import SOFTWARE_NAME, VERSION
from acoustic.analysis import LIMITATION, analyze
from acoustic.models import CONDITION_TEXT, SOURCES, TRIAL_TYPES, now, validate_project


def escape(value):
    return html.escape(str(value), quote=True)


def number(value):
    return "—" if value is None else f"{value:.6f}"


def table(headers, rows):
    head = "".join(f"<th>{escape(value)}</th>" for value in headers)
    body = []
    for row in rows:
        body.append("<tr>" + "".join(f"<td>{escape(value)}</td>" for value in row) + "</tr>")
    if not body:
        body.append(f'<tr><td colspan="{len(headers)}">暂无记录</td></tr>')
    return f"<table><thead><tr>{head}</tr></thead><tbody>{''.join(body)}</tbody></table>"


def level_chart(pair):
    rows = [band for band in pair["bands"] if band["difference"] is not None]
    if not rows:
        return "<p>无可比较数据，不绘图。</p>"
    values = [band[key] for band in rows for key in ("baseline", "target")]
    minimum = min(values) - 3
    span = max(max(values) - minimum + 3, 10)
    width = 760
    height = 280
    left = 64
    plot_width = width - left - 20
    elements = [
        f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="基线和目标频带声级，单位dB">',
        '<rect width="100%" height="100%" fill="#f8fafc"/>',
    ]
    for step in range(5):
        value = minimum + span * step / 4
        y = 220 - step * 45
        elements.append(f'<line x1="{left}" y1="{y}" x2="740" y2="{y}" stroke="#dbe3ec"/>')
        elements.append(f'<text x="5" y="{y + 4}" font-size="12">{value:.1f}</text>')
    for index, band in enumerate(rows):
        x = left + (index + 0.5) * plot_width / len(rows)
        for offset, key, color in [(-9, "baseline", "#64748b"), (9, "target", "#0d9488")]:
            y = 220 - (band[key] - minimum) / span * 180
            elements.append(f'<rect x="{x + offset - 6:.2f}" y="{y:.2f}" width="12" height="{220 - y:.2f}" fill="{color}"/>')
        elements.append(f'<text x="{x:.2f}" y="242" text-anchor="middle" font-size="11">{band["frequency"]:g}</text>')
    elements.extend([
        '<text x="5" y="18" font-size="12">dB re 20 µPa（截断纵轴）</text>',
        '<text x="650" y="268" font-size="12">频率 Hz</text>',
        '<text x="160" y="268" font-size="12" fill="#64748b">■ 基线</text>',
        '<text x="250" y="268" font-size="12" fill="#0d9488">■ 目标</text>',
        '</svg>',
    ])
    return "".join(elements)


def report_html(raw):
    project = validate_project(raw)
    analysis = analyze(project)
    site_names = {site["id"]: site["name"] for site in project["sites"]}
    sections = [
        '<!doctype html><html lang="zh-CN"><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width,initial-scale=1">',
        '<title>' + escape(project["name"]) + ' · 分析报告</title>',
        '<style>',
        'body{font:15px/1.65 "Noto Sans CJK SC","Microsoft YaHei",sans-serif;color:#173042;margin:32px auto;max-width:1100px;padding:0 24px}',
        'h1{font-size:28px}h2{border-bottom:2px solid #0d9488;padding-bottom:8px;margin-top:36px}',
        'h3{margin-top:25px}table{width:100%;border-collapse:collapse;margin:14px 0;font-size:12px;overflow-wrap:anywhere}',
        'td,th{padding:7px;border:1px solid #cad5df;text-align:left}th{background:#edf4f7}',
        '.notice{background:#fff4db;border-left:5px solid #b7791f;padding:14px}.blocked{color:#a12626}',
        'svg{max-width:100%;height:auto}.meta{color:#53697a}pre{white-space:pre-wrap;overflow-wrap:anywhere}',
        '@media print{body{margin:0;max-width:none;font-size:11px}h2{break-after:avoid}tr{break-inside:avoid}svg{max-height:230px}}',
        '</style><body>',
        '<h1>' + escape(project["name"]) + '</h1>',
        f'<p class="meta">{SOFTWARE_NAME} V{VERSION} · 报告生成时间 {escape(now())} · 项目修订 {project["revision"]}</p>',
        '<p class="notice">' + escape(LIMITATION) + '</p>',
        '<h2>1 项目信息与来源</h2>',
        table(["字段", "内容"], [
            ["项目标识", project["id"]],
            ["负责人（用户填写）", project["owner"]],
            ["项目说明", project["description"]],
            ["状态", project["status"]],
            ["格式版本", project["schemaVersion"]],
            ["来源分类", "、".join(sorted({SOURCES[trial['sourceKind']] for trial in project['trials']})) or "无试验"],
            ["有效/阻断配对", f"{analysis['counts']['valid']} / {analysis['counts']['blocked']}"],
        ]),
        '<h2>2 封堵位置</h2>',
        table(["标识", "名称", "区域", "材料", "操作记录", "备注"], [
            [site[key] for key in ("id", "name", "zone", "material", "procedure", "notes")]
            for site in project["sites"]
        ]),
        '<h2>3 工况与测量口径</h2>',
    ]
    for condition in project["conditions"]:
        rows = [[label, condition[key] or "未填写"] for key, label in CONDITION_TEXT.items()]
        rows.extend([
            ["车速 km/h", condition["speed"]],
            ["转速 r/min", condition["rpm"]],
            ["温度 °C", condition["temperature"]],
            ["积分时长 s", condition["duration"]],
            ["互不重叠频带声明", condition["independentBands"]],
            ["备注", condition["notes"]],
        ])
        sections.append('<h3>' + escape(condition["name"]) + '</h3>')
        sections.append(table(["字段", "值"], rows))
    sections.extend([
        '<h2>4 试验原始频带记录</h2>',
        '<p>空值表示未提供，不按零声级补齐。频带声级已采用工况中所写频率计权，不再重复加权。</p>',
    ])
    for trial in project["trials"]:
        sections.append('<h3>' + escape(trial["name"]) + ' · ' + TRIAL_TYPES[trial["type"]] + '</h3>')
        sections.append(table(["项目", "内容"], [
            ["标识", trial["id"]],
            ["来源", SOURCES[trial["sourceKind"]] + "；" + trial["sourceNote"]],
            ["工况标识", trial["conditionId"]],
            ["时间", trial["timestamp"]],
            ["封堵位置", "、".join(site_names[key] for key in trial["siteIds"]) or "无"],
            ["重复组", trial["repeatGroup"] or "未分组"],
            ["操作员", trial["operator"]],
            ["备注", trial["notes"]],
        ]))
        sections.append(table(["中心频率 Hz", "声级 dB re 20 µPa"], [
            [frequency, number(value)] for frequency, value in sorted(trial["bands"].items(), key=lambda item: float(item[0]))
        ]))
    sections.append('<h2>5 显式配对与测得声级差</h2>')
    for pair in analysis["pairs"]:
        sections.append('<h3>' + escape(pair["baselineName"] + " → " + pair["targetName"]) + '</h3>')
        sections.append('<p>配对标识 ' + escape(pair["id"]) + '；用途 ' + escape(pair["purpose"]) + '；状态 ' + escape(pair["status"]) + '</p>')
        sections.append('<p>来源：' + escape(SOURCES[pair["sourceKind"]]) + '</p>')
        if pair["reasons"]:
            sections.append('<p class="blocked">阻断：' + escape("；".join(pair["reasons"])) + '</p>')
        if pair["warnings"]:
            sections.append('<p class="notice">' + escape("；".join(pair["warnings"])) + '</p>')
        sections.append(level_chart(pair))
        sections.append(table(["Hz", "基线 dB", "目标 dB", "基线减目标 dB", "状态"], [
            [band["frequency"], number(band["baseline"]), number(band["target"]), number(band["difference"]), band["status"]]
            for band in pair["bands"]
        ]))
        sections.append('<p>录入频带合成声级差：' + number(pair["totalDifference"]) + ' dB；' + escape(pair["totalNote"]) + '</p>')
        if pair["screening"]:
            screen = pair["screening"]
            sections.append('<p>最大频带绝对差 ' + number(screen["maximumAbsoluteDifference"]) + ' dB；阈值 ' + number(screen["threshold"]) + ' dB；' + escape(screen["status"]) + '。' + escape(screen["meaning"]) + '</p>')
    sections.extend([
        '<h2>6 重复配对统计</h2>',
        '<p>配对差算术均值与两组等权能量平均声级之差是两个不同统计量。双侧 95% 区间要求独立配对及近似正态；n=1或复用试验不生成区间。</p>',
    ])
    for effect in analysis["effects"]:
        sections.append('<h3>' + escape(" + ".join(site_names[key] for key in effect["siteIds"])) + ' · ' + escape(SOURCES[effect["sourceKind"]]) + '</h3>')
        sections.append('<p>' + escape("；".join(effect["reasons"])) + '</p>')
        sections.append(table(["Hz", "n", "配对差均值 dB", "配对差SD dB", "组能量均值差 dB", "95%区间", "说明"], [
            [row["frequency"], row["n"], number(row["meanDifference"]), number(row["sdDifference"]), number(row["groupEnergyMeanDifference"]),
             "—" if row["ci95"] is None else " / ".join(number(value) for value in row["ci95"]), row["ciNote"]]
            for row in effect["bands"]
        ]))
    sections.append('<h2>7 重复性检查</h2>')
    for group in analysis["repeatability"]:
        sections.append('<h3>' + escape(group["name"]) + ' · ' + TRIAL_TYPES[group["type"]] + '</h3>')
        sections.append('<p>来源 ' + escape(SOURCES[group["sourceKind"]]) + '；用户样本标准差阈值 ' + number(group["threshold"]) + ' dB。' + escape("；".join(group["reasons"])) + '</p>')
        sections.append(table(["Hz", "n", "缺失", "算术均值 dB", "能量均值 dB", "样本SD dB", "极差 dB", "状态"], [
            [row["frequency"], row["n"], row["missing"], number(row["mean"]), number(row["energyMean"]), number(row["sd"]), number(row["range"]), row["status"]]
            for row in group["bands"]
        ]))
    sections.extend([
        '<h2>8 参数、质量问题与审计</h2>',
        table(["参数", "值"], [[key, value] for key, value in project["parameters"].items()]),
        table(["级别", "对象", "问题"], [[issue["level"], issue["object"], issue["message"]] for issue in analysis["issues"]]),
        table(["时间", "操作", "详情"], [[entry["time"], entry["action"], entry["detail"]] for entry in project["audit"]]),
        '<h2>9 方法及使用边界</h2>',
        '<p>频带差 ΔL = L基线 − L目标；正值表示该配对下目标声级较低，负值表示较高。等权能量均值为 10 log10(Σ10^(L/10)/n)。互不重叠频带合成去除分母 n。所有合成只覆盖录入频带。</p>',
        '<p>未提供测量不确定度预算，不作统计显著性或因果保证。工况容差和筛查阈值由用户设定，不代表国家标准。实测来源说明由操作者提供，软件不自动认证数据真实性。</p>',
        '<p>' + escape(LIMITATION) + '</p>',
        review_appendix(project, analysis),
        '</body></html>',
    ])
    return "\n".join(sections)


def review_appendix(project, analysis):
    """Export the same coverage and actual-pair matrix exposed by the UI."""
    coverage = analysis["coverage"]
    matrix = analysis["matrix"]
    parts = [
        '<h2>10 数据覆盖与试验时间线</h2>',
        '<p>' + escape(coverage["note"]) + '</p>',
    ]
    type_rows = []
    for trial_type, count in coverage["typeCounts"].items():
        type_rows.append([
            TRIAL_TYPES[trial_type],
            count,
        ])
    parts.append(table(["试验类型", "记录数"], type_rows))
    condition_rows = []
    for condition in coverage["conditions"]:
        condition_rows.append([
            condition["name"],
            condition["trialCount"],
            "字段齐全" if condition["complete"] else "有缺失",
            "、".join(condition["missing"]),
        ])
    parts.append('<h3>工况字段完整性</h3>')
    parts.append(table([
        "工况",
        "关联试验数",
        "字段状态",
        "缺失字段",
    ], condition_rows))
    site_rows = []
    for site in coverage["sites"]:
        site_rows.append([
            site["name"],
            site["singleTrials"],
            site["combinationTrials"],
            site["validEffectPairs"],
        ])
    parts.append('<h3>封堵位置覆盖</h3>')
    parts.append(table([
        "位置",
        "单项试验数",
        "组合试验数",
        "可比较配对数",
    ], site_rows))
    frequency_rows = []
    for frequency in coverage["frequencies"]:
        frequency_rows.append([
            frequency["frequency"],
            frequency["valid"],
            frequency["missing"],
            frequency["absent"],
        ])
    parts.append('<h3>频带完整性</h3>')
    parts.append(table([
        "Hz",
        "有效记录数",
        "明确缺失数",
        "未声明频带数",
    ], frequency_rows))
    timeline_rows = []
    for trial in coverage["timeline"]:
        timeline_rows.append([
            trial["timestamp"],
            trial["name"],
            TRIAL_TYPES[trial["type"]],
            SOURCES[trial["sourceKind"]],
            trial["validBands"],
            trial["validTargetPairs"],
        ])
    parts.append('<h3>按带时区时间排序的试验记录</h3>')
    parts.append(table([
        "时间",
        "试验",
        "类型",
        "来源",
        "有效频带数",
        "作为目标的可比较配对数",
    ], timeline_rows))
    parts.append('<h2>11 单项与组合测得差值矩阵</h2>')
    parts.append('<p>' + escape(matrix["note"]) + '</p>')
    headings = ["频率 Hz"]
    for column in matrix["columns"]:
        headings.append(
            column["targetName"]
            + "；基线=" + column["baselineName"]
            + "；来源=" + SOURCES[column["sourceKind"]]
        )
    matrix_rows = []
    for record in matrix["rows"]:
        values = [record["frequency"]]
        for cell in record["cells"]:
            values.append(
                number(cell["difference"])
                if cell["difference"] is not None
                else cell["status"]
            )
        matrix_rows.append(values)
    parts.append(table(headings, matrix_rows))
    parts.append(
        '<p>矩阵数值单位 dB，为各列自己的基线减目标。'
        '本表没有相加预测，也不提供路径独立能量贡献排序。</p>'
    )
    return "\n".join(parts)

"""Build inspectable registration drafts; never infer Microsoft Word page count."""
from pathlib import Path
import hashlib
import json
import re
from docx import Document
from docx.shared import Cm, Pt, RGBColor
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
IDENTITY = '车内低频声学模态与座位声压分布设计软件 V1.0'


def production_files():
    return sorted(p for p in (ROOT / 'src').rglob('*')
                  if p.suffix in {'.cs', '.xaml'} and not {'bin', 'obj'}.intersection(p.parts))


def font(run, name, size):
    run.font.name = name
    run.font.size = Pt(size)
    run._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'), name)


def base(title):
    doc = Document()
    section = doc.sections[0]
    # Dimensions and basic typography retained from the backed-up cabin manual.
    section.page_width = Cm(21)
    section.page_height = Cm(29.7)
    section.left_margin = Cm(2)
    section.right_margin = Cm(2)
    section.top_margin = Cm(1.8)
    section.bottom_margin = Cm(1.8)
    normal = doc.styles['Normal']
    normal.font.name = 'Microsoft YaHei'
    normal.font.size = Pt(10.5)
    normal._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'), 'Microsoft YaHei')
    normal.paragraph_format.space_after = Pt(7)
    normal.paragraph_format.line_spacing = 1.25
    for name, size in [('Title', 26), ('Heading 1', 19), ('Heading 2', 14), ('Heading 3', 11)]:
        style = doc.styles[name]
        style.font.name = 'Microsoft YaHei'
        style.font.size = Pt(size)
        style.font.color.rgb = RGBColor.from_string('1A5364')
        style._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'), 'Microsoft YaHei')
    header = section.header.paragraphs[0]
    font(header.add_run(IDENTITY + ' | ' + title), 'Microsoft YaHei', 8)
    footer = section.footer.paragraphs[0]
    footer.alignment = 2
    footer.add_run('草稿 · 以Microsoft Word实际分页验收 | ')
    field = OxmlElement('w:fldSimple')
    field.set(qn('w:instr'), 'PAGE')
    footer._p.append(field)
    return doc


def table(doc, rows):
    grid = doc.add_table(rows=0, cols=len(rows[0]))
    grid.style = 'Table Grid'
    for index, row in enumerate(rows):
        cells = grid.add_row().cells
        for cell, value in zip(cells, row):
            p = cell.paragraphs[0]
            p.paragraph_format.space_after = Pt(4)
            run = p.add_run(value)
            font(run, 'Microsoft YaHei', 9)
            run.bold = index == 0
        if index == 0:
            repeat = OxmlElement('w:tblHeader')
            grid.rows[0]._tr.get_or_add_trPr().append(repeat)
    return grid


CAPTURES = {
    '第二部分': [('01-empty.png', '普通启动为空白项目状态')],
    '第三部分': [('02-project.png', '独立模拟项目档案'), ('17-example-dialog.png', '原生独立示例对话框')],
    '第五部分': [('03-points.png', '声源与耳点表格'), ('16-invalid-csv-dialog.png', '原生CSV预检错误定位')],
    '第七部分': [('04a-field-settings.png', '原生剖面设置展开状态'), ('04-response-field.png', '实际WPF响应剖面'), ('05-mode-shape.png', '有符号模态振型')],
    '第八部分': [('06-seat-spectrum.png', '座位相对频响'), ('07-modes.png', '模态谱系'), ('08-coupling.png', '模态贡献与耦合')],
    '第九部分': [('09-comparison.png', '基准比较'), ('10-scan.png', '参数扫描')],
    '第十部分': [('11-diagnostics.png', '数值敏感性'), ('12-reviews.png', '工程复核与保存历史')],
    '第十三部分': [('14-invalid-input.png', '无效输入提示')],
}


def markdown_doc(source, target):
    doc = base(source.stem)
    lines = source.read_text(encoding='utf-8').splitlines()
    pending = []
    chapter = ''
    figures = 0
    def flush_table():
        if pending:
            table(doc, pending)
            pending.clear()
    def pictures():
        nonlocal figures
        for key, images in CAPTURES.items():
            if key not in chapter or source.stem != '操作说明书':
                continue
            for filename, caption in images:
                image_path = DOCS / 'screenshots' / filename
                if not image_path.exists():
                    continue
                figures += 1
                doc.add_paragraph(f'图{figures}展示本章操作后的真实WPF窗口，请结合上述步骤核对参数及状态。')
                doc.add_picture(str(image_path), width=Cm(16.5))
                p = doc.add_paragraph(f'图{figures}  {caption}（模拟数据，Windows原生渲染截图）', style='Caption')
                p.paragraph_format.keep_with_next = False
    for line in lines:
        if line.startswith('|'):
            if not re.match(r'^\|[\s:|\-]+\|$', line):
                pending.append([cell.strip() for cell in line.strip('|').split('|')])
            continue
        flush_table()
        if line.startswith('## '):
            pictures()
            chapter = line[3:]
            if source.stem == '软著信息采集表' and chapter == '软件主要功能':
                doc.add_page_break()
            doc.add_heading(chapter, level=1)
        elif line.startswith('### '):
            doc.add_heading(line[4:], level=2)
        elif line.startswith('# '):
            title = line[2:]
            if source.stem == '操作说明书':
                title = '车内低频声学模态与\n座位声压分布设计软件 V1.0\n操作说明书'
            doc.add_heading(title, level=0)
        elif line.strip():
            clean = line.replace('`', '').replace('**', '')
            doc.add_paragraph(clean)
    flush_table()
    pictures()
    doc.save(target)
    return figures


files = production_files()
entries = []
full = []
purposes = {
    'Analysis': '解析模态、复数频响、比较扫描与数值诊断',
    'Domain': '项目领域模型、示例与约束边界',
    'IO': '版本化项目读写、点位交换与研究成果输出',
    'Controls': 'WPF声场和频响原生绘制与读数',
    'Infrastructure': '属性通知、命令与原生界面转换',
    'Services': '原生文件对话框、导入确认及画布输出',
    'ViewModels': '可校验输入、研究状态与MVVM业务命令',
    'Views': '原生主窗口和独立对话框',
}
for path in files:
    text = path.read_text(encoding='utf-8')
    rel = str(path.relative_to(ROOT))
    entry = dict(path=rel, nonblank=sum(bool(line.strip()) for line in text.splitlines()),
                 sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                 purpose=next((value for key, value in purposes.items() if key in path.parts), 'WPF应用启动和共享资源'))
    entries.append(entry)
    full.append('===== ' + rel + ' =====\n' + text)
report = {
    'scope': 'Actual handwritten src/**/*.cs and src/**/*.xaml participating in the build; nonblank lines; excludes tests, scripts, docs, bin, obj, project config, dependencies and generated files.',
    'baselineOldWebNonblank': 3608,
    'minimumNativeReplacementNonblank': 3500,
    'note': 'Native platform replacement, not a scope-expansion project. No claim of 2x expansion.',
    'productionNonblank': sum(entry['nonblank'] for entry in entries),
    'files': entries,
}
(DOCS / 'source-inventory.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
(DOCS / '完整生产源码.txt').write_text('\n\n'.join(full), encoding='utf-8')
listing = ['# 生产源码清单', '', report['scope'], '', f"实际生产非空行：{report['productionNonblank']}；最低目标3500。旧Web基线3608，仅作追溯，不把原生替换宣称为双倍功能扩充。", '', '| 文件 | 非空行 | 用途 |', '| --- | --- | --- |']
listing += [f"| {x['path']} | {x['nonblank']} | {x['purpose']} |" for x in entries]
(DOCS / '源码清单.md').write_text('\n'.join(listing) + '\n', encoding='utf-8')
assert report['productionNonblank'] >= 3500
info_text = (DOCS / '软著信息采集表.md').read_text(encoding='utf-8')
major = info_text.split('## 软件主要功能')[1].split('## 技术特点')[0]
hanzi = len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0003134f]', major))
assert hanzi > 500
figures = markdown_doc(DOCS / '操作说明书.md', DOCS / '操作说明书-草稿.docx')
markdown_doc(DOCS / '软著信息采集表.md', DOCS / '软著信息采集表-工作稿.docx')
source_doc = base('完整生产源码排印稿')
source_doc.add_heading(IDENTITY + ' 完整生产源码', 0)
source_doc.add_paragraph('仅列实际构建生产源码。正式申报范围与页数由申请人按当次规则核定。')
for path in files:
    source_doc.add_heading(str(path.relative_to(ROOT)), 1)
    for line in path.read_text(encoding='utf-8').splitlines():
        paragraph = source_doc.add_paragraph()
        paragraph.paragraph_format.space_after = Pt(0)
        paragraph.paragraph_format.line_spacing = 1
        font(paragraph.add_run(line or ' '), 'Consolas', 8)
source_doc.save(DOCS / '完整生产源码排印稿.docx')
material = {
    'majorFunctionHanzi': hanzi,
    'productionNonblank': report['productionNonblank'],
    'sourceFiles': len(files),
    'nativeWpfFiguresEmbedded': figures,
    'nativeScreenshotSourceCommit': json.loads((DOCS / 'screenshots/windows-ui-evidence.json').read_text(encoding='utf-8-sig')).get('sourceCommit') if (DOCS / 'screenshots/windows-ui-evidence.json').exists() else None,
    'manualWordPageCount': None,
    'manualPageCountStatus': 'Pending Microsoft Word actual pagination and per-page review; no page count inferred.',
    'providedApplicantTemplate': False,
    'layoutReference': 'Backed-up cabin manual A4, 2cm side margins, 1.8cm top/bottom, Microsoft YaHei; applicant-specific template not supplied.',
    'status': 'Draft; all native PNGs visually reviewed with hashes in screenshot-review.json; Microsoft Word actual pagination and applicant-specific fields remain pending.',
}
(DOCS / 'materials-evidence.json').write_text(json.dumps(material, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(material, ensure_ascii=False, indent=2))

#!/usr/bin/env python3
"""Generate editable draft materials; never infer Word pagination or fabricate UI captures."""
import hashlib
import json
import re
from pathlib import Path
from docx import Document
from docx.shared import Cm, Pt
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from source_inventory import ROOT, FILES, inventory

OUT = ROOT / 'docs/materials'
NAME = '车外行人警示声音传播与覆盖设计软件 V2.0'

def document(title):
    doc = Document()
    section = doc.sections[0]
    section.page_width, section.page_height = Cm(21), Cm(29.7)
    section.top_margin = section.bottom_margin = Cm(2)
    section.left_margin = section.right_margin = Cm(2.2)
    for name in ('Normal', 'Body Text'):
        style = doc.styles[name]
        style.font.name, style.font.size = 'Microsoft YaHei', Pt(11)
        style.element.rPr.rFonts.set(qn('w:eastAsia'), '宋体')
        style.paragraph_format.line_spacing = 1.35
        style.paragraph_format.space_after = Pt(8)
    section.header.paragraphs[0].text = NAME + '｜' + title
    footer = section.footer.paragraphs[0]
    footer.add_run('第 ')
    field = OxmlElement('w:fldSimple'); field.set(qn('w:instr'), 'PAGE'); footer._p.append(field)
    footer.add_run(' 页｜实际Word分页待核验')
    doc.core_properties.title = NAME + ' ' + title
    doc.core_properties.author = '待申请人核实/填写'
    doc.core_properties.subject = '与原生桌面版本对应的内部交付草稿'
    return doc

FIGURES = {
    2: [('12-published-native-window.png', '直接启动自包含程序的真实Windows窗口')],
    3: [('01-空白项目-editor.png', '项目档案编辑器，真实WPF窗口局部')],
    4: [('03-扬声器频带-editor.png', '扬声器位置与方向输入')],
    5: [('03b-七带与来源-editor.png', '七频带、修正和数据来源')],
    6: [('05-背景网格-editor.png', '均匀背景频谱与来源')],
    7: [('05b-工程目标-editor.png', '网格、指标口径与自定义工程目标')],
    8: [('06-观察点频谱-editor.png', '观察点和环形点集输入')],
    9: [('02-模拟声场.png', '模拟输入的原生声场与观察点结果'), ('06b-点频谱贡献-editor.png', '当前观察点七带结果和声源贡献')],
    11: [('07b-方案差值-editor.png', '同条件快照比较结果，目标减基线')],
    12: [('08-扫描剖面-editor.png', '单声源参数扫描结果'), ('08b-方向剖面-editor.png', '方向剖面的离散点值')],
    13: [('09b-相对试听结果-editor.png', '观察点相对试听参数与制作结果')],
    14: [('10-成果复核-editor.png', '成果导出及输入复核')],
    15: [('04-数值校验-editor.png', '真实数值格式错误及应用阻断'), ('11-最小窗口.png', '最小尺寸的实际WPF窗口')]
}

def verified_screenshots():
    folder = ROOT / 'docs/windows-ui'
    evidence = folder / 'desktop-evidence.json'
    if not evidence.exists(): return {}
    context_path = folder / 'run-context.json'
    if not context_path.exists(): return {}
    context = json.loads(context_path.read_text(encoding='utf-8-sig'))
    if context.get('verify') != 'success' or context.get('publish') != 'success': return {}
    data = json.loads(evidence.read_text(encoding='utf-8-sig'))
    if data.get('status') != 'passed': return {}
    images = {x['file']: x['sha256'] for x in data['images']}
    published = folder / 'published-executable.json'
    if published.exists():
        record = json.loads(published.read_text(encoding='utf-8-sig'))
        if record.get('status') == 'passed': images[record['screenshot']] = record['screenshot_sha256']
    for name, expected in images.items():
        assert hashlib.sha256((folder / name).read_bytes()).hexdigest() == expected, name
    return images

def manual():
    doc = document('操作说明书草稿')
    text = (OUT / '操作说明书-草稿.md').read_text(encoding='utf-8')
    sections = 0
    images = verified_screenshots()
    used = []
    def add_figures(section):
        for name, caption in FIGURES.get(section, []):
            if name not in images: continue
            path = ROOT / 'docs/windows-ui' / name
            width = Cm(11.0 if '-editor' in name else 16.5)
            number = len(used) + 1
            reference = doc.add_paragraph(f'请对照图{number}核对“{caption}”中的字段、对象和结果状态。')
            reference.paragraph_format.keep_with_next = True
            doc.add_picture(str(path), width=width)
            doc.paragraphs[-1].alignment = WD_ALIGN_PARAGRAPH.CENTER
            doc.paragraphs[-1].paragraph_format.keep_with_next = True
            number = len(used) + 1
            paragraph = doc.add_paragraph(f'图{number}  {caption}（模拟/人工输入；非实车测试）')
            paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
            paragraph.paragraph_format.space_after = Pt(8)
            for run in paragraph.runs: run.font.size = Pt(9)
            used.append({'figure': number, 'file': name, 'sha256': images[name], 'caption': caption})
    for line in text.splitlines():
        if not line.strip(): continue
        if line.startswith('# '): doc.add_heading(line[2:], 0)
        elif line.startswith('## '):
            if sections:
                add_figures(sections)
            heading = doc.add_heading(line[3:], 1)
            heading.paragraph_format.page_break_before = sections > 0
            sections += 1
        elif line.startswith('### '): doc.add_heading(line[4:], 2)
        else: doc.add_paragraph(line)
    add_figures(sections)
    doc.save(OUT / '操作说明书-草稿.docx')
    return sections, used

def information(functions):
    doc = document('软著信息采集表')
    doc.add_heading(NAME + ' 信息采集表', 0)
    doc.add_paragraph('无专用申报模板，采用通用两列表。申请人及权属等事实须由申请人核实；本表不证明登记要求已全部满足。')
    table = doc.add_table(rows=0, cols=2); table.style = 'Table Grid'
    items = [
        ('字段', '内容'), ('软件全称', '车外行人警示声音传播与覆盖设计软件'), ('软件简称', '待申请人核实/填写'),
        ('版本号', 'V2.0'), ('申请人/著作权人', '待申请人核实/填写'), ('权属和开发方式', '待申请人核实/填写'),
        ('开发完成日期', '待申请人核实/填写'), ('首次发表及发表日期', '待申请人核实/填写'),
        ('运行平台', '支持.NET10 WPF的Windows10/11 x64；已在Windows Server 2025运行，目标机兼容性待核对'), ('开发语言', 'C#；WPF XAML描述原生界面'),
        ('开发工具与环境', '.NET SDK10.0.403，Linux交叉编译；Windows Visual Studio运行待核验'),
        ('软件主要功能', functions), ('技术特点', 'WPF原生桌面、MVVM命令层、离线项目文件、自由场近似与非相干能量叠加、输入哈希及快照可追溯。'),
        ('项目与成果格式', '.avasproj、.bak、UTF-8 CSV、静态HTML、SVG、JSON、PCM16 WAV'),
        ('源码规模', '以docs/source-inventory.json为准，只统计参与构建的手写生产.cs与.xaml，不计测试、工具、依赖或生成物。'),
        ('已完成验证', 'Linux与Windows构建、66项内核和18项命令层测试；真实WPF工作区、输入校验与直接启动自包含EXE通过。详见docs/windows-ui证据。'),
        ('材料未完成项', '原生选择器与播放、目标机缩放人工验证、Microsoft Word实际不少于10页及逐页版面检查。'),
        ('适用边界', '自由场工程估算，不做法规符合性、实车声压或真实可听性判定。')
    ]
    for key, value in items:
        cells = table.add_row().cells; cells[0].text = key; cells[1].text = value
    table.autofit = False
    table.columns[0].width, table.columns[1].width = Cm(3.4), Cm(13.2)
    for row in table.rows:
        row.cells[0].width, row.cells[1].width = Cm(3.4), Cm(13.2)
    repeat = OxmlElement('w:tblHeader'); table.rows[0]._tr.get_or_add_trPr().append(repeat)
    doc.save(OUT / '软著信息采集表.docx')

def source():
    doc = document('全部生产源码排印稿')
    doc.add_heading(NAME + ' 全部生产源码', 0)
    doc.add_paragraph('仅含实际参与构建的生产C#与XAML。文件顺序与清单一致，无测试或生成代码；正式申报范围、源码页数和格式以当次申报要求及实际Word核验为准。')
    for path in FILES:
        doc.add_heading(path.relative_to(ROOT).as_posix(), 1)
        p = doc.add_paragraph()
        p.paragraph_format.line_spacing = 1
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.keep_together = False
        run = p.add_run(path.read_text(encoding='utf-8'))
        run.font.name, run.font.size = 'Consolas', Pt(8)
        run._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'), '宋体')
    doc.save(OUT / '全部生产源码排印稿.docx')

if __name__ == '__main__':
    inv = inventory()
    functions = (OUT / '软件主要功能.txt').read_text(encoding='utf-8').strip()
    han_count = len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002fa1f]', functions))
    assert han_count > 500
    sections, figures = manual(); information(functions); source()
    result = {
        'status': 'draft-word-pagination-pending' if figures else 'draft-windows-screenshots-and-word-pagination-pending',
        'manual_sections': sections,
        'manual_actual_word_pages': None,
        'source_actual_word_pages': None,
        'word_pagination_verified': False,
        'screenshots_in_manual': len(figures),
        'figures': figures,
        'reason': 'Actual WPF screenshots are admitted only with passing Windows evidence and matching hashes; Microsoft Word pagination remains unverified.',
        'major_function_unicode_han': han_count,
        'minimum_han_exclusive': 500,
        'production_nonempty_lines': inv['total'],
        'files': [{'file': p.name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'bytes': p.stat().st_size} for p in sorted(OUT.glob('*.docx'))]
    }
    (ROOT / 'docs/material-validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))

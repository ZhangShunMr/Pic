#!/usr/bin/env python3
"""Generate editable draft materials; never infer Word pagination or fabricate UI captures."""
import hashlib
import json
import re
from pathlib import Path
from docx import Document
from docx.shared import Cm, Pt
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

def manual():
    doc = document('操作说明书草稿')
    text = (OUT / '操作说明书-草稿.md').read_text(encoding='utf-8')
    sections = 0
    for line in text.splitlines():
        if not line.strip(): continue
        if line.startswith('# '): doc.add_heading(line[2:], 0)
        elif line.startswith('## '):
            if sections: doc.add_page_break()
            doc.add_heading(line[3:], 1); sections += 1
        elif line.startswith('### '): doc.add_heading(line[4:], 2)
        else: doc.add_paragraph(line)
    doc.save(OUT / '操作说明书-草稿.docx')
    return sections

def information(functions):
    doc = document('软著信息采集表')
    doc.add_heading(NAME + ' 信息采集表', 0)
    doc.add_paragraph('无专用申报模板，采用通用两列表。申请人及权属等事实须由申请人核实；本表不证明登记要求已全部满足。')
    table = doc.add_table(rows=0, cols=2); table.style = 'Table Grid'
    items = [
        ('字段', '内容'), ('软件全称', '车外行人警示声音传播与覆盖设计软件'), ('软件简称', '待申请人核实/填写'),
        ('版本号', 'V2.0'), ('申请人/著作权人', '待申请人核实/填写'), ('权属和开发方式', '待申请人核实/填写'),
        ('开发完成日期', '待申请人核实/填写'), ('首次发表及发表日期', '待申请人核实/填写'),
        ('运行平台', '支持.NET10 WPF的Windows10/11 x64；本次未在Windows运行验证'), ('开发语言', 'C#；WPF XAML描述原生界面'),
        ('开发工具与环境', '.NET SDK10.0.403，Linux交叉编译；Windows Visual Studio运行待核验'),
        ('软件主要功能', functions), ('技术特点', 'WPF原生桌面、MVVM命令层、离线项目文件、自由场近似与非相干能量叠加、输入哈希及快照可追溯。'),
        ('项目与成果格式', '.avasproj、.bak、UTF-8 CSV、静态HTML、SVG、JSON、PCM16 WAV'),
        ('源码规模', '以docs/source-inventory.json为准，只统计参与构建的手写生产.cs与.xaml，不计测试、工具、依赖或生成物。'),
        ('已完成验证', 'Linux交叉编译；66项内核测试和18项命令层测试。详见实际日志，不能代替Windows界面验收。'),
        ('材料未完成项', 'Windows真实界面截图、原生选择器与播放、缩放人工验证、Microsoft Word实际不少于10页和逐页版面检查。'),
        ('适用边界', '自由场工程估算，不做法规符合性、实车声压或真实可听性判定。')
    ]
    for key, value in items:
        cells = table.add_row().cells; cells[0].text = key; cells[1].text = value
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
    sections = manual(); information(functions); source()
    result = {
        'status': 'draft-windows-screenshots-and-word-pagination-pending',
        'manual_sections': sections,
        'manual_actual_word_pages': None,
        'source_actual_word_pages': None,
        'word_pagination_verified': False,
        'screenshots_in_manual': 0,
        'reason': 'Linux cannot execute WPF; no actual Windows images provided. No browser or mock screenshots substituted.',
        'major_function_unicode_han': han_count,
        'minimum_han_exclusive': 500,
        'production_nonempty_lines': inv['total'],
        'files': [{'file': p.name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(), 'bytes': p.stat().st_size} for p in sorted(OUT.glob('*.docx'))]
    }
    (ROOT / 'docs/material-validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))

from pathlib import Path
import json
import re
import subprocess
import sys
from docx import Document
from docx.shared import Cm, Pt
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

root = Path(__file__).resolve().parents[1]
subprocess.run([sys.executable, str(root / 'tools/source_inventory.py')], check=True)
name = '汽车附件电机电磁激励频率设计软件 V2.0'

def setup(title, source=False):
    doc = Document()
    section = doc.sections[0]
    section.page_width = Cm(21)
    section.page_height = Cm(29.7)
    section.top_margin = Cm(2.1)
    section.bottom_margin = Cm(2)
    section.left_margin = Cm(2.1)
    section.right_margin = Cm(2.1)
    for key, size in [('Normal', 10.5), ('Heading 1', 18), ('Heading 2', 14), ('Heading 3', 12)]:
        style = doc.styles[key]
        style.font.name = 'Noto Sans CJK SC'
        style.font.size = Pt(size)
        style.element.rPr.rFonts.set(qn('w:eastAsia'), 'Noto Sans CJK SC')
        style.paragraph_format.space_after = Pt(7)
        style.paragraph_format.line_spacing = 1.25
    section.header.paragraphs[0].text = name + ' | ' + title + ('（草稿）' if not source else '')
    footer = section.footer.paragraphs[0]
    footer.text = '第 '
    field = OxmlElement('w:fldSimple')
    field.set(qn('w:instr'), 'PAGE')
    footer._p.append(field)
    footer.add_run(' 页 | ' + ('实际生产源码，不含测试/生成文件' if source else 'Windows截图与Word分页待验收'))
    doc.core_properties.title = name + ' ' + title
    doc.core_properties.subject = '原生桌面版交付；未经Windows界面和Microsoft Word实际分页验收'
    return doc

manual_text = (root / 'docs/操作说明书.md').read_text(encoding='utf-8')
if '## 1 软件介绍' not in manual_text:
    raise RuntimeError('Manual must start with 软件介绍')
manual = setup('操作说明书')
for line in manual_text.splitlines():
    if not line.strip():
        continue
    if line.startswith('# '):
        manual.add_paragraph(line[2:], 'Title')
    elif line.startswith('### '):
        manual.add_heading(line[4:], 2)
    elif line.startswith('## '):
        manual.add_heading(line[3:], 1)
    else:
        paragraph = manual.add_paragraph(line)
        paragraph.paragraph_format.widow_control = True
manual.save(root / 'docs/操作说明书-草稿.docx')

info_text = (root / 'docs/软著信息采集表.md').read_text(encoding='utf-8')
major = info_text.split('## 软件主要功能\n', 1)[1].split('\n## ', 1)[0].strip()
hanzi = len(re.findall('[\u3400-\u4DBF\u4E00-\u9FFF\U00020000-\U0002EBEF]', major))
if hanzi <= 500:
    raise RuntimeError(f'Major functions only {hanzi} Han characters')
info = setup('软著信息采集表')
info.add_heading('软件著作权信息采集表（草稿）', 0)
table = info.add_table(rows=0, cols=2)
table.style = 'Table Grid'
fields = [
    ('软件全称', '汽车附件电机电磁激励频率设计软件'), ('版本', 'V2.0'),
    ('申请人/著作权人', '待申请人核实/填写'), ('权属取得方式', '待申请人核实/填写'),
    ('开发完成日期', '待申请人核实/填写'), ('首次发表日期与地点', '待申请人核实/填写'),
    ('运行平台', 'Windows 10/11 x64；.NET10 WPF；可自包含离线发布'),
    ('开发语言与架构', 'C#、XAML；MVVM；MotorFrequency.sln'),
    ('输入输出', '.motorproj格式1，UTF-8 CSV；成果CSV、SVG、HTML、JSON'),
    ('软件主要功能', major),
    ('技术特点', info_text.split('## 技术特点与材料状态\n', 1)[1].strip()),
    ('材料状态', '草稿。主要功能Unicode汉字数：' + str(hanzi) + '；未提供用户专用申报模板，字段待按当次模板核对。Windows截图、Word分页及逐页排版未执行。'),
]
for key, value in fields:
    row = table.add_row()
    row.cells[0].width = Cm(3.3)
    row.cells[1].width = Cm(13.5)
    row.cells[0].text = key
    row.cells[1].text = value
    # Allow long functional description across pages, with no forced non-splitting rule.
    for cell in row.cells:
        for paragraph in cell.paragraphs:
            paragraph.paragraph_format.keep_together = False
info.save(root / 'docs/软著信息采集表-草稿.docx')

inventory = json.loads((root / 'docs/source-inventory.json').read_text())
source = setup('完整生产源码排印稿', source=True)
normal = source.styles['Normal']
normal.font.name = 'Consolas'
normal.font.size = Pt(7)
normal.element.rPr.rFonts.set(qn('w:eastAsia'), 'Noto Sans CJK SC')
normal.paragraph_format.line_spacing = 1
normal.paragraph_format.space_after = Pt(0)
source.add_heading(name + ' 完整生产源码', 0)
source.add_paragraph(f"实际参与构建的手写生产文件{len(inventory['files'])}个，非空行{inventory['nonblank_lines']}。全部原文排印，不含测试、工具、依赖和生成文件。页数及正式提交范围待申请人按申报规则核定。")
for item in inventory['files']:
    source.add_heading(item['path'], 1)
    source.add_paragraph(item['purpose'] + ' | SHA-256 ' + item['sha256'])
    for number, line in enumerate((root / item['path']).read_text().splitlines(), 1):
        paragraph = source.add_paragraph(f'{number:04d}  {line}')
        paragraph.paragraph_format.widow_control = False
source.save(root / 'docs/完整生产源码排印稿.docx')

# Byte/text checks do not imply layout or pagination validation.
for path in ['操作说明书-草稿.docx', '软著信息采集表-草稿.docx', '完整生产源码排印稿.docx']:
    Document(root / 'docs' / path)
result = {
    'major_functions_hanzi': hanzi,
    'manual_first_section': '软件介绍，含应用背景和软件概述',
    'manual_characters': len(manual_text),
    'production_files': len(inventory['files']),
    'production_nonblank_lines': inventory['nonblank_lines'],
    'manual_actual_word_pages': None,
    'word_pagination_verified': False,
    'real_windows_screenshots': 0,
    'template': '沿用备份说明书A4、2.1/2.0厘米页边距、18/14/10.5pt层级；用户专用登记模板未提供',
    'status': 'Draft; DOCX structures/text checked, visual layout and Word pagination NOT executed',
    'pending': ['Windows实际运行和截图', 'Microsoft Word分页不少于10页及逐页排版', '申请人信息/权属/日期/模板核实']
}
(root / 'docs/material-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))

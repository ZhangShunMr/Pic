from pathlib import Path
import json
import unicodedata
from docx import Document
from docx.shared import Pt, Emu
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from manual_content import CHAPTERS, MAIN_FUNCTIONS
from source_inventory import ROOT, inventory, production_files

NAME = '汽车内饰卡扣与防异响接触设计软件'
VERSION = 'V2.0'
DOCS = ROOT / 'docs'

def font(run, name='Microsoft YaHei', size=10.5):
    run.font.name = name
    run.font.size = Pt(size)
    run._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'), name)

def document(kind):
    d = Document()
    # Copied dimensions/margins from the preserved prior material, not inferred page counts.
    s = d.sections[0]
    s.page_width = Emu(7560310)
    s.page_height = Emu(10692130)
    s.top_margin = s.bottom_margin = Emu(647700)
    s.left_margin = s.right_margin = Emu(647700)
    s.header_distance = s.footer_distance = Emu(270000)
    d.styles['Normal'].font.name = 'Microsoft YaHei'
    d.styles['Normal'].font.size = Pt(10.5)
    d.styles['Normal']._element.rPr.rFonts.set(qn('w:eastAsia'), 'Microsoft YaHei')
    d.styles['Normal'].paragraph_format.space_after = Pt(7)
    d.styles['Normal'].paragraph_format.line_spacing = 1.15
    s.header.paragraphs[0].text = NAME + ' ' + VERSION + ' | ' + kind
    for run in s.header.paragraphs[0].runs: font(run, size=8)
    p = s.footer.paragraphs[0]
    p.alignment = 2
    p.add_run('第 ')
    for token in ['PAGE', 'NUMPAGES']:
        field = OxmlElement('w:fldSimple'); field.set(qn('w:instr'), token); p._p.append(field)
        p.add_run(' / ' if token == 'PAGE' else ' 页')
    for run in p.runs: font(run, size=8)
    return d

def paragraph(d, text):
    p = d.add_paragraph(text)
    for run in p.runs: font(run)
    return p

report = inventory()
manual = document('操作说明 · 待Windows核验草稿')
markdown = '# 操作说明书 V2.0（待Windows核验草稿）\n\n'
for i, (title, body) in enumerate(CHAPTERS):
    if i: manual.add_page_break()
    manual.add_heading(f'{i+1} {title}', 1)
    for text in body.splitlines():
        if text.strip(): paragraph(manual, text)
    markdown += f'## {i+1} {title}\n\n' + body + '\n\n'
manual.save(DOCS / '操作说明书-待Windows核验.docx')
(DOCS / '操作说明书.md').write_text(markdown.rstrip()+"\n", encoding='utf-8')

fields = [
('软件全称', NAME),
('版本号', VERSION + '；项目schema 1；模型RigidContact-1.0'),
('软件简称', '待申请人核实/填写（程序未声明简称）'),
('申请人、国籍、证件和联系方式', '待申请人核实/填写'),
('著作权人、权属来源及开发方式', '待申请人核实/填写'),
('开发完成日期', '待申请人核实/填写；不以本次生成或验证日期代替'),
('首次发表状态、日期、地点', '待申请人核实/填写；代码推送与法定发表事实分别核实'),
('语言与工程', 'C#、WPF XAML；TrimContact.sln；MVVM；Windows原生桌面应用'),
('运行环境', 'Windows10/11 x64，自包含.NET10离线发布；Linux交叉编译及业务测试已执行，Windows原生界面实机验证待完成'),
('开发工具与依赖', '.NET SDK10.0.403；生产无第三方NuGet代码依赖；官方WindowsDesktop框架；Python仅用于开发材料工具'),
('主要功能', MAIN_FUNCTIONS),
('源程序量', f"{report['totalNonempty']}行手写生产C#与XAML非空行；测试、工具、生成文件、依赖、产物、文档不计。逐文件SHA256见源码清单。"),
('文件格式', '.trimproj（schema1，2MB上限）；布置CSV；HTML、CSV及分析JSON；备份.bak'),
('模板与申报范围', '沿用备份版采集表字段顺序、A4页幅与边框；未收到申请人专用模板。Word跨页版式、签章、正式源码范围与页数待申请人核实。材料为草稿，尚无真实WPF截图。')]
collection = document('软著信息采集表 · 草稿')
collection.add_heading(NAME + ' ' + VERSION, 1)
paragraph(collection, '以下程序事实可由代码与证据核对；申请人事实必须本人核实。Windows实机与Word分页尚未完成。')
t = collection.add_table(rows=0, cols=2)
t.style = 'Table Grid'
for key, value in fields:
    cells = t.add_row().cells
    cells[0].text, cells[1].text = key, value
    cells[0].width = Emu(1550000)
    cells[1].width = Emu(4500000)
    for cell in cells:
        for p in cell.paragraphs:
            for run in p.runs: font(run)
collection.save(DOCS / '软著信息采集表-草稿.docx')
(DOCS / '软著信息采集表.md').write_text('# 软著信息采集表草稿\n\n' + '\n\n'.join(f'## {k}\n\n{v}' for k, v in fields), encoding='utf-8')

source = document('完整生产源码打印稿 · 实际构建范围')
source.add_heading(NAME + ' ' + VERSION + ' 完整生产源码', 1)
paragraph(source, '实际参与Core/Desktop构建的手写C#与XAML，按源码清单顺序。统计非空行，不含测试、工具、依赖、生成与产物。正式提交范围、页数和Word版式待当次申报核实。')
text = []
for path in production_files():
    relative = path.relative_to(ROOT).as_posix()
    source.add_heading(relative, 2)
    text.append('\n===== ' + relative + ' =====\n')
    for number, line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
        p = source.add_paragraph()
        p.paragraph_format.space_after = Pt(0)
        p.paragraph_format.line_spacing = 1
        run = p.add_run(f'{number:04d} {line}')
        font(run, 'Consolas', 8)
        text.append(f'{number:04d} {line}'.rstrip())
source.save(DOCS / '完整生产源码打印稿.docx')
(DOCS / '完整生产源码清单.txt').write_text('\n'.join(text), encoding='utf-8')
han = sum(unicodedata.name(c, '').startswith('CJK UNIFIED IDEOGRAPH') for c in MAIN_FUNCTIONS)
assert han > 500
checks = {'majorFunctionsHanCount': han, 'minimumExclusive': 500,
          'introFirst': CHAPTERS[0][0] == '软件介绍', 'manualChapters': len(CHAPTERS),
          'actualWordPageCount': None, 'actualWordPdfPageCount': None,
          'wordLayoutVerified': False, 'wpfScreenshots': 0,
          'status': 'Draft; genuine Windows screenshots, Windows UI workflow and Microsoft Word pagination/layout verification pending',
          'productionNonempty': report['totalNonempty'], 'sourceFileCount': len(report['files'])}
(DOCS / 'materials-check.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(checks, ensure_ascii=False))

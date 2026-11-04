from pathlib import Path
import json
import unicodedata
import hashlib
import struct
from docx import Document
from docx.shared import Pt, Emu, Inches
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from manual_content import CHAPTERS, MAIN_FUNCTIONS
from source_inventory import ROOT, inventory, production_files

NAME = '汽车内饰卡扣与防异响接触设计软件'
VERSION = 'V2.0'
DOCS = ROOT / 'docs'
WINDOWS = DOCS / 'windows'
def evidence(name):
    path = WINDOWS / name
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
ui = evidence('windows-ui-evidence.json')
word = evidence('word-pagination.json')
native_ok = bool(ui) and ui.get('failed') == 0
figures = {
    1: [('01-empty', '空白项目的真实WPF工作台')],
    2: [('02a-project-fields', '项目身份、来源与依据字段')],
    3: [('04-layout', '饰板、卡扣和中心接触点布置（模拟）')],
    4: [('04a-contact-fields', '单边接触点的原生参数表（模拟）')],
    5: [('05-loads', '共用载荷与相位、公差设置（模拟）')],
    6: [('03-invalid-number', '真实数值转换错误的红框和阻断状态')],
    7: [('06-envelope', '逐点结果包络（模拟）'), ('07-nominal-traces', '名义公差接触压缩量曲线（模拟）'), ('08-force-balance', '独立力与力矩核查（模拟）')],
    8: [('11-scan', '方案比较和四值预紧扫描（模拟）')],
    9: [('12a-csv-preview', 'CSV整批预检与模式'), ('12-batch', '批量预览的字段、单位和前后值')],
    10: [('13-verification-tasks', '由模拟结果生成的验证任务')],
    11: [('13a-project-library', '本机项目库、存储位置与恢复入口')]
}
figure_records = []

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
manual = document('操作说明 · 待Microsoft Word核验草稿')
markdown = '# 操作说明书 V2.0（待Microsoft Word核验草稿）\n\n'
for i, (title, body) in enumerate(CHAPTERS):
    heading = manual.add_heading(f'{i+1} {title}', 1)
    if i: heading.paragraph_format.page_break_before = True
    if native_ok:
        body = body.replace('当前版本完成Linux交叉编译和自动化业务测试，尚未取得真实WPF运行截图，原生界面绑定、布局和对话框仍须在Windows验证。本文没有用浏览器截图或绘制图代替桌面证据，不能直接作为已完成验收的申报终稿。',
            '当前版本已在Windows Server 2025 runner实际显示WPF窗口并执行输入、命令和业务流程，截图来自该真实原生窗口及其控件。原生文件与确认接口在自动流程中使用替身，真实系统对话框、高DPI及Windows10/11人工兼容检查仍待完成。runner没有Microsoft Word，本文仍需Word实际分页和版式核验，不能直接作为申报终稿。')
    for text in body.splitlines():
        if text.strip(): paragraph(manual, text)
    markdown += f'## {i+1} {title}\n\n' + body + '\n\n'
    if native_ok:
        for filename, caption in figures.get(i, []):
            image = WINDOWS / 'screenshots' / (filename + '.png')
            if not image.exists():
                continue
            number = len(figure_records) + 1
            introduction = paragraph(manual, f'图{number}对应上述操作。图内参数均为模拟；若列未完全显示，可在程序中横向或纵向滚动。')
            introduction.paragraph_format.keep_with_next = True
            picture = manual.add_paragraph()
            picture.paragraph_format.keep_with_next = True
            width, height = struct.unpack('>II', image.read_bytes()[16:24])
            picture.add_run().add_picture(str(image), width=Inches(5.5 if height / width > 0.8 else 6.3))
            caption_paragraph = paragraph(manual, f'图{number} {caption}（真实Windows WPF运行界面）')
            caption_paragraph.style = 'Caption'
            caption_paragraph.paragraph_format.keep_with_next = False
            figure_records.append({'figure': number, 'file': image.relative_to(ROOT).as_posix(), 'caption': caption,
                                   'sha256': hashlib.sha256(image.read_bytes()).hexdigest(), 'width': width, 'height': height,
                                   'sourceCommit': ui.get('sourceCommit'), 'runUrl': ui.get('runUrl')})
            markdown += f'![图{number} {caption}](windows/screenshots/{filename}.png)\n\n'
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
('运行环境', '目标Windows10/11 x64，自包含.NET10离线发布；' + ('Windows Server 2025 runner真实WPF运行已验证，Windows10/11、高DPI和系统对话框人工复核待完成' if native_ok else 'Linux交叉编译及业务测试已执行，Windows原生界面验证待完成')),
('开发工具与依赖', '.NET SDK10.0.403；生产无第三方NuGet代码依赖；官方WindowsDesktop框架；Python仅用于开发材料工具'),
('主要功能', MAIN_FUNCTIONS),
('源程序量', f"{report['totalNonempty']}行手写生产C#与XAML非空行；测试、工具、生成文件、依赖、产物、文档不计。逐文件SHA256见源码清单。"),
('文件格式', '.trimproj（schema1，2MB上限）；布置CSV；HTML、CSV及分析JSON；备份.bak'),
('模板与申报范围', '沿用备份版采集表字段顺序、A4页幅与边框；未收到申请人专用模板。Word跨页版式、签章、正式源码范围与页数待申请人核实。材料为待Word核验草稿，真实截图状态以同目录Windows证据和材料检查表为准。')]
collection = document('软著信息采集表 · 草稿')
collection.add_heading(NAME + ' ' + VERSION, 1)
paragraph(collection, '程序事实可由代码和运行证据核对；申请人事实须本人核实。Windows Server运行已验证，Windows10/11与DPI人工复核、Microsoft Word分页仍待完成。' if native_ok else '以下程序事实可由代码与证据核对；申请人事实必须本人核实。Windows实机与Word分页尚未完成。')
t = collection.add_table(rows=0, cols=2)
t.style = 'Table Grid'
t.autofit = False
t.columns[0].width = Emu(1550000)
t.columns[1].width = Emu(4500000)
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
          'wordLayoutVerified': False, 'wpfScreenshots': len(list((WINDOWS / 'screenshots').glob('*.png'))),
          'windowsUIVerified': native_ok, 'windowsEvidenceRun': ui.get('runUrl'), 'manualFigures': len(figure_records),
          'status': 'Draft; Microsoft Word pagination/layout and Windows 10/11, DPI and shell-dialog human checks pending',
          'productionNonempty': report['totalNonempty'], 'sourceFileCount': len(report['files'])}
(DOCS / 'screenshot-inventory.json').write_text(json.dumps(figure_records, ensure_ascii=False, indent=2), encoding='utf-8')
(DOCS / 'materials-check.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(checks, ensure_ascii=False))

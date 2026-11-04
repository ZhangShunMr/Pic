#!/usr/bin/env python3
"""Generate inspectable source inventory and draft Word material from actual files."""
import hashlib
import json
import re
import subprocess
from pathlib import Path
from docx import Document
from docx.shared import Cm, Pt
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from datetime import datetime, timezone

root = Path(__file__).resolve().parents[1]
docs = root / 'docs'
files = sorted(p for p in (root / 'src').rglob('*')
               if p.suffix in {'.cs', '.xaml'} and not {'bin', 'obj'} & set(p.parts))
entries = []
source = []
for path in files:
    content = path.read_text(encoding='utf-8')
    entries.append({'path': str(path.relative_to(root)),
                    'nonempty_lines': sum(bool(x.strip()) for x in content.splitlines()),
                    'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    source += ['\n===== ' + str(path.relative_to(root)) + ' =====\n', content]
ref = 'backup/pre-windows-native-20261104/blower-resonance-20261104'
old_paths = subprocess.check_output(['git', 'ls-tree', '-r', '--full-tree', '--name-only', ref], cwd=root, text=True).splitlines()
baseline = []
for name in old_paths:
    if name.startswith('blower-resonance/src/') and Path(name).suffix in {'.cs','.razor','.css','.js'}:
        content = subprocess.check_output(['git','show',ref+':'+name],cwd=root,text=True)
        baseline.append({'path': name, 'nonempty_lines': sum(bool(x.strip()) for x in content.splitlines())})
assert baseline, 'Backup production source baseline was not found'
result = {'definition': 'handwritten nonempty production lines; src .cs/.xaml; tests/generated/dependencies/build/material scripts/docs excluded',
          'purpose': 'native desktop platform migration of existing functional scope; minimum 3500',
          'minimum': 3500, 'total': sum(x['nonempty_lines'] for x in entries),
          'baseline_total': sum(x['nonempty_lines'] for x in baseline),
          'baseline_files': baseline, 'files': entries}
(docs/'source-inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(docs/'完整生产源码.txt').write_text(''.join(source),encoding='utf-8')
lines = ['# 生产源码清单\n', f"统计非空行：{result['total']}；最低目标3500。旧平台基线{result['baseline_total']}。\n",
         '| 实际构建文件 | 非空行 | SHA256 |','| --- | ---: | --- |']
lines += [f"| {x['path']} | {x['nonempty_lines']} | {x['sha256']} |" for x in entries]
(docs/'源码清单.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')

def document(title):
    doc = Document()
    section = doc.sections[0]
    section.page_width, section.page_height = Cm(21), Cm(29.7)
    section.top_margin, section.bottom_margin = Cm(2.2), Cm(2)
    section.left_margin, section.right_margin = Cm(2.2), Cm(2.2)
    style = doc.styles['Normal']
    style.font.name = 'Microsoft YaHei'
    style.font.size = Pt(10.5)
    style.element.rPr.rFonts.set(qn('w:eastAsia'),'Microsoft YaHei')
    style.paragraph_format.space_after = Pt(8)
    style.paragraph_format.line_spacing = 1.35
    section.header.paragraphs[0].text = '空调鼓风机风量挡位与共振避让设计软件 V2.0 · 待验收稿'
    footer = section.footer.paragraphs[0]
    footer.add_run('第 ')
    field = OxmlElement('w:fldSimple'); field.set(qn('w:instr'),'PAGE'); footer._p.append(field)
    footer.add_run(' 页（实际分页须在 Microsoft Word 核实）')
    doc.core_properties.created = datetime.now(timezone.utc)
    doc.core_properties.modified = datetime.now(timezone.utc)
    doc.core_properties.title = title
    doc.core_properties.subject = 'Native desktop V2.0; Windows screenshots and Word pagination pending'
    return doc

screens = docs / 'screenshots'
verified_capture = False
if (screens/'windows-ui-evidence.json').exists():
    verified_capture = json.loads((screens/'windows-ui-evidence.json').read_text(encoding='utf-8-sig')).get('failed') == 0
pictures = {
 '2': [('01-empty','图1 原生空白启动窗口')],
 '3': [('02-project','图2 独立模拟项目与来源')],
 '4': [('03-settings','图3 电机和工程规则'), ('16-invalid-input','图4 叶片数为零时的真实错误反馈')],
 '5': [('04-orders','图5 激励阶次工作台')],
 '6': [('05-modes','图6 模态表格选中后联动属性')],
 '7': [('06-plans','图7 挡位方案及属性入口')],
 '8': [('07-frequency-plot','图8 原生绘制的转速频率图'),('08-crossings','图9 交叉转速范围'),('09-contacts','图10 接近组合明细')],
 '9': [('10-scan','图11 六挡约束扫描结果')],
 '10': [('11-comparison','图12 方案比较'),('12-rule-study','图13 九组工程规则试算')],
 '11': [('13-checks','图14 验证任务'),('14-review','图15 质量复核提示')],
 '12': [('15-csv-error','图16 CSV非法转速的行号反馈')],
 '14': [('17-minimum-window','图17 最小窗口实际布局'),('18-help','图18 程序内帮助')]
}
for stem in ['操作说明书','软著信息采集表']:
    doc = document(stem)
    table_rows = []
    current_chapter = None
    def add_captures(chapter):
        if stem != '操作说明书' or not verified_capture:
            return
        if pictures.get(chapter):
            doc.add_paragraph('本节操作界面对应' + '、'.join(c.split(' ',1)[0] for _,c in pictures[chapter]) + '；请结合正文步骤和原始PNG核对选中对象、字段及结果。')
        for name, caption in pictures.get(chapter, []):
            file = screens / (name + '.png')
            if file.exists():
                doc.add_picture(str(file), width=Cm(16.4))
                doc.add_paragraph(caption + '。Windows Server 2025 runner实际显示的生产WPF窗口内容；模拟数据。原始PNG见screenshots。',style='Caption')
    for line in (docs/(stem+'.md')).read_text(encoding='utf-8').splitlines():
        if line.startswith('|'):
            if not line.startswith('| ---'):
                table_rows.append([x.strip() for x in line.strip('|').split('|')])
            continue
        if table_rows:
            table=doc.add_table(rows=0,cols=2); table.style='Table Grid'
            for cells in table_rows:
                row=table.add_row()
                for i,cell in enumerate(cells[:2]): row.cells[i].text=cell
            table_rows=[]
        if line.startswith('# '): doc.add_heading(line[2:],0)
        elif line.startswith('## '):
            add_captures(current_chapter)
            current_chapter = line[3:].split(' ', 1)[0]
            doc.add_heading(line[3:],1)
        elif line.strip(): doc.add_paragraph(line)
    add_captures(current_chapter)
    doc.save(docs/(stem+'-待验收稿.docx'))

source_doc=document('完整生产源码打印稿')
source_doc.add_heading('完整生产源码打印稿 V2.0',0)
source_doc.add_paragraph('仅包含参与构建的手写生产C#与XAML；测试、生成文件、材料脚本和编译产物排除。本文未进行Word实际分页核验，不申报页数。')
for path in files:
    source_doc.add_heading(str(path.relative_to(root)),1)
    p=source_doc.add_paragraph()
    p.paragraph_format.line_spacing=1
    p.paragraph_format.space_after=Pt(0)
    run=p.add_run(path.read_text(encoding='utf-8'))
    run.font.name='Consolas'; run.font.size=Pt(8)
source_doc.save(docs/'完整生产源码打印稿.docx')
functions=(docs/'软著信息采集表.md').read_text(encoding='utf-8').split('## 软件主要功能',1)[1]
count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]', functions))
assert count > 500, count
assert result['total'] >= result['minimum'], result['total']
evidence={'source_nonempty_lines':result['total'],'source_files':len(files),
          'major_function_hanzi':count,'manual_word_actual_pages':None,
          'manual_status':('draft; real Windows WPF captures included; Word pagination pending' if verified_capture else 'draft; real WPF screenshots and Word pagination pending'),
          'screenshot_count':len(list(screens.glob('*.png'))) if verified_capture else 0,'screenshots_fabricated':False}
(docs/'materials-evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(evidence,ensure_ascii=False))

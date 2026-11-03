"""Fixed production-only scope for the C# replacement and legacy baseline."""

import hashlib
import json
from pathlib import Path
from collections import Counter

ROOT = Path(__file__).resolve().parents[1]
files = sorted(p for p in (ROOT / 'src').rglob('*') if p.suffix in {'.cs', '.razor', '.css'} and not {'obj', 'bin'}.intersection(p.parts))
rows = []
languages = Counter()
for path in files:
    data = path.read_bytes()
    text = data.decode('utf-8')
    nonblank = sum(bool(line.strip()) for line in text.splitlines())
    languages[path.suffix] += nonblank
    relative = path.relative_to(ROOT).as_posix()
    if 'AcousticSeal.Core' in relative:
        purpose = {'Domain': '领域对象/输入验证', 'Analysis': '数值/比较/质量复核', 'Operations': '事务/历史/引用保护', 'IO': '项目/导入/持久化/输出'}[path.parent.name]
    elif 'ViewModels' in relative:
        purpose = 'MVVM状态、编辑器和业务命令'
    elif path.suffix == '.razor':
        purpose = '实际编译的页面/控件视图'
    elif path.suffix == '.css':
        purpose = '界面布局和视觉状态'
    else:
        purpose = '应用入口/文件边界/视图绑定'
    rows.append({'path': relative, 'purpose': purpose, 'physical': len(text.splitlines()), 'nonblank': nonblank, 'sha256': hashlib.sha256(data).hexdigest()})
total = sum(row['nonblank'] for row in rows)
metadata = {'scope': 'src/**/*.cs, src/**/*.razor, src/**/*.css; excluding obj/bin', 'legacyBaselineCommit': 'bf7de5ebc0fad67453e01e72db52367c9c92bade', 'legacyProductionNonblank': 3523, 'minimum': 7046, 'productionNonblank': total, 'byExtension': dict(languages), 'exclusions': '测试、工具、文档、截图、示例、依赖、生成文件、配置与解决方案', 'files': rows}
(ROOT / 'docs' / 'source-inventory.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2))
lines = ['# V2.0生产源码清单', '', '固定口径：手写生产源码非空行。C#、Razor、CSS按同一非空行方法统计；不含测试、材料工具、生成文件、bin/obj、文档、截图、示例和配置。旧版基线3523，重构最低7046。', '', '| 文件 | 实际用途 | 物理行 | 非空行 |', '| --- | --- | ---: | ---: |']
for row in rows:
    lines.append(f"| {row['path']} | {row['purpose']} | {row['physical']} | {row['nonblank']} |")
lines += ['', f'生产非空行合计：{total}；按扩展名：{dict(languages)}。', '', '每个生产文件参与当前工程编译或实际页面加载。SHA-256见source-inventory.json。运行python tools/source_inventory.py可重算。正式软著源码提交前后页及页数范围待按当次申报要求核定，当前提供完整源码排印稿。']
(ROOT / 'docs' / '源码清单.md').write_text('\n'.join(lines) + '\n')
listing = []
for row in rows:
    listing += ['', '=' * 80, row['path'], '=' * 80]
    listing += [f'{index:04d}  {line}'.rstrip() for index, line in enumerate((ROOT / row['path']).read_text().splitlines(), 1)]
(ROOT / 'docs' / '完整源码清单.txt').write_text('\n'.join(listing) + '\n')
print(json.dumps({'productionNonblank': total, 'minimum': 7046, 'byExtension': dict(languages)}, ensure_ascii=False))
if total < 7046:
    raise SystemExit('Production code gate not met')

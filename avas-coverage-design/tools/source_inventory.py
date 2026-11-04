#!/usr/bin/env python3
"""Inventory only hand-written production C# and WPF XAML used by this solution."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = sorted(p for p in (ROOT / 'src').rglob('*')
               if p.suffix in {'.cs', '.xaml'} and not {'bin', 'obj'}.intersection(p.parts))

def included(line):
    text = line.strip()
    return bool(text) and not text.startswith(('//', '/*', '*/', '<!--', '* '))

def inventory():
    files = []
    for path in FILES:
        files.append({
            'path': path.relative_to(ROOT).as_posix(),
            'nonempty_lines': sum(included(line) for line in path.read_text(encoding='utf-8').splitlines()),
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'purpose': {'Avas.Core': '模型、计算、音频、文件验证与静态成果', 'Avas.Workspace': 'MVVM状态、命令和流程', 'Avas.Desktop': 'WPF原生视图、绘图与系统交互'}[path.parts[path.parts.index('src') + 1]]
        })
    result = {
        'scope': '参与构建的手写生产 .cs/.xaml 非空行，排除注释专用行、测试、工具、文档、依赖、bin/obj及自动生成文件',
        'legacy_reported_baseline': 3680,
        'minimum': 3500,
        'rationale': '本次为原生宿主迁移，不以功能扩充为目标；采用规范第10条最低3500行，保留旧基线，不以零基线掩盖承继。',
        'total': sum(x['nonempty_lines'] for x in files),
        'csharp': sum(x['nonempty_lines'] for x in files if x['path'].endswith('.cs')),
        'xaml': sum(x['nonempty_lines'] for x in files if x['path'].endswith('.xaml')),
        'files': files
    }
    assert result['total'] >= result['minimum']
    (ROOT / 'docs/source-inventory.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    listing = ['# 实际参与构建的生产源码清单', '', f"总计 {result['total']} 非空行：C# {result['csharp']}，XAML {result['xaml']}。", '', '| 文件 | 非空行 | 用途 |', '|---|---:|---|']
    listing += [f"| {x['path']} | {x['nonempty_lines']} | {x['purpose']} |" for x in files]
    (ROOT / 'docs/materials/源码清单.md').write_text('\n'.join(listing) + '\n', encoding='utf-8')
    text = []
    for path in FILES:
        text += [f'// ===== FILE: {path.relative_to(ROOT).as_posix()} =====', path.read_text(encoding='utf-8').rstrip(), '']
    (ROOT / 'docs/materials/全部生产源码.txt').write_text('\n'.join(text), encoding='utf-8')
    return result

if __name__ == '__main__':
    data = inventory()
    print(json.dumps({k: v for k, v in data.items() if k != 'files'}, ensure_ascii=False, indent=2))

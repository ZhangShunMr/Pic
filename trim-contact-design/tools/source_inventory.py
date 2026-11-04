from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]

def production_files():
    return sorted(p for p in (ROOT / 'src').rglob('*')
                  if p.suffix in ('.cs', '.xaml') and not {'bin', 'obj'}.intersection(p.parts))

def inventory():
    rows = []
    for path in production_files():
        raw = path.read_bytes()
        lines = raw.decode('utf-8-sig').splitlines()
        rows.append({'path': path.relative_to(ROOT).as_posix(),
                     'nonempty': sum(bool(line.strip()) for line in lines),
                     'physical': len(lines), 'sha256': hashlib.sha256(raw).hexdigest()})
    result = {'scope': 'handwritten production .cs and .xaml actually compiled by Core/Desktop; excludes tests, tools, docs, bin, obj, dependencies and artifacts',
              'minimum': 3500, 'totalNonempty': sum(r['nonempty'] for r in rows), 'files': rows}
    (ROOT / 'docs/source-inventory.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    text = '# 生产源码清单\n\n统计：手写生产 .cs/.xaml 非空行；排除测试、工具、文档、生成文件、依赖与产物。\n\n'
    text += f"实际 {result['totalNonempty']} 行；最低目标3500行。平台重做保留独立物理内核，未把Web源码纳入构建或统计。\n\n"
    text += '|文件|非空行|SHA256|\n|---|---:|---|\n'
    text += ''.join(f"|{r['path']}|{r['nonempty']}|{r['sha256']}|\n" for r in rows)
    (ROOT / 'docs/源码清单.md').write_text(text, encoding='utf-8')
    print(f"Production nonempty: {result['totalNonempty']} in {len(rows)} files")
    assert result['totalNonempty'] >= result['minimum']
    return result

if __name__ == '__main__':
    inventory()

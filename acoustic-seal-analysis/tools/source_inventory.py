from pathlib import Path
import hashlib, json
root=Path(__file__).resolve().parents[1]
rows=[]
# Only handwritten production .cs and .xaml in the three build projects.
for p in sorted((root/'src').rglob('*')):
    if p.suffix not in {'.cs','.xaml'} or any(x in p.parts for x in ('bin','obj')): continue
    content=p.read_text(encoding='utf-8')
    rows.append({'path':str(p.relative_to(root)), 'nonblank_lines':sum(bool(x.strip()) for x in content.splitlines()), 'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
result={'scope':'Handwritten production C# and WPF XAML; excludes tests, generated outputs, tools, docs, examples, dependencies', 'previous_web_baseline':7140,'minimum_target':3500,'files':rows,'total_nonblank_lines':sum(row['nonblank_lines'] for row in rows)}
(root/'docs/source-inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
(root/'docs/生产源码清单.txt').write_text('\n\n'.join('文件：'+row['path']+'\n'+(root/row['path']).read_text(encoding='utf-8') for row in rows),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='files'},ensure_ascii=False))

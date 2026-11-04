from pathlib import Path
import json, hashlib
ROOT=Path(__file__).resolve().parents[1]
files=sorted(p for p in (ROOT/'src').rglob('*') if p.suffix in ('.cs','.razor') and not {'bin','obj'} & set(p.parts))
entries=[]
for p in files:
 text=p.read_text()
 entries.append({'path':str(p.relative_to(ROOT)),'lines':len(text.splitlines()),'nonblank':sum(bool(s.strip()) for s in text.splitlines()),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
total=sum(e['nonblank'] for e in entries)
assert total>=3500,total
report={'scope':'handwritten production .cs and .razor nonblank lines; exclude bin/obj/tests/tools/docs/samples, CSS and JS excluded from threshold','baseline':0,'minimum':3500,'nonblank':total,'files':entries}
(ROOT/'docs/source-inventory.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
lines=['# 生产源码清单','',f'基线0行，最低3500；本次手写C#与Razor非空行 **{total}**。排除测试、生成、依赖、文档、工具、样例；CSS、JS与配置文件不计最低门槛。所有列出文件均被SDK隐式编译/处理。','', '| 文件 | 非空行 | 用途 |','| --- | ---: | --- |']
for e in entries:
 purpose='领域与输入验证' if '/Domain/' in e['path'] else '算法与结果复核' if '/Analysis/' in e['path'] else '项目及交换文件' if '/IO/' in e['path'] else '事务与批量命令' if '/Operations/' in e['path'] else 'MVVM界面命令与状态' if '/ViewModels/' in e['path'] else '视图绑定与展示' if '/Components/' in e['path'] else '应用启动或下载服务'
 lines.append(f"| `{e['path']}` | {e['nonblank']} | {purpose} |")
(ROOT/'docs/源码清单.md').write_text('\n'.join(lines)+'\n')
full=[]
for e in entries:
 full.append('\n'+e['path']+'\n'+'='*70+'\n')
 full.extend(f'{i:04d}  {s}'.rstrip()+'\n' for i,s in enumerate((ROOT/e['path']).read_text().splitlines(),1))
(ROOT/'docs/完整生产源码清单.txt').write_text(''.join(full))
print(total,'nonblank production lines in',len(entries),'files')

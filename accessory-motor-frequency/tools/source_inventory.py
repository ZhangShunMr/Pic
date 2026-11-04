from pathlib import Path
from collections import Counter
from datetime import datetime, timezone
import json,hashlib
ROOT=Path(__file__).resolve().parents[1]
files=[]
counts=Counter()
for f in sorted((ROOT/'src').rglob('*')):
    if f.suffix not in ('.cs','.razor','.css','.js') or any(s in f.parts for s in ('obj','bin')): continue
    text=f.read_text()
    lines=sum(bool(x.strip()) for x in text.splitlines())
    counts[f.suffix]+=lines
    files.append({'file':str(f.relative_to(ROOT)),'nonblankLines':lines,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
result={'at':datetime.now(timezone.utc).isoformat(),'scope':'手写生产 C# / Razor / CSS / 必要浏览器互操作 JS 的非空行，排除测试、工具、文档、依赖和生成物','baseline':0,'target':3600,'actual':sum(counts.values()),'languages':dict(counts),'files':files}
(ROOT/'docs/source-inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
md=['# 生产源码清单','',result['scope'],'',f"目标 {result['target']}；实际 {result['actual']} 非空行。",'','| 文件 | 非空行 | 用途 |','|---|---:|---|']
for f in files:
 name=Path(f['file']).name
 purpose=''
 if name.endswith('.razor'):purpose='实际界面组件：'+name.removesuffix('.razor')
 elif name.endswith('.css'):purpose='工作簿视觉、响应式布局与打印样式'
 elif name.endswith('.js'):purpose='下载文件及未保存离开提醒互操作'
 elif '/ViewModels/' in f['file']:purpose='MVVM 输入草稿、命令与状态管理'
 else:purpose='业务模型、算法、格式校验或启动服务：'+name.removesuffix('.cs')
 md.append(f"| {f['file']} | {f['nonblankLines']} | {purpose} |")
(ROOT/'docs/源码清单.md').write_text('\n'.join(md)+'\n')
parts=[]
for f in files:
 parts.append('\n'+'='*72+'\nFILE: '+f['file']+'\n'+'='*72+'\n'+(ROOT/f['file']).read_text())
(ROOT/'docs/完整生产源码清单.txt').write_text('汽车附件电机电磁激励频率设计软件 V1.0\n仅实际参与构建的生产源码，非空行统计另见 source-inventory.json。\n'+''.join(parts))
print(json.dumps({'actual':result['actual'],'target':result['target'],'languages':result['languages']},ensure_ascii=False))
assert result['actual']>=result['target']

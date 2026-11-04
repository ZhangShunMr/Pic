"""Count only built/served handwritten production sources."""
from pathlib import Path
import json,hashlib,collections
root=Path(__file__).resolve().parents[1]
files=[];by=collections.Counter()
for path in sorted((root/'src').rglob('*')):
 if path.suffix not in {'.cs','.razor','.css','.js'} or {'obj','bin'}&set(path.parts):continue
 content=path.read_bytes();lines=sum(bool(line.strip()) for line in content.decode().splitlines())
 by[path.suffix]+=lines
 files.append({'path':str(path.relative_to(root)),'nonblank':lines,'sha256':hashlib.sha256(content).hexdigest()})
result={'baseline':0,'minimum':3500,'productionNonblank':sum(by.values()),'byExtension':dict(by),'rule':'src下.cs/.razor/.css/.js非空手写生产行；排除obj/bin、测试、工具、依赖、文档、示例','files':files}
assert result['productionNonblank']>=3500,result
(root/'docs/source-inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
(root/'docs/完整生产源码.txt').write_text('\n'.join('文件：'+f['path']+'\n'+(root/f['path']).read_text() for f in files))
(root/'docs/源码清单.md').write_text('# 生产源码清单\n\n共'+str(result['productionNonblank'])+'非空行。'+result['rule']+'。\n\n|文件|非空行|\n|---|---:|\n'+'\n'.join('|'+f['path']+'|'+str(f['nonblank'])+'|' for f in files)+'\n')
print(result['productionNonblank'],dict(by))

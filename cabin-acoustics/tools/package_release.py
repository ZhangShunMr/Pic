from pathlib import Path
import zipfile,hashlib,json,shutil
root=Path(__file__).resolve().parents[1]
archive=root/'release/CabinAcoustics-V1.0-portable.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 for p in sorted((root/'publish').rglob('*')):
  if p.is_file():z.write(p,'CabinAcoustics-V1.0/'+str(p.relative_to(root/'publish')))
 for name in ['README.md','run.sh','run.bat']:
  z.write(root/name,'CabinAcoustics-V1.0/'+name)
 for p in sorted((root/'examples').glob('*')):
  z.write(p,'CabinAcoustics-V1.0/examples/'+p.name)
 for name in ['操作说明书.docx','操作说明书-LibreOffice核对.pdf','算法与数据格式.md']:
  z.write(root/'docs'/name,'CabinAcoustics-V1.0/docs/'+name)
meta={'file':archive.name,'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'runtime':'需要.NET 10 ASP.NET Core Runtime或SDK；不内嵌运行时','build':'framework-dependent Release net10.0'}
(root/'release/package.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2))
print(json.dumps(meta,ensure_ascii=False))

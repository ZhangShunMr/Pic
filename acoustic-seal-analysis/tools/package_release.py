from pathlib import Path
import hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
source=root/'artifacts/win-x64'
archive=root/'releases/AcousticSeal-V3.0-win-x64.zip'
archive.parent.mkdir(parents=True,exist_ok=True)
if not (source/'AcousticSeal.Desktop.exe').is_file():raise SystemExit('Publish win-x64 first')
assert (source/'AcousticSeal.Desktop.exe').read_bytes()[:2]==b'MZ'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 for p in sorted(source.rglob('*')):
  if p.is_file():z.write(p,Path('AcousticSeal-V3.0-win-x64')/p.relative_to(source))
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 assert any(name.endswith('/AcousticSeal.Desktop.exe') for name in z.namelist())
manifest={'file':str(archive.relative_to(root)),'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'bytes':archive.stat().st_size,'rid':'win-x64','self_contained':True,'built_on':'Linux x64 .NET SDK 10.0.403','windows_runtime_executed':False,'zip_integrity_test':'passed','contains_exe':True}
(root/'docs/release-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(manifest,ensure_ascii=False))

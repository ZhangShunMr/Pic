from pathlib import Path
from playwright.sync_api import sync_playwright,expect
import subprocess,tempfile,shutil,json,time,urllib.request,os,zipfile
root=Path(__file__).resolve().parents[1]
dotnet=os.environ.get('CABIN_DOTNET','dotnet')
area=Path(tempfile.mkdtemp(prefix='cabin-release-'))
a=area/'original';b=area/'relocated'
with zipfile.ZipFile(root/'release/CabinAcoustics-V1.0-portable.zip') as z:z.extractall(area)
shutil.move(area/'CabinAcoustics-V1.0',a)
project=root/'examples/四座模拟.cabin.json'
checks=[]
def record(name):checks.append({'name':name,'passed':True});print('PASS',name,flush=True)
def start(folder):
 log=open(folder/'server.log','w')
 proc=subprocess.Popen([dotnet,'Cabin.App.dll','--port','8788','--data-directory',str(folder/'Data')],cwd=folder,stdout=log,stderr=log)
 for _ in range(100):
  try:
   with urllib.request.urlopen('http://127.0.0.1:8788/health') as r:
    if r.status==200:return proc,log
  except Exception:time.sleep(.1)
 raise RuntimeError('release start failed: '+(folder/'server.log').read_text())
proc,log=start(a)
try:
 record('发布输出独立启动健康检查')
 for asset in ['app.css','scene.js','_framework/blazor.web.js']:
  with urllib.request.urlopen('http://127.0.0.1:8788/'+asset) as r:assert r.status==200 and len(r.read())>100
 record('发布输出全部交互资源加载')
 with sync_playwright() as p:
  browser=p.chromium.launch(executable_path='/usr/bin/chromium',args=['--no-sandbox'])
  page=browser.new_page();page.goto('http://127.0.0.1:8788');page.wait_for_timeout(1200)
  page.get_by_role('button',name='项目与成果 ↗',exact=True).click()
  page.locator('input[type=file]').set_input_files(str(project))
  expect(page.get_by_role('status')).to_contain_text('预检通过并载入')
  page.get_by_role('button',name='计算声场',exact=True).click()
  expect(page.locator('.seat-card')).to_have_count(4)
  page.get_by_role('button',name='项目与成果 ↗',exact=True).click()
  page.get_by_role('button',name='保存到本机',exact=True).click()
  expect(page.get_by_role('status')).to_contain_text('保存成功')
  assert list((a/'Data').glob('*.cabin.json'))
  record('发布输出导入示例计算并保存到本机')
  browser.close()
finally:
 proc.terminate();proc.wait(timeout=10);log.close()
shutil.move(a,b)
proc,log=start(b)
try:
 with sync_playwright() as p:
  browser=p.chromium.launch(executable_path='/usr/bin/chromium',args=['--no-sandbox']);page=browser.new_page()
  page.goto('http://127.0.0.1:8788');page.wait_for_timeout(1200)
  page.get_by_role('button',name='项目与成果 ↗',exact=True).click()
  page.get_by_role('button',name='打开',exact=True).click()
  page.get_by_role('button',name='计算声场',exact=True).click()
  expect(page.locator('.seat-card')).to_have_count(4)
  expect(page.locator('.scene-note')).to_contain_text('50.44')
  record('发布目录连同Data搬迁后打开重算')
  page.get_by_role('button',name='项目与成果 ↗',exact=True).click()
  page.get_by_role('button',name='研究报告HTML',exact=True).click()
  with page.expect_download() as info:page.get_by_role('link',name='下载 座舱研究-report.html ↓',exact=True).click()
  report=area/'report.html';info.value.save_as(str(report));assert '<svg' in report.read_text(encoding='utf-8-sig')
  record('搬迁后研究报告真实下载')
  browser.close()
 request=urllib.request.Request('http://127.0.0.1:8788/health',headers={'Origin':'https://invalid.example'})
 try:urllib.request.urlopen(request);raise AssertionError('origin accepted')
 except urllib.error.HTTPError as e:assert e.code==403
 record('跨站来源请求拒绝')
finally:
 proc.terminate();proc.wait(timeout=10);log.close()
(root/'docs/release-evidence.json').write_text(json.dumps({'date':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'platform':'Linux x64 / .NET 10 ASP.NET Core runtime / Chromium','checks':checks,'total':len(checks),'unverified':['Windows脚本实际启动','Microsoft Word实际分页']},ensure_ascii=False,indent=2))
shutil.rmtree(area)

"""Bounded long-label layout and actual loopback HTTP checks for V2.0."""
import copy
import http.client
import json
import uuid
from pathlib import Path
from datetime import datetime,timezone
from playwright.sync_api import sync_playwright,expect
root=Path(__file__).resolve().parents[1]
p=json.loads(next((root/'examples').glob('*.sealproj')).read_text())
p['id']=str(uuid.uuid4())
p['name']='长名称模拟项目' * 15
base=copy.deepcopy(p['trials'][0])
p['trials']=[]
p['pairs']=[]
for index in range(200):
    trial=copy.deepcopy(base)
    trial['id']=str(uuid.uuid4())
    trial['name']=f'模拟长名称{index} ' + 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'*5
    trial['repeatGroup']=''
    p['trials'].append(trial)
file=root/'.runtime/layout.sealproj'
file.write_text(json.dumps(p,ensure_ascii=False))
checks=[]
with sync_playwright() as pw:
    browser=pw.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
    for width,height in [(1440,1000),(1024,768),(390,844)]:
        page=browser.new_page(viewport={'width':width,'height':height})
        page.goto('http://127.0.0.1:8765')
        expect(page.locator('[data-interactive="true"]')).to_be_visible()
        page.locator('#project-upload').set_input_files(file)
        page.get_by_test_id('confirm-action').click()
        expect(page.get_by_test_id('trial-count')).to_have_text('200')
        page.locator('[data-page="trials"]').click()
        expect(page.get_by_test_id('trial-table').locator('tbody tr')).to_have_count(40)
        assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
        page.get_by_role('button',name='下一页',exact=True).click()
        expect(page.get_by_text('第 2 页 · 每页40条',exact=True)).to_be_visible()
        checks.append({'width':width,'height':height,'longLabels':True,'trialRows':200,'pagination':40,'pageOverflow':False})
        page.close()
    browser.close()
http_checks=[]
for name,path,headers,status in [
    ('health','/health',{},200),
    ('wrong-host','/health',{'Host':'untrusted.example'},403),
    ('cross-origin','/health',{'Origin':'https://untrusted.example'},403),
    ('private-source','/src/AcousticSeal.Core/Domain/Project.cs',{},404),
]:
    connection=http.client.HTTPConnection('127.0.0.1',8765)
    connection.request('GET',path,headers=headers)
    response=connection.getresponse()
    body=response.read()
    assert response.status==status,(name,response.status)
    if name=='health':
        assert json.loads(body)['version']=='2.0.0'
        assert response.getheader('X-Content-Type-Options')=='nosniff'
    http_checks.append({'check':name,'expected':status,'actual':response.status})
    connection.close()
(root/'docs/layout-http-evidence.json').write_text(json.dumps({'date':datetime.now(timezone.utc).isoformat(),'layout':checks,'http':http_checks,'limit':'仅200行布局及本机HTTP边界，不是最大规模性能、远程或多用户安全验收'},ensure_ascii=False,indent=2))
print(json.dumps({'layouts':len(checks),'http':len(http_checks)},ensure_ascii=False))

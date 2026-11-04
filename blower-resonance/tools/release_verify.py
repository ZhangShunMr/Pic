"""Smoke verification against the relocated publish output at port5179."""
from pathlib import Path
from playwright.sync_api import sync_playwright,expect
import json,time,hashlib
root=Path(__file__).resolve().parents[1];steps=[]
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
 context=b.new_context(accept_downloads=True);page=context.new_page();page.on('dialog',lambda d:d.accept())
 page.goto('http://127.0.0.1:5179');page.wait_for_timeout(700)
 expect(page.locator('.empty-canvas')).to_be_visible();steps.append('relocated publish blank startup')
 r=page.request.get('http://127.0.0.1:5179/_framework/blazor.web.js');assert r.status==200 and len(r.body())>10000
 steps.append('framework script served from published assets')
 page.get_by_role('button',name='查看模拟示例',exact=True).click();page.wait_for_timeout(300)
 page.get_by_role('button',name='确认继续',exact=True).click();expect(page.locator('.frequency-plot')).to_be_visible()
 steps.append('interactive simulation calculation')
 page.locator('.project-actions').evaluate('(e)=>e.open=true')
 page.get_by_role('button',name='导入与成果输出',exact=True).click();page.wait_for_timeout(200)
 page.get_by_label('打开 .blowerproj 项目（UTF-8，≤4MiB）').set_input_files(str(root/'examples/示例项目.blowerproj'));page.wait_for_timeout(300)
 page.get_by_role('button',name='确认继续',exact=True).click();page.wait_for_timeout(300)
 expect(page.locator('.tree-root')).to_contain_text('副本');expect(page.locator('.check-card')).to_have_count(4)
 steps.append('project transported to independent release instance with all four verification records')
 page.get_by_role('button',name='保存项目',exact=True).click();page.wait_for_timeout(250)
 expect(page.locator('.status-line')).to_contain_text('已保存到');steps.append('save in independent release library')
 page.get_by_role('button',name='生成设计报告 HTML',exact=True).click();page.wait_for_timeout(200)
 with page.expect_download() as d:page.locator('.download-link').click()
 target=root/'docs/发布版本导出核对.html';d.value.save_as(str(target));assert '<svg' in target.read_text()
 steps.append('release HTML report downloaded and checked')
 expect(page.locator('#blazor-error-ui')).not_to_be_visible();b.close()
(root/'docs/release-evidence.json').write_text(json.dumps({'date':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'deployedDirectory':'/workspace/blower-relocated','port':5179,'status':'passed','deployedDllSha256':hashlib.sha256(Path('/workspace/blower-relocated/Blower.App.dll').read_bytes()).hexdigest(),'checks':steps},ensure_ascii=False,indent=2))
print(json.dumps(steps,ensure_ascii=False))

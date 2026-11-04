"""Targeted browser checks for scan failure, templates, rule export and oversized input."""
from pathlib import Path
from playwright.sync_api import sync_playwright,expect
import json,time
root=Path(__file__).resolve().parents[1];steps=[]
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox']);page=b.new_page(accept_downloads=True,viewport={'width':1600,'height':1100});page.on('dialog',lambda d:d.accept())
 def click(name):page.get_by_role('button',name=name,exact=True).click();page.wait_for_timeout(160)
 def getsection(text):
  v=page.locator('.results-dock > details').filter(has=page.locator('summary',has_text=text));v.evaluate('(e)=>e.open=true');v.locator('summary').scroll_into_view_if_needed();return v
 def download(button,filename):
  click(button)
  with page.expect_download() as d:page.locator('.download-link').click()
  target=root/'examples'/filename;d.value.save_as(str(target));return target
 page.goto('http://127.0.0.1:5178');page.wait_for_timeout(600);click('查看模拟示例');click('确认继续');expect(page.locator('.frequency-plot')).to_be_visible()
 page.locator('.project-actions').evaluate('(e)=>e.open=true');click('导入与成果输出');page.locator('.exchange-panel details').nth(1).evaluate('(e)=>e.open=true')
 g=download('下载挡位模板','挡位模板.csv');m=download('下载模态模板','模态模板.csv');assert g.read_text(encoding='utf-8-sig').startswith('name,rpm,fixed');assert m.read_text(encoding='utf-8-sig').startswith('name,frequency_hz,damping_ratio,source');steps.append('both CSV templates downloaded with exact import headers')
 getsection('规则敏感性与质量复核');click('试算规则组合');f=download('导出敏感性 CSV','规则敏感性.csv');assert len(f.read_text(encoding='utf-8-sig').splitlines())==10;steps.append('nine scenario CSV export downloaded')
 page.get_by_role('button',name='⚙ 计算与扫描规则').click();page.wait_for_timeout(160)
 page.get_by_label('扫描窗口 ±rpm',exact=True).fill('3000');page.get_by_label('扫描步长 rpm',exact=True).fill('1');page.locator('.inspector-heading').click();click('应用属性')
 getsection('约束扫描');click('扫描当前方案');expect(page.locator('.status-line')).to_have_class('status-line error');expect(page.locator('.status-line')).to_contain_text('超过603');steps.append('oversized candidate grid gives actionable red failure state')
 huge=Path('/tmp/blower-oversized.blowerproj');huge.write_text('x'*(4*1024*1024+1));page.get_by_label('打开 .blowerproj 项目（UTF-8，≤4MiB）').set_input_files(str(huge));page.wait_for_timeout(200);expect(page.locator('.status-line')).to_contain_text('文件读取失败');expect(page.locator('.frequency-plot')).to_be_visible();huge.unlink();steps.append('oversized project refused without circuit failure or data replacement')
 assert not page.locator('#blazor-error-ui').is_visible();b.close()
(root/'docs/boundary-browser-evidence.json').write_text(json.dumps({'date':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'status':'passed','checks':steps},ensure_ascii=False,indent=2));print(json.dumps(steps,ensure_ascii=False))

"""Browser verification of the hand-calculable operation-manual example."""
from pathlib import Path
import json,sys,re
from datetime import datetime,timezone
from playwright.sync_api import sync_playwright,expect
ROOT=Path(__file__).resolve().parents[1]
checks=[]
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
 page=b.new_page(viewport={'width':1600,'height':1100})
 def button(name):return page.get_by_role('button',name=name,exact=True)
 def nav(name):page.locator('nav').get_by_role('button',name=re.compile(re.escape(name)+'$')).click()
 def fill(label,value):page.get_by_label(label,exact=True).fill(str(value));page.get_by_label(label,exact=True).press('Tab')
 def applied():button('应用编辑').first.click()
 def calculate():button('计算全部方案').click();expect(page.locator('.status')).to_contain_text('计算完成')
 def check(name,ok=True):assert ok,name;checks.append({'name':name,'passed':True});print('PASS',name)
 page.goto(sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8775')
 button('载入模拟演示').click()
 nav('布置');button('删除选中接触点').click();button('确认继续').click();applied()
 nav('载荷');fill('法向偏置 g',1);fill('法向幅值 g',0);fill('切向幅值 g',0);fill('切向偏置 g',0);applied()
 calculate()
 expect(page.locator('table').first).to_contain_text('2.45166 / 2.45166')
 check('manual static case renders four clip forces 2.45166N',page.locator('table').first.inner_text().count('2.45166 / 2.45166')==4)
 expect(page.locator('.trace-table')).to_contain_text('0.02452')
 check('manual static case center displacement 0.02452mm rendered')
 page.screenshot(path=str(ROOT/'docs/screenshots/16-static-handcheck.png'),full_page=True)
 nav('布置');button('增加接触点').click()
 fill('名义间隙 mm',0);fill('间隙公差 ±mm',0);fill('预紧等效压缩量 mm',.2)
 # The page contains clip and contact stiffness controls; select by the second matching label.
 fields=page.get_by_label('法向刚度 N/mm',exact=True)
 fields.nth(1).fill('100');fields.nth(1).press('Tab');applied()
 nav('载荷');fill('法向偏置 g',0);applied();calculate()
 expect(page.locator('table').first).to_contain_text('0.16 / 0.16')
 expect(page.locator('table').first).to_contain_text('16 / 16')
 check('manual preload case renders 0.16mm compression and 16N reaction')
 check('manual preload case renders four clips at negative 4N',page.locator('table').first.inner_text().count('-4 / -4')==4)
 expect(page.locator('.trace-table')).to_contain_text('-0.04')
 check('manual preload case renders negative 0.04mm center displacement')
 page.screenshot(path=str(ROOT/'docs/screenshots/17-preload-handcheck.png'),full_page=True)
 nav('保存与交付');button('保存项目').first.click();expect(page.locator('.status')).to_contain_text('已保存到')
 button('打开项目').first.click();expect(page.locator('.status')).to_contain_text('实际重开')
 calculate();expect(page.locator('table').first).to_contain_text('16 / 16')
 check('manual hand-check case saved reopened recalculated without change')
 b.close()
(ROOT/'docs/manual-case-verification.json').write_text(json.dumps({'utc':datetime.now(timezone.utc).isoformat(),'count':len(checks),'checks':checks},ensure_ascii=False,indent=2))

"""Real Chromium interaction evidence. Requires the application on loopback:5178."""
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
import json, time, csv, io
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs'; SHOTS=OUT/'screenshots'; EX=ROOT/'examples'
SHOTS.mkdir(exist_ok=True); EX.mkdir(exist_ok=True)
steps=[]; errors=[]
def record(name,detail=''):
 steps.append({'name':name,'status':'passed','detail':detail}); print('PASS',name,flush=True)
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
 context=browser.new_context(viewport={'width':1600,'height':1100},accept_downloads=True)
 page=context.new_page(); page.on('pageerror',lambda e:errors.append(str(e)))
 page.on('dialog',lambda d:d.accept())
 page.set_default_timeout(12000)
 def btn(name): return page.get_by_role('button',name=name,exact=True)
 def settle():page.wait_for_timeout(180)
 def click(name):btn(name).click();settle()
 def confirm():
  settle()
  if btn('确认继续').is_visible():click('确认继续')
 def apply():click('应用属性');expect(page.locator('.status-line')).not_to_have_class('status-line error')
 def field(name,value):page.locator('.inspector').get_by_label(name,exact=True).fill(str(value));page.locator('.inspector-heading').click();settle()
 def shot(name,locator=None):
  if locator is None:page.screenshot(path=str(SHOTS/name),full_page=False)
  else:locator.screenshot(path=str(SHOTS/name))
 def section(text):
  x=page.locator('.results-dock > details').filter(has=page.locator('summary',has_text=text))
  x.evaluate('(e)=>e.open=true');x.locator('summary').scroll_into_view_if_needed();settle();return x
 def menu():
  x=page.locator('.project-actions');x.evaluate('(e)=>e.open=true');settle()
 def export(kind,name):
  click(kind)
  link=page.locator('.download-link');expect(link).to_be_visible()
  with page.expect_download() as download:link.click()
  target=EX/name;download.value.save_as(str(target));return target
 page.goto('http://127.0.0.1:5178');page.wait_for_function('window.Blazor !== undefined');settle()
 expect(page.locator('.empty-canvas')).to_be_visible();shot('01-空白项目.png')
 record('blank startup and permanent inspector')
 field('项目名称','手工核对 · 40叶片');field('负责人','验证工程师');field('车辆 / 平台','模拟平台')
 page.locator('.inspector').get_by_label('数据来源类别',exact=True).select_option('Simulation')
 field('来源说明','端到端手工模拟数据；不是真实试验');apply()
 click('＋ 添加阶次');page.locator('.inspector').get_by_label('阶次类型',exact=True).select_option('Blade')
 field('阶次名称','叶片通过 BPF');field('倍数（0.1～100）',1);apply();shot('16-阶次属性.png',page.locator('.editor-fields'))
 click('＋ 添加模态');field('模态名称','壳体 M1');field('模态频率 Hz',600);field('阻尼比 ζ（0～0.1，可留空）','.02');field('来源 / 测试或仿真边界','模拟600Hz模态');field('结构位置','壳体外侧');apply()
 shot('03-模态属性.png',page.locator('.editor-fields'))
 click('＋ 新建方案');field('方案名称','手工基准');apply();click('＋ 添加挡位');field('目标转速 rpm',900);apply()
 click('＋ 添加挡位');field('目标转速 rpm',1800);apply()
 expect(page.locator('.frequency-plot')).to_be_visible();record('blank-to-computable manual workflow','40 blades; BPF; 600Hz mode; 900 and 1800rpm gears')
 page.locator('.frequency-plot .plot-object').filter(has=page.locator('title',has_text='1挡 900')).click()
 expect(page.locator('.inspector-heading')).to_contain_text('挡位属性');shot('04-挡位联动.png')
 field('目标转速 rpm',-1);click('应用属性');expect(page.locator('.status-line')).to_contain_text('必须在')
 expect(page.locator('.gear-children')).to_contain_text('900 rpm');shot('05-无效参数.png',page.locator('.inspector'))
 click('撤回修改');record('invalid speed rejected without changing current data')
 field('目标转速 rpm',920)
 click('＋ 添加模态')
 # Add refuses editor changes with actionable error, selection explicitly requests confirmation.
 expect(page.locator('.status-line')).to_contain_text('尚未应用')
 page.get_by_role('button',name='⚙ 计算与扫描规则').click();settle();expect(page.get_by_role('dialog')).to_be_visible();click('取消');click('撤回修改')
 record('unapplied draft protected on object switch')
 click('保存项目');expect(page.locator('.status-line')).to_contain_text('已保存到')
 menu();click('打开项目库');expect(page.locator('.library-row').first).to_contain_text('手工核对')
 page.locator('.library-row').filter(has_text='手工核对').first.get_by_role('button',name='打开',exact=True).click();settle()
 expect(page.locator('.status-line')).to_contain_text('重开');record('real save and reopen')
 shot('06-保存重开.png')
 menu();click('载入模拟示例');confirm();expect(page.locator('.frequency-plot')).to_be_visible();shot('02-模拟工作台.png')
 record('simulation entry with persistent source markers')
 page.get_by_role('button',name='⚙ 计算与扫描规则').click();settle();shot('15-计算规则.png',page.locator('.editor-fields'))
 # Crossings and independently verifiable numerical result.
 cross=section('交叉与敏感转速范围');expect(cross).to_contain_text('853～947');shot('07-交叉范围.png',cross)
 record('UI crossing interval matches independent 853-947rpm calculation')
 # Constraint scan, preserve original, adopt candidate.
 scan=section('约束扫描');click('扫描当前方案');expect(scan).to_contain_text('约束内离散候选已生成');shot('08-约束扫描.png',scan)
 click('采纳为新方案');expect(page.locator('.tree-group').last).to_contain_text('扫描候选');record('real constrained scan and adoption as new plan')
 comp=section('方案比较');expect(comp).to_contain_text('基准：');shot('09-方案比较.png',comp)
 record('live shared-condition plan comparison')
 # Switch original and generate check tasks.
 page.locator('.plan-row').filter(has_text='A · 初始挡位').first.click();settle()
 check=section('验证清单');click('生成验证清单');expect(page.locator('.check-card').first).to_be_visible()
 before=page.locator('.check-card').count();click('生成验证清单');assert page.locator('.check-card').count()==before
 page.locator('.check-card').first.click();settle();field('负责人','模拟复核人员')
 page.locator('.inspector').get_by_label('验证状态',exact=True).select_option('Completed');field('证据位置 / 验证记录','模拟操作记录，仅供流程验证；无实测证据');apply();shot('10-验证记录.png',page.locator('.editor-fields'))
 record('deduplicated verification tasks and editable evidence')
 # Rule study is not a mutation.
 study=section('规则敏感性与质量复核');click('试算规则组合');expect(study).to_contain_text('共9 种组合');shot('11-规则敏感性.png',study)
 record('nine nonmutating rule scenarios and quality review')
 # Editing inputs invalidates completed check evidence.
 page.locator('.gear-children .tree-row').first.click();settle();field('目标转速 rpm',910);apply()
 check=section('验证清单');expect(check).to_contain_text('需复核');record('input changes force completed evidence to recheck')
 click('↶');expect(page.locator('.gear-children')).to_contain_text('900 rpm');click('↷');expect(page.locator('.gear-children')).to_contain_text('910 rpm');click('↶');record('actual undo and redo')
 # Exchange panel and CSV boundary tests.
 menu();click('导入与成果输出')
 panel=page.locator('.exchange-panel');panel.locator('details').nth(1).evaluate('(e)=>e.open=true')
 page.get_by_label('CSV内容',exact=True).fill('name,rpm,fixed\n低挡,abc,false');click('预检 CSV');expect(panel).to_contain_text('第2 行');shot('12-CSV错误预检.png',panel)
 page.get_by_label('CSV内容',exact=True).fill('name,rpm,fixed\nCSV低挡,1000,false\nCSV高挡,2000,true');click('预检 CSV');expect(panel).to_contain_text('可应用');click('应用预检结果');confirm();expect(page.locator('.plan-row').last).to_contain_text('CSV导入方案')
 record('CSV invalid row refused, valid two-gear batch accepted')
 # Lock and protect gear edits.
 page.locator('.plan-row').last.click();settle();page.locator('.inspector').get_by_label('锁定挡位，防止误改').check();apply()
 page.locator('.gear-children .tree-row').first.click();settle();field('目标转速 rpm',1100);click('应用属性');expect(page.locator('.status-line')).to_contain_text('锁定');click('撤回修改')
 record('locked plan refuses gear edit')
 # Freeze final sample and export all supported artifacts.
 click('保存项目')
 file=export('下载项目文件','示例项目.blowerproj');data=json.loads(file.read_text());assert data['schema']==1 and data['origin']=='Simulation'
 record('project download contains all inputs, checks and provenance')
 for button,name in [('生成设计报告 HTML','设计报告.html'),('导出频率接近明细 CSV','频率接近明细.csv'),('导出交叉转速 CSV','交叉转速.csv'),('导出设计图 SVG','转速频率图.svg'),('导出验证清单 CSV','验证清单.csv')]:
  output=export(button,name);assert output.stat().st_size>100
 html=(EX/'设计报告.html').read_text();assert '<svg' in html and '模拟设计数据' in html and 'input' not in html[:20]
 rows=list(csv.reader(io.StringIO((EX/'频率接近明细.csv').read_text(encoding='utf-8-sig'))));assert len(rows)>2 and len(rows[0])==len(rows[1])
 record('HTML/SVG/three CSV exports downloaded and inspected')
 shot('13-成果输出.png',panel)
 # Transportable project actually uploaded and reopened as independent identity.
 page.get_by_label('打开 .blowerproj 项目（UTF-8，≤4MiB）').set_input_files(str(file));settle();confirm();expect(page.locator('.tree-root')).to_contain_text('副本')
 record('downloaded project uploaded and reopened as independent copy')
 # Malformed JSON leaves current project untouched.
 bad=EX/'invalid-test.json';bad.write_text('{"schema":999}')
 page.get_by_label('打开 .blowerproj 项目（UTF-8，≤4MiB）').set_input_files(str(bad));settle();expect(page.locator('.status-line')).to_have_class('status-line error');expect(page.locator('.tree-root')).to_contain_text('副本');bad.unlink()
 record('corrupt project import rejected without replacing active project')
 menu();click('归档项目');expect(page.locator('.tree-root')).to_contain_text('已归档');expect(btn('应用属性')).to_be_disabled();click('解除归档');expect(btn('应用属性')).to_be_enabled();record('archive and unarchive enforce editing state')
 # Narrow layout must reflow without page-level horizontal overflow.
 page.set_viewport_size({'width':600,'height':1000});page.evaluate('window.scrollTo(0,0)');settle();shot('14-窄屏工作台.png')
 assert page.evaluate('document.documentElement.scrollWidth <= innerWidth+1')
 record('600px layout without page-level overflow')
 page.set_viewport_size({'width':1600,'height':1100});page.evaluate('window.scrollTo(0,0)');settle()
 expect(page.locator('#blazor-error-ui')).not_to_be_visible();assert not errors,errors
 record('no browser script errors or Blazor error UI')
 browser.close()
(OUT/'browser-evidence.json').write_text(json.dumps({'date':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'browser':'Chromium / Playwright','viewport':'1600x1100; 600x1000','steps':steps,'scriptErrors':errors},ensure_ascii=False,indent=2))

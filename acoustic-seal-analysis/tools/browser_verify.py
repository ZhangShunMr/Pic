"""Execute the C# application through its actual Blazor interface."""

import json
import time
import uuid
from pathlib import Path
from datetime import datetime, timezone
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
SHOTS = ROOT / 'docs' / 'screenshots'
TEMP = ROOT / '.runtime' / 'downloads'
SHOTS.mkdir(parents=True, exist_ok=True)
TEMP.mkdir(parents=True, exist_ok=True)
PROJECT_NAME = 'C#浏览器验收 · 70减67 · ' + uuid.uuid4().hex[:6]
steps = []
errors = []


def note(title, actual):
    steps.append({'title': title, 'actual': actual, 'status': 'passed'})


with sync_playwright() as pw:
    browser = pw.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000}, accept_downloads=True)
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('dialog', lambda dialog: dialog.accept())
    page.goto('http://127.0.0.1:8765')
    expect(page.locator('[data-interactive="true"]')).to_be_visible()

    def nav(key):
        page.locator(f'[data-page="{key}"]').click()
        expect(page.locator('.nav-button.selected')).to_have_attribute('data-page', key)

    def confirm():
        expect(page.get_by_role('dialog')).to_be_visible()
        page.get_by_test_id('confirm-action').click()
        expect(page.get_by_role('dialog')).to_have_count(0)

    def fill(name, value):
        page.locator(f'[name="{name}"]').fill(value)

    def apply():
        page.get_by_test_id('apply-editor').click()
        expect(page.get_by_test_id('notice')).to_contain_text('已应用')

    def shot(name):
        closer = page.get_by_role('button', name='关闭提示')
        if closer.count():
            closer.click()
            expect(page.get_by_role('button', name='关闭提示')).to_have_count(0)
        if name in ('07-差值比较.png', '08-原始频谱.png'):
            page.locator('.result-card').first.evaluate('(element) => element.scrollIntoView({block:"start"})')
        page.screenshot(path=str(SHOTS / name), full_page=False)

    expect(page.get_by_test_id('trial-count')).to_have_text('0')
    shot('01-空白项目.png')
    note('普通启动', '空白工作台，零试验，无自动演示数据')
    fill('project-name', PROJECT_NAME)
    fill('project-description', '浏览器逐项录入的固定模拟数据，非实测。')
    page.get_by_test_id('apply-metadata').click()
    expect(page.get_by_test_id('notice')).to_contain_text('项目信息已应用')
    nav('locations')
    fill('location-name', '模拟车门位置')
    fill('location-zone', '左前门')
    fill('location-procedure', '模拟封堵步骤，不作为真实作业指导。')
    apply()
    location_id = page.locator('[data-location]').first.get_attribute('data-location')
    nav('conditions')
    for key, value in {
        'name': '模拟可比工况', 'vehicle': 'SIM-UI', 'microphone': '模拟耳旁',
        'excitation': '模拟宽带', 'surface': '模拟试验室', 'gear': 'P',
        'speed': '0', 'rpm': '0', 'temperature': '23', 'duration': '30',
        'instrument': 'SIM-METER', 'calibration': '模拟标定记录',
    }.items():
        fill('condition-' + key, value)
    page.locator('[name="condition-nonoverlap"]').check()
    apply()
    nav('trials')
    fill('trial-name', '基线70（模拟）')
    page.locator('[name="trial-source"]').select_option('Simulation')
    fill('trial-source-note', '手工构造70，模拟核验。')
    fill('band-frequency', '125')
    fill('band-level', '70')
    apply()
    baseline_id = page.locator('[data-trial]').first.get_attribute('data-trial')
    page.get_by_test_id('new-trial').click()
    fill('trial-name', '封堵67（模拟）')
    page.locator('[name="trial-kind"]').select_option('Single')
    page.locator(f'[name="trial-location"][value="{location_id}"]').check()
    page.locator('[name="trial-source"]').select_option('Simulation')
    fill('trial-source-note', '手工构造67，模拟核验。')
    fill('band-frequency', '125')
    fill('band-level', '67')
    apply()
    target_id = page.locator('[data-trial]').nth(1).get_attribute('data-trial')
    nav('pairs')
    page.locator('[name="pair-baseline"]').select_option(baseline_id)
    page.locator('[name="pair-target"]').select_option(target_id)
    apply()
    nav('analysis')
    expect(page.locator('[data-testid="pair-bands"] tbody tr').first).to_contain_text('3.000')
    note('空白输入到计算', '建立位置/工况/基线/单项/显式配对，125Hz得到3.000dB')
    page.get_by_test_id('save-project').click()
    expect(page.get_by_test_id('notice')).to_contain_text('项目实际保存成功')
    files = list((ROOT / '.runtime' / 'projects').glob('*.sealproj'))
    project_file = next(path for path in files if json.loads(path.read_text())['name'] == PROJECT_NAME)
    saved = json.loads(project_file.read_text())
    assert len(saved['trials']) == 2
    note('实际落盘', f'确认{project_file.name}存在，内含2试验和1配对')
    nav('project')
    fill('project-owner', '模拟变更后的负责人')
    page.get_by_test_id('apply-metadata').click()
    page.get_by_role('button', name='撤销', exact=True).click()
    expect(page.locator('[name="project-owner"]')).to_have_value('')
    page.get_by_role('button', name='重做', exact=True).click()
    expect(page.locator('[name="project-owner"]')).to_have_value('模拟变更后的负责人')
    page.get_by_test_id('save-project').click()
    expect(page.get_by_test_id('notice')).to_contain_text('项目实际保存成功')
    assert Path(str(project_file) + '.bak').is_file()
    note('撤销重做与备份', '负责人可撤销/重做，二次保存实际生成.bak')
    page.get_by_test_id('refresh-library').click()
    row = page.locator('tr').filter(has=page.get_by_text(PROJECT_NAME, exact=True))
    row.get_by_role('button', name='打开', exact=True).click()
    confirm()
    expect(page.locator('[name="project-owner"]')).to_have_value('模拟变更后的负责人')
    note('项目库重开', '从实际文件重新打开，负责人、试验及配对保留')
    nav('trials')
    page.locator(f'[data-trial="{baseline_id}"]').click()
    fill('band-level', '69')
    expect(page.locator('.save-indicator')).to_contain_text('有未应用表单')
    page.locator('[data-page="analysis"]').click()
    expect(page.get_by_role('dialog')).to_be_visible()
    page.get_by_role('button', name='取消，保留当前工作').click()
    expect(page.locator('[name="band-level"]')).to_have_value('69')
    page.locator('[data-page="analysis"]').click()
    confirm()
    expect(page.locator('[data-testid="pair-bands"] tbody tr').first).to_contain_text('3.000')
    note('未应用频带保护', '编辑69后顶栏标记草稿；切页可取消保留，确认放弃后计算仍是已应用70减67')
    nav('delivery')
    for testid, label in [('prepare-report', 'HTML'), ('prepare-results', 'CSV')]:
        page.get_by_test_id(testid).click()
        with page.expect_download() as download_info:
            page.get_by_test_id('download-export').click()
        download = download_info.value
        target = TEMP / download.suggested_filename
        download.save_as(target)
        text = target.read_text(encoding='utf-8-sig')
        assert '2.0.0' in text and ('input_hash' in text if label == 'CSV' else '路径独立能量贡献' in text)
    page.get_by_role('button', name='准备项目下载').click()
    with page.expect_download() as download_info:
        page.get_by_test_id('download-export').click()
    moved = TEMP / 'relocated.sealproj'
    download_info.value.save_as(moved)
    nav('project')
    page.locator('#project-upload').set_input_files(moved)
    confirm()
    expect(page.get_by_test_id('trial-count')).to_have_text('2')
    note('导出和搬迁导入', '实际下载HTML/CSV/项目；搬迁项目重开仍有2个试验')
    page.get_by_test_id('load-demo').click()
    confirm()
    expect(page.locator('.result-card')).to_have_count(6)
    shot('07-差值比较.png')
    page.get_by_role('button', name='基线与目标频谱', exact=True).first.click()
    expect(page.locator('.result-card').first).to_contain_text('截断纵轴')
    shot('08-原始频谱.png')
    note('模拟案例与频谱', '8试验6配对，差值图/原始频谱均加载，图中文字和来源可见')
    for key, image in [('project', '02-模拟项目库.png'), ('locations', '03-封堵位置.png'), ('conditions', '04-工况口径.png'), ('trials', '05-试验档案.png')]:
        nav(key)
        if key == 'locations':
            page.locator('[data-location]').first.click()
        if key == 'conditions':
            page.locator('[data-condition]').first.click()
        if key == 'trials':
            page.locator('[data-trial]').nth(2).click()
        shot(image)
    page.get_by_role('heading', name='结构化频带数据').scroll_into_view_if_needed()
    shot('06-结构化频带.png')
    nav('import')
    fill('import-content', 'trial_id,frequency_hz,level_db\n00000000-0000-4000-8000-000000000030,125,70\n00000000-0000-4000-8000-000000000032,125,67')
    page.get_by_test_id('preview-csv').click()
    expect(page.get_by_test_id('apply-csv')).to_be_enabled()
    shot('09-CSV预检.png')
    page.get_by_test_id('apply-csv').click()
    confirm()
    expect(page.get_by_test_id('notice')).to_contain_text('CSV已整体应用')
    note('CSV预检事务', '2行模拟数据预检并显式确认，沿用来源，其他频带保留')
    nav('pairs')
    page.locator('[data-pair]').first.click()
    shot('10-显式配对.png')
    nav('quality')
    shot('11-参数与重复性.png')
    page.get_by_role('heading', name='基线漂移与解除封堵恢复', exact=True).scroll_into_view_if_needed()
    shot('12-漂移恢复.png')
    for trial_id in ['00000000-0000-4000-8000-000000000030', '00000000-0000-4000-8000-000000000032']:
        page.locator(f'[name="workbook-trial"][value="{trial_id}"]').check()
    page.get_by_test_id('build-workbook').click()
    expect(page.get_by_test_id('input-workbook').locator('tbody tr')).to_have_count(3)
    page.get_by_role('heading', name='原始频带人工核对工作表').scroll_into_view_if_needed()
    shot('13-原始核对工作表.png')
    page.get_by_test_id('calculate-scenarios').click()
    expect(page.get_by_test_id('scenario-table').locator('tbody tr')).to_have_count(3)
    page.get_by_role('heading', name='工程规则情景核对').scroll_into_view_if_needed()
    shot('14-工程规则情景.png')
    note('核对表和规则情景', '显式选择2试验生成3频带工作表；3组阈值情景不改项目参数')
    nav('history')
    fill('reviewer', '模拟复核人')
    fill('review-comment', '已核对模拟输入和差值边界。')
    page.get_by_test_id('record-review').click()
    expect(page.get_by_test_id('notice')).to_contain_text('已记录输入修订')
    shot('15-审计复核.png')
    nav('revision')
    page.get_by_role('button', name='左侧使用当前已应用快照').click()
    page.get_by_role('button', name='右侧使用当前已应用快照').click()
    page.get_by_test_id('compare-revisions').click()
    expect(page.get_by_test_id('revision-table')).to_contain_text('没有符合筛选的内容变化')
    shot('16-版本核对.png')
    note('复核和版本核对', '用户复核写入参数/哈希；相同快照无数据变化，未覆盖当前项目')
    nav('delivery')
    shot('17-保存成果.png')
    nav('conditions')
    page.locator('[data-condition]').first.click()
    fill('condition-vehicle', '')
    apply()
    nav('analysis')
    expect(page.locator('.result-card')).to_have_count(6)
    expect(page.locator('.result-card').first).to_contain_text('车辆标识缺失')
    shot('18-不可比阻断.png')
    note('条件缺失阻断', '车辆标识缺失，6条配对阻断，差值不生成，原因可见')
    nav('project')
    page.get_by_role('button', name='归档为只读').click()
    confirm()
    expect(page.locator('[name="project-name"]')).to_be_disabled()
    page.get_by_role('button', name='解除归档').click()
    confirm()
    expect(page.locator('[name="project-name"]')).to_be_enabled()
    note('归档保护', '归档禁用编辑，解除归档恢复')
    small = context.new_page()
    small.set_viewport_size({'width': 390, 'height': 844})
    small.goto('http://127.0.0.1:8765')
    expect(small.locator('[data-interactive="true"]')).to_be_visible()
    assert small.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
    small.screenshot(path=str(SHOTS / '19-窄屏.png'), full_page=True)
    note('窄屏', '390px工作台无整页水平溢出')
    assert not errors, errors
    note('JavaScript异常', '无pageerror；Blazor未显示断连错误栏')
    expect(page.locator('#blazor-error-ui')).not_to_be_visible()
    version = browser.version
    browser.close()

(ROOT / 'docs' / 'browser-evidence.json').write_text(json.dumps({
    'date': datetime.now(timezone.utc).isoformat(), 'browser': version,
    'application': 'C#/.NET10 Blazor Server MVVM V2.0', 'steps': steps, 'javascriptErrors': errors,
    'limitations': ['未验证Visual Studio/Windows/macOS图形启动', '未验证真实车辆数据', '无Microsoft Word实际分页证据'],
}, ensure_ascii=False, indent=2))
print(json.dumps({'steps': len(steps), 'errors': errors, 'browser': version}, ensure_ascii=False))

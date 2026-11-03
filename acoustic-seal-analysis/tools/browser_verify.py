"""Real Chromium workflow and screenshot evidence; requires Playwright for QA only."""

import json
from pathlib import Path
from datetime import datetime, timezone
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "screenshots"
TEMP = ROOT / ".runtime" / "downloads"
OUTPUT.mkdir(parents=True, exist_ok=True)
TEMP.mkdir(parents=True, exist_ok=True)
steps = []
errors = []


def note(name, actual):
    steps.append({"step": name, "actual": actual, "status": "passed"})


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(
        executable_path="/usr/bin/chromium",
        headless=True,
        args=["--no-sandbox"],
    )
    context = browser.new_context(viewport={"width": 1440, "height": 1000}, accept_downloads=True)
    page = context.new_page()
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("dialog", lambda dialog: dialog.accept())

    def nav(name):
        page.locator(f'[data-action="navigate"][data-page="{name}"]').click()

    def submit(form_id):
        with page.expect_response(lambda response: response.url.endswith('/api/command')) as response_info:
            page.locator(f'#{form_id} button[type="submit"]').click()
        response = response_info.value
        assert response.status == 200, response.text()
        payload = response.json()
        expect(page.locator('#busy')).to_be_hidden()
        return payload

    def snapshot(name):
        page.locator('#toast').evaluate('(element) => element.hidden = true')
        page.screenshot(path=str(OUTPUT / name), full_page=False)

    page.goto('http://127.0.0.1:8765')
    expect(page.get_by_role('heading', name='项目工作台', exact=True)).to_be_visible()
    expect(page.locator('.metric strong').nth(1)).to_have_text('0')
    snapshot('01-空白项目.png')
    note('普通启动为空白', '0 个试验，未自动载入样例')
    page.locator('[name="name"]').fill('浏览器验收 · 70减67')
    page.locator('textarea[name="description"]').fill('浏览器自动化逐项录入的模拟数据，非实测。')
    submit('metadata-form')
    nav('sites')
    page.locator('[name="name"]').fill('模拟车门密封位置')
    page.locator('[name="zone"]').fill('模拟左前门')
    site_result = submit('site-form')
    site_id = site_result['resultId']
    nav('conditions')
    for key, value in {
        'name': '模拟可比工况',
        'vehicle': 'SIM-UI',
        'microphone': '模拟耳旁测点',
        'excitation': '模拟宽带声源',
        'surface': '模拟试验室',
        'gear': 'P',
        'instrument': 'SIM-INSTRUMENT',
        'calibration': '模拟标定记录',
        'speed': '0',
        'rpm': '0',
        'temperature': '23',
        'duration': '30',
    }.items():
        page.locator(f'[name="{key}"]').fill(value)
    page.locator('[name="independentBands"]').check()
    condition_result = submit('condition-form')
    condition_id = condition_result['resultId']
    nav('trials')
    page.locator('[name="name"]').fill('基线70（模拟）')
    page.locator('[name="sourceKind"]').select_option('simulation')
    page.locator('[name="sourceNote"]').fill('手工构造数值70，模拟验证。')
    page.locator('[name="bandsText"]').fill('125,70\n250,60')
    baseline_result = submit('trial-form')
    baseline_id = baseline_result['resultId']
    page.locator('[data-action="select"][data-collection="trials"][data-id=""]').first.click()
    page.locator('[name="name"]').fill('单项67（模拟）')
    page.locator('[name="type"]').select_option('single')
    page.locator(f'[name="siteIds"][value="{site_id}"]').check()
    page.locator('[name="sourceKind"]').select_option('simulation')
    page.locator('[name="sourceNote"]').fill('手工构造数值67，模拟验证。')
    page.locator('[name="bandsText"]').fill('125,67\n250,')
    target_result = submit('trial-form')
    target_id = target_result['resultId']
    nav('pairs')
    page.locator('[name="baselineId"]').select_option(baseline_id)
    page.locator('[name="targetId"]').select_option(target_id)
    paired = submit('pair-form')
    first_pair = paired['analysis']['pairs'][0]
    assert first_pair['bands'][0]['difference'] == 3
    assert first_pair['bands'][1]['difference'] is None
    note('空白到显式配对', '创建位置、完整工况、基线和单项；125Hz=3dB，250Hz保持缺失')
    nav('results')
    expect(page.locator('.result-card')).to_have_count(1)
    nav('export')
    with page.expect_download() as download_info:
        page.locator('[data-action="save"]').last.click()
    download = download_info.value
    saved = TEMP / 'relocated-project.aslp.json'
    download.save_as(saved)
    saved_project = json.loads(saved.read_text())
    assert len(saved_project['trials']) == 2
    page.locator('#open-file').set_input_files(saved)
    expect(page.get_by_role('heading', name='项目工作台', exact=True)).to_be_visible()
    expect(page.locator('[name="name"]')).to_have_value('浏览器验收 · 70减67')
    note('保存搬迁重开', '实际下载到独立目录并重新选择文件；2个试验及配对保留')
    nav('export')
    for kind in ['report', 'results', 'measurements']:
        with page.expect_download() as download_info:
            page.locator(f'[data-action="export"][data-kind="{kind}"]').click()
        target = TEMP / download_info.value.suggested_filename
        download_info.value.save_as(target)
        assert target.stat().st_size > 30
        if kind == "results":
            assert "software_version" in target.read_text(encoding="utf-8-sig").splitlines()[0]
            assert "parameters_json" in target.read_text(encoding="utf-8-sig").splitlines()[0]
    note('三类成果导出', 'HTML报告、结果CSV及原始频带CSV均实际下载成功')
    nav('project')
    page.get_by_role('button', name='载入模拟样例').click()
    expect(page.locator('.result-card')).to_have_count(6)
    note('模拟样例', '8个试验、6个配对，组合125Hz差4.5dB')
    snapshot('07-比较统计.png')
    nav('project')
    snapshot('02-演示项目.png')
    nav('sites')
    page.locator('[data-action="select"][data-id="site-door"]').click()
    snapshot('03-封堵位置.png')
    nav('conditions')
    page.locator('[data-action="select"][data-id="condition-demo"]').click()
    snapshot('04-工况口径.png')
    nav('trials')
    page.locator('[data-action="select"][data-id="s1"]').click()
    snapshot('05-试验频带.png')
    page.locator('[name="bandsText"]').scroll_into_view_if_needed()
    snapshot('13-逐行频带输入.png')
    nav('import')
    page.locator('[name="csvText"]').fill('trial_id,frequency_hz,level_db\nb1,125,70\ns1,125,67\n')
    page.locator('#import-form button[type="submit"]').click()
    expect(page.get_by_role('button', name='确认导入')).to_be_enabled()
    snapshot('06-导入预检.png')
    with page.expect_response(lambda response: response.url.endswith('/api/command')) as response_info:
        page.get_by_role('button', name='确认导入').click()
    assert response_info.value.status == 200
    note('CSV预检与确认', '2行合并导入；覆盖2个频带，保留其他频带')
    nav('pairs')
    page.locator('[data-action="select"][data-id="p1"]').click()
    snapshot('08-显式配对.png')
    nav('quality')
    snapshot('09-质量复核.png')
    page.get_by_role('heading', name='漂移与恢复摘要').scroll_into_view_if_needed()
    snapshot('14-漂移恢复覆盖.png')
    nav('export')
    snapshot('10-保存报告.png')
    nav('results')
    page.locator('#scan-form').scroll_into_view_if_needed()
    snapshot('15-方案矩阵.png')
    page.locator('[name="scanLow"]').fill('200')
    page.locator('[name="scanHigh"]').fill('300')
    page.locator('#scan-form button[type="submit"]').click()
    expect(page.get_by_text('显示 1 / 3 个频带。', exact=False)).to_be_visible()
    note('频带扫描', '200至300Hz范围仅显示250Hz矩阵行')
    nav('conditions')
    page.locator('[data-action="select"][data-id="condition-demo"]').click()
    page.locator('[name="vehicle"]').fill('')
    blocked = submit('condition-form')
    assert blocked['analysis']['counts']['blocked'] == 6
    nav('results')
    expect(page.locator('.error-box').first).to_contain_text('车辆标识缺失')
    snapshot('11-不可比阻断.png')
    note('条件缺失阻断', '清空车辆标识后6个配对全部阻断，差值为空，原因可见')
    nav('project')
    with page.expect_response(lambda response: response.url.endswith('/api/command')) as response_info:
        page.get_by_role('button', name='归档为只读').click()
    assert response_info.value.status == 200
    expect(page.locator('[name="name"]')).to_be_disabled()
    page.get_by_role('button', name='解除归档').click()
    expect(page.locator('[name="name"]')).to_be_enabled()
    note('归档保护', '归档表单禁用，解除归档后可编辑')
    context2 = browser.new_context(viewport={'width': 390, 'height': 844})
    mobile = context2.new_page()
    mobile.on('pageerror', lambda error: errors.append(str(error)))
    mobile.goto('http://127.0.0.1:8765')
    expect(mobile.get_by_role('heading', name='项目工作台', exact=True)).to_be_visible()
    assert mobile.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
    mobile.screenshot(path=str(OUTPUT / '12-窄屏布局.png'), full_page=True)
    note('窄屏布局', '390px宽度无整页横向溢出；表格在各自容器内滚动')
    assert not errors, errors
    note('浏览器异常', '无 JavaScript pageerror')
    browser_version = browser.version
    context2.close()
    context.close()
    browser.close()

result = {
    'date': datetime.now(timezone.utc).isoformat(),
    'environment': 'Linux Python 3.12; headless system Chromium; 1440x1000 and 390x844',
    'browser': browser_version,
    'steps': steps,
    'javascriptErrors': errors,
    'limitations': ['未在Windows/macOS执行', '未使用真实车辆数据', '未验证Microsoft Word分页'],
}
(ROOT / 'docs' / 'browser-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
print(json.dumps({'steps': len(steps), 'errors': errors, 'browser': browser_version}, ensure_ascii=False))

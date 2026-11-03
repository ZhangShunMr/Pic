"""Bounded layout probe for long labels and 200 simulated trial rows."""

import copy
import json
import sys
from pathlib import Path
from datetime import datetime, timezone
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from acoustic.demo import demonstration

project = demonstration()
project['name'] = '长名称模拟项目' * 15
project['trials'] = []
project['pairs'] = []
source = demonstration()['trials'][0]
for index in range(200):
    trial = copy.deepcopy(source)
    trial['id'] = f'layout-{index}'
    trial['name'] = f'模拟长试验名称{index} ' + 'ABCDEFGHIJKLMNOPQRSTUVWXYZ' * 5
    trial['repeatGroup'] = ''
    project['trials'].append(trial)
path = ROOT / '.runtime' / 'layout.aslp.json'
path.write_text(json.dumps(project, ensure_ascii=False))
checks = []
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox'])
    for width, height in [(1440, 1000), (1024, 768), (390, 844)]:
        page = browser.new_page(viewport={'width': width, 'height': height})
        page.on('dialog', lambda dialog: dialog.accept())
        page.goto('http://127.0.0.1:8765')
        expect(page.get_by_role('heading', name='项目工作台', exact=True)).to_be_visible()
        page.locator('#open-file').set_input_files(path)
        expect(page.locator('[name="name"]')).to_have_value(project['name'])
        page.locator('[data-action="navigate"][data-page="trials"]').click()
        expect(page.locator('.table-wrap tbody tr')).to_have_count(200)
        assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
        checks.append({'width': width, 'height': height, 'trialRows': 200, 'longLabels': True, 'pageOverflow': False})
        page.close()
    browser.close()
(ROOT / 'docs' / 'layout-evidence.json').write_text(json.dumps({
    'date': datetime.now(timezone.utc).isoformat(),
    'checks': checks,
    'limit': '仅布局探测，不是2000试验/200频带最大规模性能验收',
}, ensure_ascii=False, indent=2))
print(json.dumps(checks, ensure_ascii=False))

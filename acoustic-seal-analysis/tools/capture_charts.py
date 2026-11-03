"""Capture full visible charts by scrolling the real application page."""
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
    page=browser.new_page(viewport={'width':1440,'height':1000})
    page.on('dialog',lambda d:d.accept())
    page.goto('http://127.0.0.1:8765')
    expect(page.locator('[data-interactive="true"]')).to_be_visible()
    page.get_by_test_id('load-demo').click()
    page.get_by_test_id('confirm-action').click()
    expect(page.locator('.result-card')).to_have_count(6)
    page.get_by_role('button',name='关闭提示').click()
    expect(page.get_by_role('button',name='关闭提示')).to_have_count(0)
    first=page.locator('.result-card').first
    first.evaluate('(element)=>element.scrollIntoView({block:"start"})')
    page.screenshot(path=str(root/'docs/screenshots/07-差值比较.png'))
    first.get_by_role('button',name='基线与目标频谱').click()
    expect(first).to_contain_text('截断纵轴')
    first.evaluate('(element)=>element.scrollIntoView({block:"start"})')
    page.screenshot(path=str(root/'docs/screenshots/08-原始频谱.png'))
    browser.close()
print('actual charts recaptured after scrolling')

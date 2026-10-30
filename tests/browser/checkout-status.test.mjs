import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../src/EnglishLearningPlatform.Web/wwwroot/js/checkout-status.js', import.meta.url), 'utf8');
async function fixture(run, { settled = true, authExpired = false, delay = 0, failRead = 0 } = {}) {
    let reads = 0, reconciles = 0;
    const server = http.createServer((req, res) => {
        if (req.url === '/Checkout/Status/demo') {
            reads++;
            if (authExpired) { res.writeHead(302, { Location: '/Account/Login' }); res.end(); return; }
            if (reads === failRead) { res.writeHead(503); res.end('Unavailable'); return; }
            res.setHeader('Content-Type', 'application/json');
            const data = JSON.stringify({ status: settled && reads > 1 ? 'Succeeded' : null, canLearn: settled && reads > 1,
                expired: false, resultUrl: '/Checkout/Result/demo' });
            setTimeout(() => res.end(data), delay); return;
        }
        if (req.url === '/reconcile') {
            reconciles++; res.setHeader('Content-Type', 'application/json'); res.end('{"updated":false}'); return;
        }
        res.setHeader('Content-Type', 'text/html; charset=utf-8');
        res.end(req.url === '/Checkout/Result/demo' ? '<h1>Thanh toán thành công</h1><a>Vào học</a>' : `
            <main data-checkout-status-url="/Checkout/Status/demo" data-checkout-pending="true" data-checkout-expired="false" data-vnpay-order-id="demo">
            <a href="https://sandbox.example.test" data-vnpay-pay-link="true" style="display:inline-flex">Thanh toán</a>
            <p data-checkout-message></p></main><script>${script}</script>`);
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true });
    try {
        const page = await browser.newPage();
        await run(page, `http://127.0.0.1:${server.address().port}`, () => ({ reads, reconciles }));
    } finally {
        await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}

test('confirmed status refreshes the result without another purchase', async () => fixture(async (page, url, counts) => {
    await page.goto(url);
    await page.waitForURL('**/Checkout/Result/demo');
    assert.equal(await page.locator('h1').textContent(), 'Thanh toán thành công');
    assert.ok(counts().reads >= 2);
    assert.equal(counts().reconciles, 0);
}));

test('returning to a started order hides repeat payment', async () => fixture(async (page, url, counts) => {
    await page.addInitScript(() => sessionStorage.setItem('vnpay-started:demo', 'true'));
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('[data-vnpay-pay-link]').style.display === 'none');
    assert.equal(await page.locator('[data-vnpay-pay-link]').isVisible(), false);
    assert.equal(counts().reconciles, 0);
    assert.equal(new URL(page.url()).origin, url);
}, { settled: false }));

test('expired login stops polling without navigating the user to another origin', async () => fixture(async (page, url, counts) => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('[data-checkout-message]').textContent.includes('Phiên đăng nhập đã hết hạn'));
    assert.equal(new URL(page.url()).origin, url);
    assert.equal(counts().reads, 1);
}, { authExpired: true }));


test('pending payment waits automatically without a manual check button', async () => fixture(async (page, url, counts) => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('[data-checkout-message]').textContent.includes('Kiểm tra lúc'));
    assert.equal(await page.locator('[data-checkout-check]').count(), 0);
    await page.waitForTimeout(2200);
    assert.ok(counts().reads >= 2);
    assert.equal(page.url(), url + '/');
}, { settled: false }));

test('automatic polling recovers after service error', async () => fixture(async (page, url, counts) => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('[data-checkout-message]').textContent.includes('Chưa kiểm tra được'));
    await page.waitForFunction(() => document.querySelector('[data-checkout-message]').textContent.includes('Kiểm tra lúc'));
    assert.ok(counts().reads >= 3);
    assert.equal(page.url(), url + '/');
}, { settled: false, failRead: 2 }));
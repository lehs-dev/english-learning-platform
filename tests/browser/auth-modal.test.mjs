import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { mkdir, readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { join } from 'node:path';
import { chromium } from 'playwright';

const webRoot = new URL('../../src/EnglishLearningPlatform.Web/', import.meta.url);
let origin;
const catalogPath = '/Courses?Search=english';
const returnUrl = '/Practice?skill=Reading';
const authUrl = action => `/Account/${action}?returnUrl=${encodeURIComponent(returnUrl)}`;
const escapeHtml = value => String(value).replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
const aside = (await readFile(new URL('Views/Shared/_AuthAside.cshtml', webRoot), 'utf8'))
  .replace(/@Url\.Content\("~\/([^"\n]*)"\)/g, '/$1');
const dialog = (await readFile(new URL('Views/Shared/_AuthModal.cshtml', webRoot), 'utf8'))
  .replace(/@Url\.Action\("Index", "Home"\)/g, '/')
  .replace(/@Url\.Content\("~\/([^"\n]*)"\)/g, '/$1');

// Preserve the MVC field names, CSS classes and CSRF field used by the Razor forms.
// The native dialog, aside, scripts and styles come directly from production files.
function authPanel(action = 'Login', { error = '', notice = '', email = '', csrf = true } = {}) {
  const register = action === 'Register';
  const newPasswordAutocomplete = 'new-password';
  const currentPasswordAutocomplete = 'current-password';
  const passwordField = (name, label, confirm = false) => `<div class="form-field">
    <label for="${name}">${label}</label><div class="password-field">
      <input id="${name}" name="${name}" type="password" autocomplete="${register ? newPasswordAutocomplete : currentPasswordAutocomplete}" ${register && !confirm ? 'minlength="8"' : ''} required>
      <button type="button" class="password-toggle" data-password-toggle="${name}" aria-label="Hiện mật khẩu${confirm ? ' xác nhận' : ''}" aria-pressed="false"><svg class="icon" aria-hidden="true"><use href="/images/icons.svg#eye"></use></svg></button>
    </div>${register && !confirm ? '<small class="field-hint">Tối thiểu 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.</small>' : ''}</div>`;
  return `<section class="auth-shell${register ? ' register-shell' : ''}" data-auth-panel aria-labelledby="auth-title">${aside}
    <div class="auth-form-panel"><div class="auth-heading"><h1 id="auth-title">${register ? 'Tạo tài khoản' : 'Đăng nhập'}</h1>
      <p>${register ? 'Tham gia Estudy với tài khoản học viên miễn phí.' : 'Tiếp tục hành trình học tiếng Anh của bạn.'}</p></div>
      ${notice ? `<div class="notice notice-success" role="status">${escapeHtml(notice)}</div>` : ''}
      ${register ? '' : `<div class="demo-account-shortcuts" data-demo-password="Demo_Fixture!123" aria-label="Chọn tài khoản demo">
        <span class="demo-account-label">Chuyển nhanh tài khoản demo</span><div class="demo-account-buttons">
          <button type="button" class="demo-account-button" data-demo-email="student1@demo.example.test" aria-pressed="false">Học viên</button>
          <button type="button" class="demo-account-button" data-demo-email="teacher1@demo.example.test" aria-pressed="false">Giảng viên</button>
          <button type="button" class="demo-account-button" data-demo-email="admin1@demo.example.test" aria-pressed="false">Quản trị</button>
        </div><span class="field-hint">Chọn vai trò để tự động điền email và mật khẩu demo.</span></div>`}
      <form class="account-form" action="/Account/${action}" method="post">
        ${csrf ? '<input type="hidden" name="__RequestVerificationToken" value="fixture-csrf-token">' : ''}
        <input type="hidden" name="ReturnUrl" value="${escapeHtml(returnUrl)}">
        <div class="validation-summary${error ? ' validation-summary-errors' : ''}" role="alert">${escapeHtml(error)}</div>
        ${register ? '<div class="form-field"><label for="FullName">Họ và tên</label><input id="FullName" name="FullName" autocomplete="name" maxlength="200" required></div>' : ''}
        <div class="form-field"><label for="Email">Email</label><input id="Email" name="Email" type="email" autocomplete="${register ? 'email' : 'username'}" value="${escapeHtml(email)}" required></div>
        ${passwordField('Password', 'Mật khẩu')}${register ? passwordField('ConfirmPassword', 'Xác nhận mật khẩu', true) : ''}
        ${register ? '' : '<label class="checkbox-label"><input id="RememberMe" name="RememberMe" type="checkbox" value="true"> <span>Ghi nhớ đăng nhập</span></label><input type="hidden" name="RememberMe" value="false">'}
        <button class="button button-primary button-full" type="submit">${register ? 'Tạo tài khoản' : 'Đăng nhập'}</button>
      </form><p class="auth-switch">${register ? 'Bạn đã có tài khoản?' : 'Bạn chưa có tài khoản?'}
        <a href="${authUrl(register ? 'Login' : 'Register')}">${register ? 'Đăng nhập' : 'Đăng ký ngay'}</a></p>
    </div></section>`;
}

function shell(panel = '') {
  return `<!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="stylesheet" href="/css/site.css"><link rel="stylesheet" href="/css/account.css"></head><body class="canvas-body">
    <main class="container page-content"><h1>Danh sách khóa học</h1><label for="page-query">Tìm khóa học</label><input id="page-query" value="english">
      <p><a id="login-trigger" href="${authUrl('Login')}">Đăng nhập</a> <a id="register-trigger" href="${authUrl('Register')}">Đăng ký</a></p>
      <a id="lowercase-trigger" href="/account/login?returnUrl=${encodeURIComponent(returnUrl)}">Đăng nhập từ khóa học</a>${panel}</main>
    ${dialog}<script src="/js/site.js" defer></script><script src="/js/auth-modal.js" defer></script></body></html>`;
}

function postedFields(request) {
  const body = request.postData() || '';
  const contentType = request.headers()['content-type'] || '';
  if (contentType.includes('application/x-www-form-urlencoded')) return [...new URLSearchParams(body)];
  const boundary = /boundary=(?:"([^"]+)"|([^;]+))/.exec(contentType);
  assert.ok(boundary, 'Auth forms must send their actual browser FormData');
  return body.split(`--${boundary[1] || boundary[2]}`).flatMap(part => {
    const name = /Content-Disposition: form-data; name="([^"]+)"/i.exec(part)?.[1];
    if (!name) return [];
    const start = part.indexOf('\r\n\r\n');
    return [[name, part.slice(start + 4).replace(/\r\n$/, '')]];
  });
}

let browser;
let redirectServer;
const redirectedRequests = [];
before(async () => {
  // Playwright route fulfillment does not intercept follow-up requests after a
  // redirect. Use a real loopback responder for the browser's redirected GET.
  redirectServer = createServer((request, response) => {
    const url = new URL(request.url, origin);
    if (url.pathname === '/Account/Login') {
      redirectedRequests.push({ method: request.method, url: url.href, modal: request.headers['x-auth-modal'] });
      response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      response.end(authPanel('Login', { notice: 'Đăng ký thành công. Vui lòng đăng nhập.' }));
    } else {
      response.writeHead(404);
      response.end();
    }
  });
  await new Promise(resolve => redirectServer.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${redirectServer.address().port}`;
  browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || undefined });
});
after(async () => {
  await browser?.close();
  if (redirectServer) await new Promise(resolve => redirectServer.close(resolve));
});

async function fixture(t, { width = 1024, height = 844, mode = 'invalid', csrf = true, direct = false } = {}) {
  const context = await browser.newContext({ viewport: { width, height }, reducedMotion: 'reduce' });
  t.after(() => context.close());
  const page = await context.newPage();
  page.setDefaultTimeout(5000);
  page.setDefaultNavigationTimeout(10000);
  const errors = [];
  const requests = [];
  let registered = false;
  page.on('pageerror', error => errors.push(error.message));
  t.after(() => assert.deepEqual(errors, [], 'Production auth scripts must run without browser errors'));
  await page.route(`${origin}/**`, async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (/^\/(?:css\/[\w-]+\.css|js\/[\w-]+\.js|images\/icons\.svg)$/.test(url.pathname)) {
      const body = await readFile(new URL(`wwwroot${url.pathname}`, webRoot));
      return route.fulfill({ contentType: url.pathname.endsWith('.css') ? 'text/css' : url.pathname.endsWith('.js') ? 'application/javascript' : 'image/svg+xml', body });
    }
    const auth = /^\/account\/(login|register)\/?$/i.exec(url.pathname);
    if (auth) {
      const action = auth[1].toLowerCase() === 'register' ? 'Register' : 'Login';
      const modal = request.headers()['x-auth-modal'] === 'true';
      if (!modal) return route.fulfill({ contentType: 'text/html', body: shell(authPanel(action, { csrf })) });
      requests.push({ method: request.method(), url: request.url(), modal, fields: request.method() === 'POST' ? postedFields(request) : [] });
      if (request.method() === 'POST') {
        if (mode === 'csrf') return route.fulfill({ status: 400, contentType: 'text/html', body: 'Antiforgery validation failed.' });
        if (mode === 'success') return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ redirectUrl: '/Practice' }) });
        if (action === 'Register' && mode === 'register') {
          registered = true;
          return route.fulfill({ status: 302, headers: { location: authUrl('Login') } });
        }
        const email = requests.at(-1).fields.find(([name]) => name === 'Email')?.[1] || '';
        return route.fulfill({ contentType: 'text/html', body: authPanel(action, { error: 'Email hoặc mật khẩu không đúng.', email, csrf }) });
      }
      return route.fulfill({ contentType: 'text/html', body: authPanel(action, { csrf, notice: registered ? 'Đăng ký thành công. Vui lòng đăng nhập.' : '' }) });
    }
    if (url.pathname === '/Practice') return route.fulfill({ contentType: 'text/html; charset=utf-8', body: '<h1>Luyện tập</h1>' });
    if (url.pathname === '/Courses' || url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: shell() });
    return route.abort();
  });
  await page.goto(`${origin}${direct ? authUrl('Login') : catalogPath}`);
  return { page, requests };
}

async function open(page, trigger = '#login-trigger') {
  await page.locator(trigger).click();
  await page.waitForFunction(() => document.querySelector('[data-auth-dialog]').open && document.querySelector('[data-auth-dialog] [data-auth-panel]'));
  return page.locator('[data-auth-dialog]');
}

async function fillLogin(page) {
  await page.locator('#Email').fill('learner@example.test');
  await page.locator('#Password').fill('Fixture!123');
}

async function screenshot(page, filename) {
  const directory = process.env.ELP_AUTH_MODAL_SCREENSHOTS;
  if (!directory) return;
  await mkdir(directory, { recursive: true });
  await page.screenshot({ path: join(directory, filename), fullPage: true });
}

test('Opening and switching auth forms preserves the background URL, content and ReturnUrl', async t => {
  const { page, requests } = await fixture(t);
  await page.locator('#page-query').fill('đang tìm khóa học');
  const modal = await open(page, '#lowercase-trigger');
  assert.equal(page.url(), `${origin}${catalogPath}`);
  assert.equal(await modal.locator('h1').textContent(), 'Đăng nhập');
  await screenshot(page, 'auth-modal-login-desktop.png');
  assert.equal(await page.locator('#Email').evaluate(el => el === document.activeElement), true);
  assert.equal(await modal.locator('[name="ReturnUrl"]').inputValue(), returnUrl);
  await modal.getByRole('link', { name: 'Đăng ký ngay' }).click();
  await page.waitForFunction(() => Boolean(document.querySelector('#FullName')));
  assert.equal(await modal.locator('h1').textContent(), 'Tạo tài khoản');
  assert.equal(await modal.locator('[name="ReturnUrl"]').inputValue(), returnUrl);
  await modal.getByRole('link', { name: 'Đăng nhập', exact: true }).click();
  await page.waitForFunction(() => !document.querySelector('#FullName'));
  assert.equal(page.url(), `${origin}${catalogPath}`);
  assert.equal(await page.locator('#page-query').inputValue(), 'đang tìm khóa học');
  assert.equal(requests.length, 3);
  for (const request of requests) {
    assert.equal(request.modal, true);
    assert.equal(new URL(request.url).searchParams.get('returnUrl'), returnUrl);
  }
});

test('Validation stays in the popup and dynamically inserted demo/password controls work', async t => {
  const { page, requests } = await fixture(t);
  const modal = await open(page);
  await modal.getByRole('button', { name: 'Học viên', exact: true }).click();
  assert.equal(await page.locator('#Email').inputValue(), 'student1@demo.example.test');
  assert.equal(await page.locator('#Password').inputValue(), 'Demo_Fixture!123');
  const passwordToggle = modal.locator('[data-password-toggle="Password"]');
  await passwordToggle.click();
  assert.equal(await page.locator('#Password').getAttribute('type'), 'text');
  assert.equal(await passwordToggle.getAttribute('aria-pressed'), 'true');
  await page.locator('#RememberMe').check();
  await modal.locator('button[type="submit"]').click();
  await page.waitForFunction(() => Boolean(document.querySelector('.validation-summary-errors')));
  assert.match(await modal.locator('[role="alert"]').textContent(), /Email hoặc mật khẩu không đúng/);
  assert.equal(await modal.locator('.validation-summary-errors').evaluate(el => el === document.activeElement), true);
  assert.equal(await page.locator('#Email').inputValue(), 'student1@demo.example.test');
  assert.equal(page.url(), `${origin}${catalogPath}`);
  const fields = requests.find(request => request.method === 'POST').fields;
  assert.ok(fields.some(([name, value]) => name === '__RequestVerificationToken' && value === 'fixture-csrf-token'));
  assert.ok(fields.some(([name, value]) => name === 'ReturnUrl' && value === returnUrl));
  assert.deepEqual(fields.filter(([name]) => name === 'RememberMe').map(([, value]) => value), ['true', 'false']);
  await modal.getByRole('button', { name: 'Giảng viên', exact: true }).click();
  assert.equal(await page.locator('#Email').inputValue(), 'teacher1@demo.example.test');
  await modal.locator('[data-password-toggle="Password"]').click();
  assert.equal(await page.locator('#Password').getAttribute('type'), 'text');
});

test('Registration follows the login response and displays its success notice in the popup', async t => {
  const { page, requests } = await fixture(t, { mode: 'register' });
  const modal = await open(page, '#register-trigger');
  await page.locator('#FullName').fill('Học viên mới');
  await fillLogin(page);
  await page.locator('#ConfirmPassword').fill('Fixture!123');
  await modal.locator('button[type="submit"]').click();
  await page.waitForFunction(() => Boolean(document.querySelector('.notice-success')));
  assert.equal(await modal.locator('h1').textContent(), 'Đăng nhập');
  assert.match(await modal.locator('.notice-success').textContent(), /Đăng ký thành công/);
  assert.equal(await modal.locator('.notice-success').evaluate(el => el === document.activeElement), true);
  assert.equal(page.url(), `${origin}${catalogPath}`);
  const registration = requests.find(request => request.method === 'POST');
  assert.ok(registration.fields.some(([name, value]) => name === 'ConfirmPassword' && value === 'Fixture!123'));
  assert.ok(registration.fields.some(([name, value]) => name === 'ReturnUrl' && value === returnUrl));
  const redirectedLogin = redirectedRequests.at(-1);
  assert.equal(redirectedLogin.method, 'GET');
  assert.equal(redirectedLogin.modal, 'true', 'A same-origin redirect must preserve the modal request header');
  assert.equal(new URL(redirectedLogin.url).searchParams.get('returnUrl'), returnUrl);
});

test('A successful login navigates to the server-provided local destination', async t => {
  const { page } = await fixture(t, { mode: 'success' });
  const modal = await open(page);
  await fillLogin(page);
  await Promise.all([page.waitForURL(`${origin}/Practice`), modal.locator('button[type="submit"]').click()]);
  assert.equal(await page.getByRole('heading', { name: 'Luyện tập' }).textContent(), 'Luyện tập');
});

test('A missing CSRF token is not fabricated and a rejected POST preserves entered fields', async t => {
  const { page, requests } = await fixture(t, { mode: 'csrf', csrf: false });
  const modal = await open(page);
  await fillLogin(page);
  await modal.locator('button[type="submit"]').click();
  await page.locator('[data-auth-error]').waitFor();
  const fields = requests.find(request => request.method === 'POST').fields;
  assert.equal(fields.some(([name]) => name === '__RequestVerificationToken'), false);
  assert.equal(await page.locator('#Email').inputValue(), 'learner@example.test');
  assert.equal(await page.locator('#Password').inputValue(), 'Fixture!123');
  assert.equal(await modal.locator('button[type="submit"]').isEnabled(), true);
  assert.equal(page.url(), `${origin}${catalogPath}`);
  assert.match(await modal.locator('[data-auth-error]').textContent(), /Phiên biểu mẫu đã hết hạn/);
  const fallback = new URL(await modal.locator('[data-auth-fallback]').getAttribute('href'), origin);
  assert.equal(fallback.searchParams.get('returnUrl'), returnUrl);
});

test('Native dialog contains keyboard focus and restores the trigger after Escape, close and backdrop', async t => {
  const { page } = await fixture(t, { width: 1440, height: 1000 });
  const modal = await open(page);
  assert.equal(await modal.getAttribute('aria-labelledby'), 'auth-title');
  await page.locator('#page-query').evaluate(el => el.focus());
  assert.equal(await modal.evaluate(el => el.contains(document.activeElement)), true);
  for (let i = 0; i < 16; i++) {
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.querySelector('main').contains(document.activeElement)), false, 'Background controls must not receive focus while the dialog is open');
  }
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('[data-auth-dialog]').open && document.activeElement === document.querySelector('#login-trigger'));
  assert.equal(await page.locator('#login-trigger').evaluate(el => el === document.activeElement), true);
  await open(page);
  await modal.locator('[data-auth-close]').click();
  await page.waitForFunction(() => !document.querySelector('[data-auth-dialog]').open && document.activeElement === document.querySelector('#login-trigger'));
  assert.equal(await page.locator('#login-trigger').evaluate(el => el === document.activeElement), true);
  await open(page);
  await page.mouse.click(4, 4);
  await page.waitForFunction(() => !document.querySelector('[data-auth-dialog]').open && document.activeElement === document.querySelector('#login-trigger'));
  assert.equal(await page.locator('#login-trigger').evaluate(el => el === document.activeElement), true);
});

test('A directly opened login page automatically uses the dialog and closes to home', async t => {
  const { page } = await fixture(t, { direct: true });
  await page.waitForFunction(() => document.querySelector('[data-auth-dialog]').open);
  assert.equal(await page.locator('main [data-auth-panel]').count(), 0);
  assert.equal(await page.locator('[data-auth-dialog] [data-auth-panel]').count(), 1);
  assert.equal(await page.locator('#Email').evaluate(el => el === document.activeElement), true);
  await Promise.all([page.waitForURL(`${origin}/`), page.locator('[data-auth-close]').click()]);
});

for (const width of [320, 390]) {
  test(`Register popup fits ${width}px and keeps fields, submit and close reachable`, async t => {
    const { page } = await fixture(t, { width, height: 640 });
    const modal = await open(page, '#register-trigger');
    if (width === 390) await screenshot(page, 'auth-modal-register-390.png');
    const bounds = await modal.boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1, 'Dialog must fit the viewport width');
    assert.ok(bounds.y >= 0 && bounds.y + bounds.height <= 641, 'Dialog must fit the viewport height');
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    assert.equal(await modal.evaluate(el => el.scrollWidth <= el.clientWidth + 1), true, 'Auth content must not overflow horizontally');
    await page.locator('#FullName').fill('Học viên trên điện thoại');
    await fillLogin(page);
    await page.locator('#ConfirmPassword').fill('Fixture!123');
    const submit = modal.locator('button[type="submit"]');
    await submit.scrollIntoViewIfNeeded();
    const buttonBounds = await submit.boundingBox();
    assert.ok(buttonBounds.y >= bounds.y && buttonBounds.y + buttonBounds.height <= bounds.y + bounds.height + 1, 'Submit must scroll into the dialog viewport');
    await modal.evaluate(el => { el.scrollTop = 0; });
    await modal.locator('[data-auth-close]').click();
    await page.waitForFunction(() => !document.querySelector('[data-auth-dialog]').open);
  });
}

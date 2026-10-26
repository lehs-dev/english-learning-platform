import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const webRoot = new URL('../../src/EnglishLearningPlatform.Web/', import.meta.url);
const widths = [320, 390, 768, 900, 901, 1024, 1025, 1080, 1081, 1250, 1440];
const layouts = {
  public: { file: '_Layout.cshtml', navigation: '#main-nav', breakpoint: 900 },
  student: { file: '_LayoutStudent.cshtml', navigation: '#student-nav', breakpoint: 900 },
  teacher: { file: '_LayoutTeacher.cshtml', navigation: '#studio-sidebar', breakpoint: 1024 },
  admin: { file: '_LayoutAdmin.cshtml', navigation: '#admin-sidebar', breakpoint: 1024 },
};

async function accountMenuFixture(role) {
  const raw = await readFile(new URL('Views/Shared/Components/AccountMenu/Default.cshtml', webRoot), 'utf8');
  const guest = raw.match(/<div class="guest-actions">[\s\S]*?<\/div>/);
  const authenticated = raw.match(/<details class="account-menu"[\s\S]*?<\/details>/);
  assert.ok(guest && authenticated, 'Keep fixture branches aligned with the real AccountMenu');
  if (role === 'public') return guest[0];
  return authenticated[0]
    .replace(/@Model\.FullName/g, 'Nguyễn Thị Học Viên')
    .replace(/@Model\.Initial/g, 'HV')
    .replace(/@Model\.RoleLabel/g, { student: 'Học viên', teacher: 'Giảng viên', admin: 'Quản trị viên' }[role])
    .replace(/@if\s*\(User\.IsInRole\("Student"\)\)\s*\{([\s\S]*?)\}/g, (_match, content) => role === 'student' ? content : '');
}

function representativeContent(role) {
  const heading = '<div class="page-heading"><div><h1>Quản lý nội dung và tiến độ học tập</h1><p class="muted">Nội dung đại diện cho màn hình có nhiều dữ liệu.</p></div></div>';
  const cards = `<div class="course-grid">${Array.from({ length: 3 }, (_, i) => `
    <article class="panel course-card"><h2>Khóa học tiếng Anh giao tiếp nâng cao dành cho học viên ${i + 1}</h2>
      <p class="muted">Luyện kỹ năng nghe, nói, đọc và viết trong các tình huống thường gặp.</p>
      <a class="button" href="#fixture-target">Xem chương trình học</a></article>`).join('')}</div>`;
  const form = `<form class="panel editor-form"><fieldset><legend>Thông tin khóa học</legend>
    <label for="course-title">Tên khóa học</label><input id="course-title" value="Khóa học tiếng Anh giao tiếp nâng cao">
    <label for="course-category">Danh mục</label><select id="course-category"><option>Chương trình luyện thi tiếng Anh học thuật dành cho học viên mới bắt đầu</option></select>
    <label for="course-summary">Mô tả</label><textarea id="course-summary" rows="3">Nội dung học tập và kế hoạch luyện tập hằng tuần.</textarea>
    </fieldset><div class="actions"><button class="button" type="button">Lưu thay đổi</button><a class="button secondary" href="#fixture-target">Quay lại danh sách</a></div></form>`;
  const table = `<div class="${role === 'admin' ? 'admin-table-wrap' : 'table-scroll'}" data-fixture-table>
    <table class="${role === 'admin' ? 'admin-table' : 'report-table'}"><thead><tr><th>Học viên / tài khoản</th><th>Khóa học đăng ký</th><th>Ngày tham gia</th><th>Tiến độ và lịch sử</th><th>Trạng thái giao dịch</th></tr></thead>
      <tbody><tr><td>${'nguoidungrattendai'.repeat(8)}@example.test</td><td>Khóa học tiếng Anh giao tiếp nâng cao</td><td>15/10/2026</td><td>Hoàn thành 75 phần trăm chương trình học</td><td>Đã thanh toán thành công</td></tr></tbody></table></div>`;
  if (role === 'admin') {
    return `${heading}<div class="admin-kpi-grid">${Array.from({ length: 4 }, (_, i) => `<article class="admin-kpi-card"><span class="admin-kpi-label">Tổng số giao dịch trong kỳ</span><strong class="admin-kpi-value">${1234 + i}</strong></article>`).join('')}</div>
      <form class="admin-filters"><div><label for="user-query">Tìm kiếm người dùng</label><input id="user-query" value="Nguyễn Thị Học Viên"></div>
        <div><label for="user-role">Vai trò</label><select id="user-role"><option>Học viên đang tham gia chương trình luyện thi tiếng Anh học thuật</option></select></div>
        <button class="button" type="button">Lọc kết quả</button></form>${table}<div id="fixture-target">Kết thúc danh sách</div>`;
  }
  return `${heading}${role === 'teacher' ? '<div class="teacher-stats-grid"><article class="teacher-stat-card"><span class="teacher-stat-label">Học viên đang học</span><strong class="teacher-stat-value">1234</strong></article></div>' : ''}
    ${cards}${form}${role === 'teacher' ? table : ''}<div id="fixture-target">Kết thúc nội dung</div>`;
}

// This is deliberately a small adapter for these layout files, not a Razor engine.
// Keep the real header/sidebar DOM, CSS links and script; retain conditional static
// sections with sample notices. Use the real AccountMenu branch with stable names
// and representative body content, without evaluating server-side conditions.
function stripBalanced(source, pattern, replacement = '', includeBrace = false) {
  for (let match; (match = pattern.exec(source));) {
    const start = match.index;
    const opening = source[start + match[0].length - 1];
    const closing = opening === '(' ? ')' : '}';
    let depth = 1;
    let quote = null;
    let end = start + match[0].length;
    for (; end < source.length && depth; end++) {
      const char = source[end];
      if (quote) {
        if (char === '\\') end++;
        else if (char === quote) quote = null;
      } else if (char === '"' || char === "'") quote = char;
      else if (char === opening) depth++;
      else if (char === closing) depth--;
    }
    assert.equal(depth, 0, 'Fixture adapter encountered an unbalanced Razor expression');
    if (includeBrace) {
      const brace = /^\s*\{/.exec(source.slice(end));
      assert.ok(brace, 'Conditional layout markup must start a block');
      end += brace[0].length;
    }
    source = source.slice(0, start) + replacement + source.slice(end);
  }
  return source;
}

async function layoutFixture(role) {
  const raw = await readFile(new URL(`Views/Shared/${layouts[role].file}`, webRoot), 'utf8');
  const accountMenu = await accountMenuFixture(role);
  let html = raw.slice(raw.indexOf('<!DOCTYPE html>'))
    .replace(/@\*[\s\S]*?\*@/g, '')
    .replace(/@await Component\.InvokeAsync\("AccountMenu"\)/g, accountMenu)
    .replace(/@await RenderSectionAsync\([^\n]*\)/g, '')
    .replace(/@RenderBody\(\)/g, representativeContent(role))
    .replace(/@Url\.Content\("~\/([^"\n]*)"\)/g, '/$1')
    .replace(/@ViewData\["PageClass"\]/g, 'fixture-page')
    .replace(/@DateTime\.UtcNow\.Year/g, '2026');
  html = stripBalanced(html, /@\{/);
  html = stripBalanced(html, /@if\s*\(/, '', true);
  html = stripBalanced(html, /@\(/);
  html = html.replace(/^\s*}\s*$/gm, '')
    .replace(/@(currentTitle|title|successMsg|errorMsg|successNotice|errorNotice)\b/g, 'Thông báo kiểm thử')
    .replace(/@ViewData\["Title"\]/g, 'Kiểm thử giao diện')
    .replace(/\sasp-[\w-]+="[^"]*"/g, '')
    .replace(/~\//g, '/')
    .replace(/<a\b([^>]*)>/g, (tag, attributes) => /\bhref\s*=/.test(attributes) ? tag : `<a${attributes} href="#fixture-target">`);
  assert.doesNotMatch(html, /@(?:await|if|RenderBody|ViewData|\{|\()/, 'Update the adapter when the Razor layout changes');
  return html;
}

let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });

async function fixture(t, role, width = 390, height = 844) {
  const context = await browser.newContext({ viewport: { width, height }, reducedMotion: 'reduce' });
  t.after(() => context.close());
  const page = await context.newPage();
  page.setDefaultTimeout(5000);
  page.setDefaultNavigationTimeout(10000);
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  t.after(() => assert.deepEqual(errors, [], 'Production site.js must run without browser errors'));
  const html = await layoutFixture(role);
  await page.route('https://role-layout.test/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/') return route.fulfill({ contentType: 'text/html', body: html });
    if (/^\/(?:css\/[\w-]+\.css|js\/site\.js|images\/icons\.svg)$/.test(path)) {
      const body = await readFile(new URL(`wwwroot${path}`, webRoot));
      return route.fulfill({ contentType: path.endsWith('.css') ? 'text/css' : path.endsWith('.js') ? 'application/javascript' : 'image/svg+xml', body });
    }
    return route.abort();
  });
  await page.goto('https://role-layout.test/');
  return page;
}

async function assertClosedKeyboardAccess(page, selector, toggle) {
  await toggle.focus();
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(selector).evaluate(el => el.contains(document.activeElement)), false, 'Closed navigation must be skipped by Tab');
  }
  await page.locator(`${selector} a`).first().evaluate(el => el.focus());
  assert.equal(await page.locator(selector).evaluate(el => el.contains(document.activeElement)), false, 'Hidden navigation cannot receive focus');
  await toggle.focus();
}

async function assertDrawerClosed(page, selector) {
  await page.waitForFunction(selector => {
    const sidebar = document.querySelector(selector);
    return sidebar.inert && !sidebar.classList.contains('is-open') && getComputedStyle(sidebar).visibility === 'hidden';
  }, selector);
  assert.equal(await page.locator('[data-sidebar-toggle]').getAttribute('aria-expanded'), 'false');
  assert.equal(await page.locator(selector).getAttribute('aria-hidden'), 'true');
  assert.equal(await page.locator(selector).getAttribute('aria-modal'), null);
  assert.equal(await page.locator('[data-sidebar-backdrop]').isVisible(), false);
  assert.equal(await page.locator('body').evaluate(el => el.classList.contains('sidebar-open')), false);
}

for (const role of ['public', 'student']) {
  test(`${role} navigation opens its aria-controls target and closes on Escape, link and resize`, async t => {
    const page = await fixture(t, role);
    const toggle = page.locator('[data-nav-toggle]');
    const nav = page.locator(layouts[role].navigation);
    assert.equal(await toggle.getAttribute('aria-controls'), layouts[role].navigation.slice(1));
    assert.equal(await nav.isVisible(), false);
    await assertClosedKeyboardAccess(page, layouts[role].navigation, toggle);
    const closedLabel = await toggle.getAttribute('aria-label');
    await toggle.click();
    assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
    assert.equal(await nav.isVisible(), true);
    assert.notEqual(await toggle.getAttribute('aria-label'), closedLabel);
    await page.keyboard.press('Escape');
    assert.equal(await nav.isVisible(), false);
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
    assert.equal(await toggle.getAttribute('aria-label'), closedLabel);
    assert.ok(await toggle.evaluate(el => el === document.activeElement));
    await toggle.click();
    await nav.locator('a').first().click();
    assert.equal(await nav.isVisible(), false);
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
    await toggle.click();
    await page.locator('main textarea').click();
    assert.equal(await nav.isVisible(), false);
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
    await toggle.click();
    await page.setViewportSize({ width: 901, height: 844 });
    await page.waitForFunction(() => document.querySelector('[data-nav-toggle]').getAttribute('aria-expanded') === 'false');
    assert.equal(await toggle.isVisible(), false);
    assert.equal(await nav.isVisible(), true);
    assert.ok(await nav.locator('a').first().evaluate(el => el === document.activeElement), 'Desktop resize moves focus away from the hidden toggle');
    await page.setViewportSize({ width: 900, height: 844 });
    // CSS can hide the navigation before the matchMedia change listener restores focus.
    await page.waitForFunction(selector =>
      getComputedStyle(document.querySelector(selector)).display === 'none'
      && document.activeElement === document.querySelector('[data-nav-toggle]'),
    layouts[role].navigation, { timeout: 5000 });
    assert.equal(await toggle.isVisible(), true);
    assert.ok(await toggle.evaluate(el => el === document.activeElement), 'Mobile resize moves focus out of the hidden navigation');
  });
}

for (const role of ['teacher', 'admin']) {
  test(`${role} drawer traps focus, restores focus and closes through every control`, async t => {
    const page = await fixture(t, role);
    const selector = layouts[role].navigation;
    const sidebar = page.locator(selector);
    const toggle = page.locator('[data-sidebar-toggle]');
    const backdrop = page.locator('[data-sidebar-backdrop]');
    assert.equal(await toggle.getAttribute('aria-controls'), selector.slice(1));
    assert.equal(await backdrop.getAttribute('aria-controls'), selector.slice(1));
    await assertDrawerClosed(page, selector);
    await assertClosedKeyboardAccess(page, selector, toggle);
    await toggle.click();
    assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
    assert.equal(await sidebar.evaluate(el => el.inert), false);
    assert.equal(await sidebar.getAttribute('role'), 'dialog');
    assert.equal(await sidebar.getAttribute('aria-modal'), 'true');
    assert.equal(await sidebar.getAttribute('aria-hidden'), null);
    assert.equal(await backdrop.isVisible(), true);
    assert.equal(await page.locator('body').evaluate(el => el.classList.contains('sidebar-open')), true);
    assert.equal(await page.locator('body').evaluate(el => getComputedStyle(el).overflowY), 'hidden');
    assert.ok(await sidebar.evaluate(el => el.contains(document.activeElement)), 'Opening the drawer moves focus inside');
    const focusable = sidebar.locator('a[href], button:not([disabled]), summary, input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]').filter({ visible: true });
    await focusable.last().focus();
    await page.keyboard.press('Tab');
    assert.ok(await focusable.first().evaluate(el => el === document.activeElement), 'Tab wraps to the first drawer control');
    await page.keyboard.press('Shift+Tab');
    assert.ok(await focusable.last().evaluate(el => el === document.activeElement), 'Shift+Tab wraps to the last drawer control');
    if (role === 'admin') {
      const accountMenu = sidebar.locator('[data-account-menu]');
      const summary = accountMenu.locator('summary');
      await summary.click();
      assert.ok(await accountMenu.evaluate(el => el.open));
      await page.keyboard.press('Escape');
      assert.equal(await accountMenu.evaluate(el => el.open), false);
      assert.equal(await toggle.getAttribute('aria-expanded'), 'true', 'Escape closes the nested account menu before its drawer');
      assert.ok(await summary.evaluate(el => el === document.activeElement));
    }
    await page.keyboard.press('Escape');
    await assertDrawerClosed(page, selector);
    assert.ok(await toggle.evaluate(el => el === document.activeElement));

    await toggle.click();
    await sidebar.locator('[data-sidebar-close]').click();
    await assertDrawerClosed(page, selector);
    assert.ok(await toggle.evaluate(el => el === document.activeElement));
    await toggle.click();
    await backdrop.click({ position: { x: 382, y: 200 } });
    await assertDrawerClosed(page, selector);
    assert.ok(await toggle.evaluate(el => el === document.activeElement));
    await toggle.click();
    await sidebar.locator('nav a').first().click();
    await assertDrawerClosed(page, selector);

    await toggle.click();
    await sidebar.locator('[data-sidebar-close]').focus();
    await page.setViewportSize({ width: 1025, height: 844 });
    await page.waitForFunction(selector => !document.querySelector(selector).inert && !document.body.classList.contains('sidebar-open'), selector);
    assert.equal(await sidebar.isVisible(), true);
    assert.equal(await toggle.isVisible(), false);
    assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
    assert.equal(await backdrop.isVisible(), false);
    assert.equal(await sidebar.getAttribute('aria-hidden'), null);
    assert.equal(await sidebar.getAttribute('aria-modal'), null);
    assert.ok(await sidebar.locator('a').first().evaluate(el => el === document.activeElement), 'Desktop resize moves focus away from the hidden drawer close control');
    await page.setViewportSize({ width: 1024, height: 844 });
    await assertDrawerClosed(page, selector);
    assert.equal(await toggle.isVisible(), true);
  });

  test(`${role} drawer controls and scrollable navigation remain reachable at 390x320`, async t => {
    const page = await fixture(t, role, 390, 320);
    const selector = layouts[role].navigation;
    const sidebar = page.locator(selector);
    const toggle = page.locator('[data-sidebar-toggle]');
    await assertDrawerClosed(page, selector);
    await assertClosedKeyboardAccess(page, selector, toggle);
    await toggle.click();

    const close = sidebar.locator('[data-sidebar-close]');
    const closeBounds = await close.boundingBox();
    assert.ok(closeBounds.y >= 0 && closeBounds.y + closeBounds.height <= 320, 'Close control stays inside the short viewport');
    const nav = sidebar.locator('nav');
    const lastLink = nav.locator('a').last();
    await lastLink.scrollIntoViewIfNeeded();
    const navBounds = await nav.boundingBox();
    const lastBounds = await lastLink.boundingBox();
    assert.ok(lastBounds.y >= navBounds.y - 1 && lastBounds.y + lastBounds.height <= navBounds.y + navBounds.height + 1, 'Last navigation link can scroll fully into view');
    assert.ok(lastBounds.y >= 0 && lastBounds.y + lastBounds.height <= 320, 'Last navigation link remains inside the viewport');
    assert.ok(await nav.evaluate(el => el.scrollHeight <= el.clientHeight + 1 || el.scrollTop > 0), 'Long navigation scrolls independently of the locked page');

    if (role === 'admin') {
      await sidebar.locator('.account-summary strong').evaluate(el => { el.textContent = 'Nguyễn Thị Học Viên '.repeat(10); });
      const summary = sidebar.locator('.admin-sidebar-footer summary');
      const summaryBounds = await summary.boundingBox();
      assert.ok(summaryBounds.y >= 0 && summaryBounds.y + summaryBounds.height <= 320, 'Footer account control stays reachable');
      await summary.click();
      const logout = sidebar.locator('.account-dropdown .logout-item');
      await logout.scrollIntoViewIfNeeded();
      const logoutBounds = await logout.boundingBox();
      assert.ok(logoutBounds.y >= 0 && logoutBounds.y + logoutBounds.height <= 320, 'Footer dropdown actions remain reachable');
    }
    await close.click();
    await assertDrawerClosed(page, selector);
    assert.ok(await toggle.evaluate(el => el === document.activeElement));
  });
}

for (const role of Object.keys(layouts)) {
  test(`${role} responsive shell, cards, forms and tables fit 320–1440px`, async t => {
    const page = await fixture(t, role, widths[0]);
    for (const width of widths) {
      await page.setViewportSize({ width, height: 844 });
      // Let media-query listeners and layout settle without a fixed delay.
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      const dimensions = await page.evaluate(() => ({
        viewport: document.documentElement.clientWidth,
        root: document.documentElement.scrollWidth,
        body: document.body.scrollWidth,
        main: document.querySelector('main').getBoundingClientRect().toJSON(),
        controls: [...document.querySelectorAll('main input, main select, main textarea, main .course-card, main .admin-kpi-card')].map(el => ({ tag: el.tagName, ...el.getBoundingClientRect().toJSON() })),
      }));
      assert.ok(dimensions.root <= dimensions.viewport + 1, `${role} at ${width}px: document overflows (${dimensions.root}px)`);
      assert.ok(dimensions.body <= dimensions.viewport + 1, `${role} at ${width}px: body overflows (${dimensions.body}px)`);
      assert.ok(dimensions.main.x >= 0 && dimensions.main.right <= dimensions.viewport + 1, `${role} at ${width}px: main stays inside the viewport`);
      for (const control of dimensions.controls) {
        assert.ok(control.width > 0 && control.x >= dimensions.main.x - 1 && control.right <= dimensions.main.right + 1, `${role} at ${width}px: ${control.tag} fits main content`);
      }
      const mobile = width <= layouts[role].breakpoint;
      const toggle = page.locator(role === 'public' || role === 'student' ? '[data-nav-toggle]' : '[data-sidebar-toggle]');
      assert.equal(await toggle.isVisible(), mobile, `${role} at ${width}px: toggle matches its breakpoint`);
      assert.equal(await page.locator(layouts[role].navigation).isVisible(), !mobile, `${role} at ${width}px: navigation visibility matches its breakpoint`);
      if (!mobile && (role === 'teacher' || role === 'admin')) {
        const sidebar = await page.locator(layouts[role].navigation).boundingBox();
        assert.ok(dimensions.main.x >= sidebar.x + sidebar.width - 1, 'Desktop content clears the fixed sidebar');
      }
    }
    if (role === 'teacher' || role === 'admin') {
      await page.setViewportSize({ width: 320, height: 844 });
      const table = page.locator('[data-fixture-table]');
      assert.ok(await table.evaluate(el => el.scrollWidth > el.clientWidth), 'Wide tables scroll within their wrapper');
      assert.ok(await table.evaluate(el => { el.scrollLeft = 100; return el.scrollLeft > 0; }), 'Table columns remain reachable by horizontal scrolling');
      assert.equal(await page.evaluate(() => window.scrollX), 0);
    }
  });
}

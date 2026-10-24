import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

// Read-only UAT against a running local app. Credentials must stay outside tracked files.
const baseURL = new URL(process.env.ELP_UAT_URL ?? 'http://localhost:8080');
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(baseURL.hostname), 'Use a local UAT host.');
assert.ok(['http:', 'https:'].includes(baseURL.protocol), 'Use an HTTP(S) UAT URL.');
assert.ok(!baseURL.username && !baseURL.password && !baseURL.search && !baseURL.hash, 'Use a base URL without credentials or query parameters.');
const email = process.env.ELP_ADMIN_EMAIL;
const password = process.env.ELP_ADMIN_PASSWORD;
assert.ok(email && password, 'Set ELP_ADMIN_EMAIL and ELP_ADMIN_PASSWORD via environment.');

const startedAtUtc = new Date().toISOString();
const directory = `TestResults/admin-dashboard-${startedAtUtc.replace(/[:.]/g, '-')}`;
const output = path.join(fileURLToPath(new URL('../../', import.meta.url)), directory);
await mkdir(output, { recursive: true });
const redact = value => String(value).split(password).join('[REDACTED]').split(email).join('[REDACTED]');
const report = { baseURL: baseURL.origin, startedAtUtc, readOnly: true, passed: false, checks: [], screenshots: [] };
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ baseURL: baseURL.origin, viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  page.setDefaultNavigationTimeout(30000);
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(redact(error.message)));

  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await Promise.all([
    page.waitForURL(url => !url.pathname.startsWith('/Account/Login')),
    page.locator('form.account-form button[type="submit"]').click()
  ]);

  async function visit(route) {
    const response = await page.goto(route);
    assert.equal(response?.status(), 200, `${route}: HTTP response`);
    assert.equal(new URL(page.url()).pathname, route.split('?')[0], `${route}: destination`);
    await page.locator('body.admin-body').waitFor();
    await page.locator('.admin-page').waitFor();
    await page.evaluate(() => document.fonts.ready);
  }

  await visit('/Admin');
  assert.equal(await page.locator('.admin-summary > article').count(), 4);
  assert.equal(await page.locator('.admin-shortcuts').count(), 0);
  assert.equal(await page.locator('[data-month-revenue]').count(), 1);
  assert.equal(await page.locator('[data-metric="admins"]').count(), 1);
  const reportLink = page.locator('.admin-stat-revenue a');
  const destination = new URL(await reportLink.getAttribute('href'), baseURL);
  const displayed = (await page.locator('[data-month-revenue]').innerText()).replace(/\s/g, '');
  await visit(destination.pathname + destination.search);
  assert.equal((await page.locator('[data-revenue-currency="VND"] h2').innerText()).replace(/\s/g, ''), displayed);
  report.checks.push({ name: 'Four compact KPIs, Admin count and current-month revenue match report', passed: true });

  // Scope action links to their metric, because headings intentionally provide the same destinations.
  for (const [metric, route, filter, value] of [
    ['students', '/Admin/Users', 'role', 'Student'],
    ['teachers', '/Admin/Users', 'role', 'Teacher'],
    ['admins', '/Admin/Users', 'role', 'Admin'],
    ['courses', '/Admin/Courses', 'status', ''],
    ['draft-courses', '/Admin/Courses', 'status', 'Draft'],
    ['enrollments', '/Admin/Enrollments', 'completed', ''],
    ['completed-enrollments', '/Admin/Enrollments', 'completed', 'true']
  ]) {
    await visit('/Admin');
    const tile = page.locator('[data-dashboard-tile]').filter({ has: page.locator(`[data-metric="${metric}"]`) });
    assert.equal(await tile.count(), 1, `${metric}: unique metric tile`);
    const dashboardTotal = Number((await tile.locator(`[data-metric="${metric}"]`).innerText()).trim());
    assert.ok(Number.isSafeInteger(dashboardTotal) && dashboardTotal >= 0, `${metric}: integer count`);
    const action = tile.locator('.admin-metric-action a');
    assert.equal(await action.count(), 1, `${metric}: one action link`);
    const destination = new URL(await action.getAttribute('href'), baseURL);
    assert.equal(destination.pathname, route, `${metric}: action route`);
    assert.equal(destination.searchParams.get(filter) ?? '', value, `${metric}: link filter`);
    await Promise.all([
      page.waitForURL(url => url.pathname === route),
      action.click()
    ]);
    assert.equal(await page.locator(`#${filter}`).inputValue(), value, `${metric}: selected filter`);
    const listTotal = Number((await page.locator('.admin-count').innerText()).trim());
    assert.equal(listTotal, dashboardTotal, `${metric}: list total matches Dashboard`);
    report.checks.push({ name: `${metric} drilldown`, passed: true, total: dashboardTotal, route, filter: { [filter]: value } });
  }

  const expectedNavigation = ['/Admin', '/Admin/Transactions', '/Admin/Users', '/Admin/Courses', '/Admin/Enrollments', '/Admin/Revenue', '/Admin/Audit', '/Courses'];
  for (const width of [320, 390, 768, 1440]) {
    await page.setViewportSize({ width, height: width >= 1000 ? 1000 : 844 });
    for (const [route, name] of [['/Admin', 'dashboard'], ['/Admin/Courses', 'courses'], ['/Admin/Enrollments', 'enrollments']]) {
      await visit(route);
      const navigation = page.locator('#admin-sidebar .admin-nav-link');
      assert.equal(await navigation.count(), expectedNavigation.length, `${route}: sidebar destinations`);
      const destinations = await navigation.evaluateAll(links => links.map(link => new URL(link.href).pathname));
      assert.deepEqual(destinations, expectedNavigation, `${route}: sidebar destinations`);
      assert.equal(await page.locator('#admin-sidebar .admin-nav-link[aria-current="page"]').count(), 1, `${route}: one active sidebar item`);

      const layout = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
      assert.ok(layout.document <= layout.viewport + 1, `${route} at ${width}px: document width ${layout.document}`);
      if (width <= 1024) {
        const toggle = page.locator('[data-sidebar-toggle]');
        const sidebar = page.locator('#admin-sidebar');
        assert.equal(await toggle.getAttribute('aria-expanded'), 'false', 'Drawer starts closed');
        await toggle.click();
        await page.waitForFunction(() => document.querySelector('#admin-sidebar')?.classList.contains('is-open'));
        assert.equal(await sidebar.isVisible(), true, 'Drawer opens');
        assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
        assert.equal(await page.locator('[data-sidebar-backdrop]').isVisible(), true);
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.querySelector('#admin-sidebar')?.classList.contains('is-open'));
        assert.equal(await toggle.getAttribute('aria-expanded'), 'false', 'Escape closes drawer');
        assert.equal(await page.locator('[data-sidebar-backdrop]').isVisible(), false);
      }
      const screenshot = `${name}-${width}.png`;
      await page.screenshot({ path: path.join(output, screenshot), fullPage: true, animations: 'disabled' });
      report.screenshots.push(screenshot);
      report.checks.push({ name: `${name} responsive and navigation`, passed: true, width, ...layout });
    }
  }

  assert.deepEqual(pageErrors, [], 'No browser JavaScript errors');
  report.checks.push({ name: 'No browser JavaScript errors', passed: true });
  report.passed = true;
  console.log(`Admin Dashboard UAT passed: ${report.checks.length} checks. Evidence: ${directory}`);
} catch (error) {
  report.error = redact(error.message);
  console.error(report.error);
  process.exitCode = 1;
} finally {
  report.finishedAtUtc = new Date().toISOString();
  await writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  await browser?.close();
}

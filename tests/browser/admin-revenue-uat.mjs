import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

// Only login and GET report pages. Never create orders or simulate payment results.
const baseURL = new URL(process.env.ELP_UAT_URL ?? 'http://localhost:8080');
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(baseURL.hostname), 'Use a local UAT host.');
assert.ok(['http:', 'https:'].includes(baseURL.protocol), 'Use HTTP(S).');
assert.ok(!baseURL.username && !baseURL.password && !baseURL.search && !baseURL.hash, 'Use a base URL without credentials or query parameters.');
const email = process.env.ELP_ADMIN_EMAIL;
const password = process.env.ELP_ADMIN_PASSWORD;
assert.ok(email && password, 'Set ELP_ADMIN_EMAIL and ELP_ADMIN_PASSWORD via environment.');

const repository = fileURLToPath(new URL('../../', import.meta.url));
const startedAtUtc = new Date().toISOString();
const directory = `TestResults/admin-revenue-${startedAtUtc.replace(/[:.]/g, '-')}`;
const output = path.join(repository, directory);
await mkdir(output, { recursive: true });
const redact = value => String(value).split(password).join('[REDACTED]').split(email).join('[REDACTED]');
const report = { baseURL: baseURL.origin, startedAtUtc, readOnly: true, passed: false,
  fixtureSource: process.env.ELP_REVENUE_SAMPLE_DIR ? 'SQL integration-test rendered HTML; not live database orders' : null,
  checks: [], screenshots: [] };
const widths = [320, 390, 768, 1440];
const expectedNavigation = ['/Admin', '/Admin/Transactions', '/Admin/Users', '/Admin/Courses', '/Admin/Enrollments', '/Admin/Revenue', '/Admin/Audit', '/Courses'];
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ baseURL: baseURL.origin,
    viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  page.setDefaultNavigationTimeout(30000);
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(redact(error.message)));

  async function settle() {
    await page.locator('body.admin-body').waitFor();
    await page.locator('.admin-page').waitFor();
    await page.evaluate(() => document.fonts.ready);
  }
  async function visit(route) {
    const response = await page.goto(route);
    assert.equal(response?.status(), 200, `${route}: HTTP response`);
    assert.equal(new URL(page.url()).pathname, '/Admin/Revenue', 'Revenue destination');
    await settle();
  }
  async function capture(name, width, source) {
    const screenshot = `${name}-${width}.png`;
    await page.screenshot({ path: path.join(output, screenshot), fullPage: true, animations: 'disabled' });
    report.screenshots.push(screenshot);
    const layout = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
    assert.ok(layout.document <= layout.viewport + 1, `${name} at ${width}px: document width ${layout.document}`);
    report.checks.push({ name: `${name} responsive`, width, source, passed: true, ...layout });
  }

  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await Promise.all([
    page.waitForURL(url => !url.pathname.startsWith('/Account/Login')),
    page.locator('form.account-form button[type="submit"]').click()
  ]);
  await visit('/Admin/Revenue');
  assert.equal(await page.locator('#period').inputValue(), 'month', 'Initial report defaults to this month');
  const vietnamToday = await page.locator('#period').getAttribute('data-vietnam-today');
  const monthStart = vietnamToday.slice(0, 7) + '-01';
  assert.equal(await page.locator('#fromDate').inputValue(), monthStart);
  const navigation = page.locator('#admin-sidebar .admin-nav-link');
  assert.deepEqual(await navigation.evaluateAll(links => links.map(link => new URL(link.href).pathname)), expectedNavigation);
  const active = page.locator('#admin-sidebar .admin-nav-link[aria-current="page"]');
  assert.equal(await active.count(), 1, 'One active sidebar item');
  assert.equal(new URL(await active.getAttribute('href'), baseURL).pathname, '/Admin/Revenue');
  report.checks.push({ name: 'Live report HTTP 200 and eight sidebar destinations with Revenue active', passed: true });

  await page.locator('#fromDate').fill('2026-10-01');
  await page.locator('#toDate').fill('2026-10-31');
  assert.equal(await page.locator('#currency').count(), 0, 'VND is fixed; no currency selector');
  await page.locator('#groupBy').selectOption('month');
  await Promise.all([
    page.waitForURL(url => url.pathname === '/Admin/Revenue' && url.searchParams.get('groupBy') === 'month'),
    page.locator('form.admin-filters button[type="submit"]').click()
  ]);
  await settle();
  const filteredURL = new URL(page.url());
  for (const [name, value] of [['fromDate', '2026-10-01'], ['toDate', '2026-10-31'], ['groupBy', 'month']]) {
    assert.equal(filteredURL.searchParams.get(name), value, `${name}: URL filter`);
    assert.equal(await page.locator(`#${name}`).inputValue(), value, `${name}: selected filter`);
  }
  report.checks.push({ name: 'Live GET form keeps date and monthly grouping; VND only', passed: true });
  for (const width of widths) {
    await page.setViewportSize({ width, height: width >= 1000 ? 1000 : 844 });
    await visit(filteredURL.pathname + filteredURL.search);
    await capture('live-report', width, 'live-local-application');
  }

  await page.locator('#fromDate').fill('2026-10-24');
  await page.locator('#toDate').fill('2026-10-23');
  await Promise.all([
    page.waitForURL(url => url.searchParams.get('fromDate') === '2026-10-24'),
    page.locator('form.admin-filters button[type="submit"]').click()
  ]);
  await settle();
  assert.ok(await page.locator('.validation-summary-errors').isVisible(), 'Invalid range is explained');
  assert.equal(await page.locator('[data-revenue-currency]').count(), 0, 'Invalid range does not show revenue');
  report.checks.push({ name: 'Live invalid range shows validation and no revenue figures', passed: true });
  await page.screenshot({ path: path.join(output, 'live-invalid-range.png'), fullPage: true });
  report.screenshots.push('live-invalid-range.png');
  await Promise.all([
    page.waitForURL(url => url.pathname === '/Admin/Revenue' && !url.search),
    page.locator('form.admin-filters a').click()
  ]);
  await settle();
  assert.equal(await page.locator('#period').inputValue(), 'month');
  assert.equal(await page.locator('#fromDate').inputValue(), monthStart);
  assert.ok(await page.locator('#fromDate').inputValue());
  assert.ok(await page.locator('#toDate').inputValue());
  assert.equal(await page.locator('#groupBy').inputValue(), 'day');
  report.checks.push({ name: 'Live reset returns to this month by day', passed: true });

  if (process.env.ELP_REVENUE_SAMPLE_DIR) {
    const samples = path.resolve(repository, process.env.ELP_REVENUE_SAMPLE_DIR);
    for (const group of ['day', 'month']) {
      const html = await readFile(path.join(samples, `sample-revenue-${group}.html`), 'utf8');
      for (const width of widths) {
        // Stay on the local origin so relative CSS/JS resolve against the running application.
        await page.setViewportSize({ width, height: width >= 1000 ? 1000 : 844 });
        await visit('/Admin/Revenue');
        await page.setContent(html, { waitUntil: 'networkidle' });
        await settle();
        const totals = page.locator('[data-revenue-currency]');
        assert.equal(await totals.count(), 1, 'SQL fixture filtered to one currency');
        assert.equal(await totals.getAttribute('data-revenue-currency'), 'VND');
        assert.match(await totals.innerText(), /420\s*₫/u, 'SQL fixture revenue is 420 VND');
        assert.equal(await page.locator('#groupBy').inputValue(), group, 'SQL fixture grouping');
        assert.equal(await page.locator('.admin-revenue-charts figure').count(), 1, 'SQL fixture chart rendered');
        await capture(`sql-fixture-${group}`, width, 'SQL integration-test rendered HTML; not live database orders');
      }
    }
  }

  assert.deepEqual(pageErrors, [], 'No browser JavaScript errors');
  report.checks.push({ name: 'No browser JavaScript errors', passed: true });
  report.passed = true;
  console.log(`Admin Revenue UAT passed: ${report.checks.length} checks. Evidence: ${directory}`);
} catch (error) {
  report.error = redact(error.message);
  console.error(report.error);
  process.exitCode = 1;
} finally {
  report.finishedAtUtc = new Date().toISOString();
  await writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  await browser?.close();
}

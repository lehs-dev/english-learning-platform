import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const baseURL = process.env.ELP_UAT_URL ?? 'http://localhost:5108';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(baseURL).hostname), 'Use a local Development instance.');
const output = resolve(process.env.ELP_UAT_RESULTS ?? '../../artifacts/course-library');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const results = [], errors = [];
const desktop = { width: 1440, height: 1000 };
const mobile = { width: 390, height: 844 };

async function check(name, action) {
  try { await action(); results.push({ name, passed: true }); }
  catch (error) { results.push({ name, passed: false, error: String(error.message) }); }
}
async function screenshot(page, name) {
  await page.evaluate(() => document.fonts.ready);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await page.screenshot({ path: resolve(output, name + '.png'), animations: 'disabled' });
}
async function noOverflow(page) {
  const size = await page.evaluate(() => ({ page: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }));
  assert.ok(size.page <= size.viewport + 1, JSON.stringify(size));
}
async function filtersAboveResults(page) {
  const filter = await page.locator('.catalog-filters').boundingBox();
  const results = await page.locator('.catalog-results').boundingBox();
  assert.ok(filter && results && filter.y + filter.height <= results.y + 1);
}
async function login(page, role) {
  await page.goto('/Account/Login');
  await page.locator(`[data-demo-email="${role}1@demo.example.test"]`).click();
  await Promise.all([
    page.waitForURL(url => !url.pathname.startsWith('/Account/Login')),
    page.locator('form.account-form button[type="submit"]').click()
  ]);
}
async function mobileDrawer(page, sidebar) {
  await page.setViewportSize(mobile);
  const toggle = page.locator('[data-sidebar-toggle]');
  await toggle.click();
  await page.locator(sidebar + '[role="dialog"]').waitFor({ state: 'visible' });
  assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
  await page.keyboard.press('Escape');
  assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
  assert.equal(await toggle.evaluate(element => element === document.activeElement), true);
  await noOverflow(page);
}

try {
  for (const role of ['guest', 'student', 'teacher', 'admin']) {
    const context = await browser.newContext({ baseURL, viewport: desktop });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push({ role, message: error.message }));
    page.setDefaultTimeout(12000);
    if (role !== 'guest') await login(page, role);

    if (role === 'guest') {
      await check('Guest home fills viewport', async () => {
        await page.goto('/');
        const frame = await page.locator('.v2-canvas-frame').boundingBox();
        assert.equal(frame.x, 0);
        assert.equal(Math.round(frame.width), await page.evaluate(() => document.documentElement.clientWidth));
        assert.equal(await page.locator('body').evaluate(element => getComputedStyle(element).padding), '0px');
        await noOverflow(page);
        await screenshot(page, 'guest-home-desktop');
      });
    }

    await check(role + ' course library desktop', async () => {
      await page.goto(role === 'student' ? '/Learning' : '/Courses');
      if (role === 'student') {
        assert.equal(await page.locator('.student-sidebar .studio-nav-link span').count(), 7);
        const enrolled = await page.locator('[data-enrolled-courses] [data-course-id]').evaluateAll(items => items.map(item => item.dataset.courseId));
        const discovery = await page.locator('.catalog-results [data-course-id]').evaluateAll(items => items.map(item => item.dataset.courseId));
        assert.ok(enrolled.length > 0);
        assert.equal(discovery.some(id => enrolled.includes(id)), false);
        const enrolledBox = await page.locator('.enrolled-courses-section').boundingBox();
        const discoverBox = await page.locator('#discover-courses').boundingBox();
        assert.ok(enrolledBox.y < discoverBox.y);
        await screenshot(page, 'student-enrolled-desktop');
        await page.locator('#discover-courses').scrollIntoViewIfNeeded();
      } else if (role !== 'guest') {
        assert.equal(await page.locator(role === 'teacher' ? '.teacher-sidebar' : '.admin-sidebar').count(), 1);
        assert.equal(await page.locator('aside a.is-active[href="/Courses"][aria-current="page"]').count(), 1);
        assert.equal(await page.locator('.site-header').count(), 0);
      }
      if (role !== 'guest') {
        const sidebar = page.locator('aside.studio-sidebar, aside.admin-sidebar');
        assert.equal(Math.round((await sidebar.boundingBox()).y), 0);
      }
      await filtersAboveResults(page);
      await noOverflow(page);
      await screenshot(page, role + '-catalog-desktop');
    });

    await check(role + ' filters and empty result', async () => {
      const form = page.locator('form[aria-label="Bộ lọc khám phá khóa học"]');
      await form.locator('[name="Search"]').fill('no-course-matches-this-uat-query');
      await Promise.all([page.waitForURL(url => url.search.includes('no-course-matches')), form.getByRole('button', { name: 'Lọc khóa học' }).click()]);
      assert.equal(await page.locator('.catalog-results .course-card').count(), 0);
      assert.equal(await page.locator('.catalog-empty').isVisible(), true);
      if (role === 'student') assert.ok(await page.locator('[data-enrolled-courses] .course-card').count() > 0);
      await page.goto('/Courses');
      if (role === 'student') assert.equal(await page.locator('.student-sidebar').count(), 1);
    });

    await check(role + ' mobile catalog and navigation', async () => {
      await page.setViewportSize(mobile);
      await page.goto('/Courses');
      await page.locator('#discover-courses').scrollIntoViewIfNeeded();
      assert.equal(await page.locator('.catalog-filters summary').isVisible(), true);
      assert.equal(await page.locator('.catalog-filters').evaluate(element => element.open), false);
      await page.locator('.catalog-filters summary').click();
      assert.equal(await page.locator('.catalog-filters [name="Search"]').isVisible(), true);
      await filtersAboveResults(page);
      await noOverflow(page);
      await screenshot(page, role + '-catalog-mobile');
      if (role !== 'guest') {
        const sidebar = role === 'student' ? '.student-sidebar' : role === 'teacher' ? '.teacher-sidebar' : '.admin-sidebar';
        await page.evaluate(() => window.scrollTo(0, 0));
        await mobileDrawer(page, sidebar);
        if (role === 'student') {
          await page.locator('[data-sidebar-toggle]').click();
          await screenshot(page, 'student-sidebar-mobile');
          await page.keyboard.press('Escape');
        }
        await page.setViewportSize({ width: 1025, height: 900 });
        assert.equal(await page.locator(sidebar).evaluate(element => element.inert), false);
      }
    });

    if (role === 'student') await check('Student dashboard uses labelled full-width shell', async () => {
      await page.setViewportSize(desktop);
      await page.goto('/Learning/Dashboard');
      assert.equal(await page.locator('body').evaluate(element => getComputedStyle(element).padding), '0px');
      await noOverflow(page);
      await screenshot(page, 'student-dashboard-desktop');
    });
    await context.close();
  }
} finally {
  await browser.close();
}
const report = { results, javascriptErrors: errors };
await writeFile(resolve(output, 'results.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
assert.equal(results.filter(result => !result.passed).length, 0, 'See results.json for failed UI checks.');
assert.equal(errors.length, 0, 'Unexpected browser JavaScript errors.');

import assert from 'node:assert/strict';
import { readFile, mkdir } from 'node:fs/promises';
import { test } from 'node:test';
import { chromium } from 'playwright';

// Focused layout check of the actual date fields; integration tests cover Razor,
// authorization, SQL predicates and pagination on the complete pages.
test('Admin date fields fit desktop and mobile and stack on small screens', async () => {
  const root = new URL('../../src/EnglishLearningPlatform.Web/', import.meta.url);
  const styles = await Promise.all(['site', 'courses', 'admin', 'role-layouts'].map(name =>
    readFile(new URL(`wwwroot/css/${name}.css`, root), 'utf8')));
  const output = new URL('../../TestResults/admin-date-filters/', import.meta.url);
  await mkdir(output, { recursive: true });
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    for (const action of ['Users', 'Transactions', 'Audit']) {
      const view = await readFile(new URL(`Views/Admin/${action}.cshtml`, root), 'utf8');
      const fields = view.match(/<div class="admin-date-filter">[\s\S]*?<\/div>/g);
      assert.equal(fields?.length, 2);
      const html = fields.join('').replace(/@ViewData\["(?:FromDate|ToDate)"\]/g, '2026-10-23');
      for (const width of [320, 390, 768, 1440]) {
        await page.setViewportSize({ width, height: 900 });
        await page.setContent(`<html lang="vi"><meta charset="utf-8"><style>${styles.join('\n')}</style>
          <div class="admin-page"><section class="admin-surface"><form class="admin-filters">
          <div class="admin-search"><label for="search">Tìm kiếm</label><input id="search"></div>
          <div><label for="status">Trạng thái</label><select id="status"><option>Tất cả trạng thái</option></select></div>
          ${html}<div class="admin-filter-actions"><button class="button">Tìm kiếm</button><a>Đặt lại</a></div>
          </form></section></div></html>`);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${action}: overflow at ${width}`);
        const from = await page.locator('#fromDate').boundingBox();
        const to = await page.locator('#toDate').boundingBox();
        assert.ok(from.width > 100 && to.width > 100);
        if (width <= 390) assert.ok(to.y >= from.y + from.height, 'Dates should stack');
        if (width === 390) await page.screenshot({ path: new URL(`${action}-390.png`, output).pathname.replace(/^\/(\w:)/, '$1') });
      }
    }
  } finally {
    await browser.close();
  }
});

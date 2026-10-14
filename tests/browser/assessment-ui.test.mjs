import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../src/EnglishLearningPlatform.Web/wwwroot/js/assessment.js', import.meta.url), 'utf8');

// Exercise the production script in a real browser, without requiring demo accounts or a database.
async function fixture(t, milliseconds = 60000) {
  const browser = await chromium.launch({ headless: true });
  t.after(() => browser.close());
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.route('https://assessment.test/**', route => route.fulfill({ contentType: 'text/html', body: '<html></html>' }));
  await page.goto('https://assessment.test/');
  const now = Date.now();
  await page.setContent(`
    <div data-assessment-clock data-deadline="${new Date(now + milliseconds).toISOString()}" data-server-now="${new Date(now).toISOString()}"><span data-time-remaining></span></div>
    <form action="/submit" data-assessment-form data-save-url="/save">
      <p data-answer-progress></p><a data-question-link="1"></a><a data-question-link="2"></a>
      <fieldset data-question><input type="radio" name="a" value="correct"><input type="radio" name="a" value="" checked></fieldset>
      <fieldset data-question><input type="radio" name="b" value="correct"><input type="radio" name="b" value="" checked></fieldset>
      <p data-save-status></p>
      <button type="submit" formaction="/save" data-save-button>Save</button>
      <button type="submit" data-submit-button>Submit</button>
      <dialog data-submit-dialog><p data-submit-summary></p><button type="button" data-cancel-submit>Cancel</button><button type="button" data-confirm-submit>Confirm</button></dialog>
    </form>`);
  await page.evaluate(() => {
    window.submissions = [];
    window.saves = [];
    window.fetch = async (_url, options) => {
      window.saves.push([...options.body]);
      return { ok: true, redirected: false };
    };
    HTMLFormElement.prototype.submit = function () { window.submissions.push({ action: this.action, answers: [...new FormData(this)] }); };
  });
  await page.addScriptTag({ content: script });
  return page;
}

test('Manual submit warns about blanks; cancel preserves attempt; confirm submits once', async t => {
  const page = await fixture(t);
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => window.saves.length === 1);
  assert.equal(await page.locator('[data-answer-progress]').textContent(), '1/2 câu đã trả lời');
  assert.ok(await page.locator('[data-question-link="1"]').evaluate(el => el.classList.contains('is-answered')));
  await page.locator('[data-submit-button]').click();
  assert.ok(await page.locator('dialog').evaluate(el => el.open));
  assert.match(await page.locator('[data-submit-summary]').textContent(), /1 câu chưa trả lời/);
  await page.locator('[data-cancel-submit]').click();
  assert.equal(await page.evaluate(() => window.submissions.length), 0);
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/submit$/);
});

test('Manual save does not ask for submission confirmation', async t => {
  const page = await fixture(t);
  await page.locator('[data-save-button]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/save$/);
});

test('Deadline closes an open confirmation and auto-submits without user approval', async t => {
  const page = await fixture(t, 1800);
  await page.locator('[data-submit-button]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
  assert.equal(await page.locator('[data-time-remaining]').textContent(), '0:00');
});

test('A failed autosave can retry and submit the latest answer', async t => {
  const page = await fixture(t);
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.deepEqual(await page.evaluate(() => window.submissions[0].answers.find(([key]) => key === 'a')), ['a', 'correct']);
});

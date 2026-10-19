import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../src/EnglishLearningPlatform.Web/wwwroot/js/assessment.js', import.meta.url), 'utf8');

// Exercise the production script in a real browser, without requiring demo accounts or a database.
async function createPage(t) {
  const browser = await chromium.launch({ headless: true });
  t.after(() => browser.close());
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.route('https://assessment.test/**', route => route.fulfill({ contentType: 'text/html', body: '<html></html>' }));
  await page.goto('https://assessment.test/Assessment/Take/demo?attemptId=current-attempt');
  return page;
}

async function fixture(t, milliseconds = 60000) {
  const page = await createPage(t);
  const now = Date.now();
  await page.setContent(`
    <div data-assessment-clock data-deadline="${new Date(now + milliseconds).toISOString()}" data-server-now="${new Date(now).toISOString()}"><span data-time-remaining></span></div>
    <form method="post" action="/submit" data-assessment-form data-save-url="/save">
      <p data-answer-progress></p><a href="#question-1" data-question-link="1">1</a><a href="#question-2" data-question-link="2">2</a>
      <fieldset id="question-1" tabindex="-1" data-question><input type="radio" name="a" value="correct"><input type="radio" name="a" value="" checked><a href="#question-2">Câu tiếp</a></fieldset>
      <fieldset id="question-2" tabindex="-1" data-question><input type="radio" name="b" value="correct"><input type="radio" name="b" value="" checked><a href="#question-1">Câu trước</a></fieldset>
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
      return { ok: true, redirected: false, json: async () => ({ saved: true }) };
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

test('Returning after deadline closes confirmation, locks choices and auto-submits', async t => {
  const page = await fixture(t);
  await page.locator('input[name=a][value=correct]').check();
  await page.locator('[data-submit-button]').click();
  // Giả lập quay lại tab sau hạn; không chờ giờ thật để tránh test chập chờn.
  await page.evaluate(() => {
    const afterDeadline = performance.now() + 61000;
    performance.now = () => afterDeadline;
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
  assert.equal(await page.locator('[data-time-remaining]').textContent(), '0:00');
  assert.equal(await page.locator('[data-question]').evaluateAll(questions => questions.every(question => question.inert)), true);
  // Khóa tương tác không được làm biến mất lựa chọn trong payload.
  assert.deepEqual(await page.evaluate(() => window.submissions[0].answers), [['a', 'correct'], ['b', '']]);
});

test('Reopening an expired attempt posts its CSRF form once without starting a new attempt', async t => {
  const page = await createPage(t);
  await page.setContent(`
    <form method="post" action="/Assessment/Submit/expired-attempt" data-expired-attempt-form>
      <input type="hidden" name="__RequestVerificationToken" value="fixture-token">
      <button type="submit">Hoàn tất chấm điểm</button>
    </form>`);
  await page.evaluate(() => {
    window.submissions = [];
    HTMLFormElement.prototype.submit = function () {
      window.submissions.push({ action: this.action, method: this.method, fields: [...new FormData(this)] });
    };
  });
  await page.addScriptTag({ content: script });
  assert.deepEqual(await page.evaluate(() => window.submissions), [{
    action: 'https://assessment.test/Assessment/Submit/expired-attempt',
    method: 'post', fields: [['__RequestVerificationToken', 'fixture-token']]
  }]);
});

test('A failed autosave can retry and submit the latest answer', async t => {
  const page = await fixture(t);
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent !== 'Đang lưu đáp án…');
  assert.match(await page.locator('[data-save-status]').textContent(), /Chưa lưu/);
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.deepEqual(await page.evaluate(() => window.submissions[0].answers.find(([key]) => key === 'a')), ['a', 'correct']);
});

test('Queued autosaves stay ordered and only the latest acknowledgement marks answers saved', async t => {
  const page = await fixture(t);
  await page.evaluate(() => {
    window.pendingSaves = [];
    window.fetch = (_url, options) => new Promise(resolve => {
      window.saves.push([...options.body]);
      window.pendingSaves.push(resolve);
    });
  });
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => window.saves.length === 1);
  await page.locator('input[name=a][value=""]').check();
  assert.equal(await page.evaluate(() => window.saves.length), 1);

  // Phản hồi cũ không được báo đã lưu khi lần bỏ chọn mới vẫn đang chờ.
  await page.evaluate(() => window.pendingSaves[0]({ ok: true, redirected: false, json: async () => ({ saved: true }) }));
  await page.waitForFunction(() => window.saves.length === 2);
  assert.match(await page.locator('[data-save-status]').textContent(), /Đang lưu/);
  assert.deepEqual(await page.evaluate(() => window.saves.map(save => save.find(([key]) => key === 'a'))), [
    ['a', 'correct'], ['a', '']
  ]);
  assert.equal(await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  }), true);

  await page.evaluate(() => window.pendingSaves[1]({ ok: true, redirected: false, json: async () => ({ saved: true }) }));
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  assert.equal(await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  }), false);
});

test('Autosave requires server confirmation, preserves unsaved choices and can recover', async t => {
  const page = await fixture(t);
  // HTTP 200 chưa đủ: payload phải xác nhận saved = true.
  await page.evaluate(() => {
    window.fetch = async () => ({ ok: true, redirected: false, json: async () => ({ saved: false }) });
  });
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent !== 'Đang lưu đáp án…');
  assert.match(await page.locator('[data-save-status]').textContent(), /Chưa lưu/);
  assert.equal(await page.locator('input[name=a][value=correct]').isChecked(), true);

  await page.evaluate(() => {
    window.fetch = async (_url, options) => {
      window.saves.push([...options.body]);
      return { ok: true, redirected: false, json: async () => ({ saved: true }) };
    };
  });
  await page.locator('input[name=b][value=correct]').check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  assert.deepEqual(await page.evaluate(() => window.saves.at(-1)), [['a', 'correct'], ['b', 'correct']]);
});

test('Question navigation preserves choices, updates blank markers and does not post the form', async t => {
  const page = await fixture(t);
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  await page.getByRole('link', { name: 'Câu tiếp', exact: true }).click();
  assert.equal(new URL(page.url()).hash, '#question-2');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'question-2');
  await page.getByRole('link', { name: 'Câu trước', exact: true }).click();
  assert.equal(new URL(page.url()).hash, '#question-1');
  assert.equal(await page.locator('input[name=a][value=correct]').isChecked(), true);
  await page.locator('[data-question-link="2"]').click();
  assert.equal(new URL(page.url()).searchParams.get('attemptId'), 'current-attempt');
  assert.equal(await page.evaluate(() => window.saves.length), 1);
  assert.equal(await page.evaluate(() => window.submissions.length), 0);

  await page.locator('[data-question-link="1"]').click();
  await page.locator('input[name=a][value=""]').check();
  assert.equal(await page.locator('[data-answer-progress]').textContent(), '0/2 câu đã trả lời');
  assert.equal(await page.locator('[data-question-link="1"]').getAttribute('aria-label'), 'Đến câu 1, chưa trả lời');
  assert.equal(await page.locator('[data-question-link="1"]').evaluate(el => el.classList.contains('is-answered')), false);
});

test('Confirm locks the dialog while an autosave is pending, then posts the latest sheet only once', async t => {
  const page = await fixture(t);
  await page.evaluate(() => {
    window.fetch = (_url, options) => new Promise(resolve => {
      window.saves.push([...options.body]);
      window.finishSave = resolve;
    });
  });
  await page.locator('input[name=a][value=correct]').check();
  await page.waitForFunction(() => window.saves.length === 1);
  // Có cả một lần bỏ chọn và một câu mới chưa kịp autosave.
  await page.locator('input[name=a][value=""]').check();
  await page.locator('input[name=b][value=correct]').check();
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  assert.equal(await page.evaluate(() => window.submissions.length), 0);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), true);
  assert.equal(await page.locator('[data-confirm-submit]').isDisabled(), true);
  assert.equal(await page.locator('[data-cancel-submit]').isDisabled(), true);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('dialog').evaluate(el => el.open), true);
  await page.evaluate(() => document.querySelector('[data-confirm-submit]').click());

  await page.evaluate(() => window.finishSave({ ok: true, redirected: false, json: async () => ({ saved: true }) }));
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.evaluate(() => window.saves.length), 1);
  assert.deepEqual(await page.evaluate(() => window.submissions[0].answers), [['a', ''], ['b', 'correct']]);
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/submit$/);
  assert.equal(await page.locator('[data-save-status]').textContent(), 'Đang nộp bài…');
});

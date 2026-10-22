import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const script = await readFile(new URL('../../src/EnglishLearningPlatform.Web/wwwroot/js/assessment.js', import.meta.url), 'utf8');
const token = ['__RequestVerificationToken', 'fixture-token'];
const questionId = index => '10000000-0000-0000-0000-' + String(index + 1).padStart(12, '0');
const answerName = index => 'Answers[' + index + '].SelectedOptionId';
const questionName = index => 'Answers[' + index + '].AssessmentQuestionId';
const answer = (page, index, value = 'correct') => page.locator('input[name="' + answerName(index) + '"][value="' + value + '"]');
const partialFields = entries => [token, ...entries.flatMap(([index, value]) => [
  ['Answers.Index', String(index)], [questionName(index), questionId(index)], [answerName(index), value]
])];
const assertFields = (actual, expected) => assert.deepEqual(
  actual.map(field => JSON.stringify(field)).sort(), expected.map(field => JSON.stringify(field)).sort());

// Exercise the production script in a real browser, with the MVC names rendered by Take.cshtml.
async function createPage(t) {
  const browser = await chromium.launch({ headless: true });
  t.after(() => browser.close());
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await page.route('https://assessment.test/**', route => route.fulfill({ contentType: 'text/html', body: '<html></html>' }));
  await page.goto('https://assessment.test/Assessment/Take/demo?attemptId=current-attempt');
  return page;
}

async function fixture(t, questionCount = 2) {
  const page = await createPage(t);
  const now = Date.now();
  const questions = Array.from({ length: questionCount }, (_, index) =>
    '<fieldset id="question-' + (index + 1) + '" tabindex="-1" data-question>' +
      '<input type="hidden" name="' + questionName(index) + '" value="' + questionId(index) + '">' +
      '<input type="radio" name="' + answerName(index) + '" value="correct">' +
      '<input type="radio" name="' + answerName(index) + '" value="" checked>' +
      (index > 0 ? '<a href="#question-' + index + '">Câu trước</a>' : '') +
      (index < questionCount - 1 ? '<a href="#question-' + (index + 2) + '">Câu tiếp</a>' : '') +
    '</fieldset>').join('');
  const links = Array.from({ length: questionCount }, (_, index) =>
    '<a href="#question-' + (index + 1) + '" data-question-link="' + (index + 1) + '">' + (index + 1) + '</a>').join('');
  await page.setContent(
    '<div data-assessment-clock data-deadline="' + new Date(now + 60000).toISOString() +
      '" data-server-now="' + new Date(now).toISOString() + '"><span data-time-remaining></span></div>' +
    '<form method="post" action="/submit" data-assessment-form data-save-url="/save">' +
      '<input type="hidden" name="__RequestVerificationToken" value="fixture-token">' +
      '<p data-answer-progress></p>' + links + questions +
      '<p data-save-status></p>' +
      '<button type="submit" formaction="/save" data-save-button>Save</button>' +
      '<button type="submit" data-submit-button>Submit</button>' +
      '<dialog data-submit-dialog><p data-submit-summary></p><button type="button" data-cancel-submit>Cancel</button>' +
        '<button type="button" data-confirm-submit>Confirm</button></dialog>' +
    '</form>');
  await page.evaluate(() => {
    window.submissions = [];
    window.saves = [];
    window.fetch = async (_url, options) => {
      window.saves.push([...options.body]);
      return { ok: true, redirected: false, json: async () => ({ saved: true }) };
    };
    HTMLFormElement.prototype.submit = function () {
      window.submissions.push({ action: this.action, answers: [...new FormData(this)] });
    };
  });
  await page.addScriptTag({ content: script });
  return page;
}

test('Manual submit warns about blanks; cancel preserves attempt; confirm submits once', async t => {
  const page = await fixture(t);
  await answer(page, 0).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  assert.equal(await page.locator('[data-answer-progress]').textContent(), '1/2 câu đã trả lời');
  assert.ok(await page.locator('[data-question-link="1"]').evaluate(el => el.classList.contains('is-answered')));
  assertFields(await page.evaluate(() => window.saves[0]), partialFields([[0, 'correct']]));
  await page.locator('[data-submit-button]').click();
  assert.ok(await page.locator('dialog').evaluate(el => el.open));
  assert.match(await page.locator('[data-submit-summary]').textContent(), /1 câu chưa trả lời/);
  await page.locator('[data-cancel-submit]').click();
  assert.equal(await page.evaluate(() => window.submissions.length), 0);
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/submit$/);
  assertFields(await page.evaluate(() => window.submissions[0].answers), [token]);
});

test('Manual save retries a nonzero dirty question without submission confirmation', async t => {
  const page = await fixture(t);
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await answer(page, 1).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  await page.locator('[data-save-button]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/save$/);
  assertFields(await page.evaluate(() => window.submissions[0].answers), partialFields([[1, 'correct']]));
});

test('Returning after deadline closes confirmation, locks choices and posts unsaved answers', async t => {
  const page = await fixture(t);
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await answer(page, 1).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  await page.locator('[data-submit-button]').click();
  await page.evaluate(() => {
    const afterDeadline = performance.now() + 61000;
    performance.now = () => afterDeadline;
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await page.waitForFunction(() => window.submissions.length === 1);
  assert.equal(await page.locator('dialog').evaluate(el => el.open), false);
  assert.equal(await page.locator('[data-time-remaining]').textContent(), '0:00');
  assert.equal(await page.locator('[data-question]').evaluateAll(questions => questions.every(question => question.inert)), true);
  assertFields(await page.evaluate(() => window.submissions[0].answers), partialFields([[1, 'correct']]));
});

test('Reopening an expired attempt posts its CSRF form once without starting a new attempt', async t => {
  const page = await createPage(t);
  await page.setContent(
    '<form method="post" action="/Assessment/Submit/expired-attempt" data-expired-attempt-form>' +
      '<input type="hidden" name="__RequestVerificationToken" value="fixture-token">' +
      '<button type="submit">Hoàn tất chấm điểm</button>' +
    '</form>');
  await page.evaluate(() => {
    window.submissions = [];
    HTMLFormElement.prototype.submit = function () {
      window.submissions.push({ action: this.action, method: this.method, fields: [...new FormData(this)] });
    };
  });
  await page.addScriptTag({ content: script });
  assert.deepEqual(await page.evaluate(() => window.submissions), [{
    action: 'https://assessment.test/Assessment/Submit/expired-attempt',
    method: 'post', fields: [token]
  }]);
});

test('A failed autosave can submit the latest answer with its original nonzero index', async t => {
  const page = await fixture(t);
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await answer(page, 1).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assertFields(await page.evaluate(() => window.submissions[0].answers), partialFields([[1, 'correct']]));
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
  await answer(page, 1).check();
  await page.waitForFunction(() => window.saves.length === 1);
  await answer(page, 1, '').check();
  assert.equal(await page.evaluate(() => window.saves.length), 1);

  await page.evaluate(() => window.pendingSaves[0]({ ok: true, redirected: false, json: async () => ({ saved: true }) }));
  await page.waitForFunction(() => window.saves.length === 2);
  assert.match(await page.locator('[data-save-status]').textContent(), /Đang lưu/);
  const saves = await page.evaluate(() => window.saves);
  assertFields(saves[0], partialFields([[1, 'correct']]));
  assertFields(saves[1], partialFields([[1, '']]));
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

test('Saving another question preserves the failed-save warning until the failed question recovers', async t => {
  const page = await fixture(t);
  await page.evaluate(() => {
    window.fetch = async () => ({ ok: true, redirected: false, json: async () => ({ saved: false }) });
  });
  await answer(page, 0).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  assert.equal(await answer(page, 0).isChecked(), true);

  await page.evaluate(() => {
    window.fetch = async (_url, options) => {
      window.saves.push([...options.body]);
      return { ok: true, redirected: false, json: async () => ({ saved: true }) };
    };
  });
  await answer(page, 1).check();
  await page.waitForFunction(() => window.saves.length === 1);
  assertFields(await page.evaluate(() => window.saves[0]), partialFields([[1, 'correct']]));
  assert.match(await page.locator('[data-save-status]').textContent(), /Chưa lưu/);
  assert.equal(await page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  }), true);

  await answer(page, 0, '').check();
  await answer(page, 0).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  const saves = await page.evaluate(() => window.saves);
  assert.equal(saves.length, 3);
  assertFields(saves[1], partialFields([[0, '']]));
  assertFields(saves[2], partialFields([[0, 'correct']]));
});

test('Question navigation preserves choices, updates blank markers and does not post the form', async t => {
  const page = await fixture(t);
  await answer(page, 0).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  await page.getByRole('link', { name: 'Câu tiếp', exact: true }).click();
  assert.equal(new URL(page.url()).hash, '#question-2');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'question-2');
  await page.getByRole('link', { name: 'Câu trước', exact: true }).click();
  assert.equal(new URL(page.url()).hash, '#question-1');
  assert.equal(await answer(page, 0).isChecked(), true);
  await page.locator('[data-question-link="2"]').click();
  assert.equal(new URL(page.url()).searchParams.get('attemptId'), 'current-attempt');
  assert.equal(await page.evaluate(() => window.saves.length), 1);
  assert.equal(await page.evaluate(() => window.submissions.length), 0);

  await page.locator('[data-question-link="1"]').click();
  await answer(page, 0, '').check();
  assert.equal(await page.locator('[data-answer-progress]').textContent(), '0/2 câu đã trả lời');
  assert.equal(await page.locator('[data-question-link="1"]').getAttribute('aria-label'), 'Đến câu 1, chưa trả lời');
  assert.equal(await page.locator('[data-question-link="1"]').evaluate(el => el.classList.contains('is-answered')), false);
});

test('Confirm locks the dialog while autosave is pending, then posts the latest dirty questions once', async t => {
  const page = await fixture(t);
  await page.evaluate(() => {
    window.fetch = (_url, options) => new Promise(resolve => {
      window.saves.push([...options.body]);
      window.finishSave = resolve;
    });
  });
  await answer(page, 0).check();
  await page.waitForFunction(() => window.saves.length === 1);
  await answer(page, 0, '').check();
  await answer(page, 1).check();
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
  assertFields(await page.evaluate(() => window.submissions[0].answers), partialFields([[0, ''], [1, 'correct']]));
  assert.match(await page.evaluate(() => window.submissions[0].action), /\/submit$/);
  assert.equal(await page.locator('[data-save-status]').textContent(), 'Đang nộp bài…');
});

test('Nonzero autosave and a clean stale tab submit leave another tab answer untouched', async t => {
  const page = await fixture(t, 3);
  await page.evaluate(() => {
    window.serverAnswers = { '0': 'other-tab-choice' };
    window.fetch = async (_url, options) => {
      window.saves.push([...options.body]);
      for (const index of options.body.getAll('Answers.Index')) {
        window.serverAnswers[index] = options.body.get('Answers[' + index + '].SelectedOptionId');
      }
      return { ok: true, redirected: false, json: async () => ({ saved: true }) };
    };
  });
  await answer(page, 2).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  assertFields(await page.evaluate(() => window.saves[0]), partialFields([[2, 'correct']]));
  assert.deepEqual(await page.evaluate(() => window.serverAnswers), { '0': 'other-tab-choice', '2': 'correct' });
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assertFields(await page.evaluate(() => window.submissions[0].answers), [token]);
});

test('Sparse dirty submit includes explicit indexes and omits successfully saved questions', async t => {
  const page = await fixture(t, 3);
  await answer(page, 1).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent === 'Đã lưu đáp án.');
  await page.evaluate(() => { window.fetch = async () => { throw new Error('offline'); }; });
  await answer(page, 0).check();
  await answer(page, 2).check();
  await page.waitForFunction(() => document.querySelector('[data-save-status]').textContent.includes('Chưa lưu'));
  await page.locator('[data-submit-button]').click();
  await page.locator('[data-confirm-submit]').click();
  await page.waitForFunction(() => window.submissions.length === 1);
  assertFields(await page.evaluate(() => window.submissions[0].answers), partialFields([[0, 'correct'], [2, 'correct']]));
});

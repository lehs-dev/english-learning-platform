import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { chromium } from 'playwright';

// This writes demo content through the real UI. The caller must start a fresh,
// isolated app/database; never point this script at the user's working demo.
const password = process.env.LearningDemo__Password ?? '';
const redact = value => password ? String(value).split(password).join('[REDACTED]') : String(value);
const cases = [];
const javascriptErrors = [];
const contexts = [];
let browser;
let output;
let failure;
const startedAtUtc = new Date().toISOString();
const runId = `T2-${Date.now()}-${randomUUID().slice(0, 8)}`;

async function runCase(id, action) {
  const started = new Date().toISOString();
  try {
    await action();
    cases.push({ id, outcome: 'Passed', startedAtUtc: started, finishedAtUtc: new Date().toISOString() });
    console.log(`${id}: Passed`);
  } catch (error) {
    const safe = redact(error.message ?? error);
    cases.push({ id, outcome: 'Failed', startedAtUtc: started, finishedAtUtc: new Date().toISOString(), error: safe });
    throw new Error(safe);
  }
}

async function login(page, account) {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(`${account}@demo.example.test`);
  await page.locator('#Password').fill(password);
  await Promise.all([
    page.waitForURL(url => !url.pathname.includes('/Account/Login')),
    page.locator('form.account-form button[type="submit"]').click()
  ]);
}

// A real navigation is awaited even when POST redirects back to the same URL.
async function submit(page, button) {
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
    button.click()
  ]);
}

async function capture(page, name) {
  await page.screenshot({ path: join(output, `${name}.png`), fullPage: true, animations: 'disabled' });
}

async function responsive(page, name) {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
    // Wait for the real responsive sidebar transition before asserting/capturing the page.
    await page.waitForFunction(() => {
      const sidebar = document.getElementById('studio-sidebar');
      return !sidebar || innerWidth > 1024
        || (sidebar.getAttribute('aria-hidden') === 'true' && sidebar.getBoundingClientRect().right <= 1);
    });
    assert.ok(await page.locator('#main-content').isVisible(), `${name}: main content visible at ${width}px.`);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
      `${name}: no horizontal document overflow at ${width}px.`);
    await capture(page, `${name}-${width}`);
  }
  await page.setViewportSize({ width: 1440, height: 1000 });
}

const configuration = page => page.locator('form.editor-form').evaluate(form => {
  const fields = ['Title', 'Description', 'AssessmentType', 'TargetSkill', 'DurationMinutes', 'MaxAttempts', 'PassingScore', 'ExpectedUpdatedAtUtc'];
  return Object.fromEntries(fields.map(name => [name, form.querySelector(`[name="Input.${name}"]`).value]));
});

async function saveConfiguration(page) {
  await submit(page, page.getByRole('button', { name: 'Lưu cấu hình', exact: true }));
  assert.equal(await page.locator('.validation-summary-errors').count(), 0, 'Valid configuration must save.');
}

async function invalidConfiguration(page, id, label, change) {
  await page.goto(`/TeacherAssessments/Edit/${id}`);
  const before = await configuration(page);
  await page.locator('#Input_Title').fill(`${before.Title} rejected`);
  await change();
  // Bypass only browser HTML constraints so this tests SERVER validation.
  await page.locator('form.editor-form').evaluate(form => { form.noValidate = true; });
  await submit(page, page.getByRole('button', { name: 'Lưu cấu hình', exact: true }));
  assert.ok(await page.locator('.validation-summary-errors').isVisible(), `${label}: clear server validation required.`);
  await capture(page, `invalid-${label}`);
  await page.goto(`/TeacherAssessments/Edit/${id}`);
  assert.deepEqual(await configuration(page), before, `${label}: all saved fields and version remain unchanged.`);
}

async function createQuestion(page, index, skill) {
  await page.goto('/TeacherQuestionBank/Create');
  await page.locator('#Level').selectOption('A1');
  await page.locator('#Content').fill(`${runId} question ${index}`);
  await page.locator('#PrimarySkill').selectOption(String(skill));
  const options = page.locator('[data-option-row] input[name$=".Content"]');
  assert.ok(await options.count() >= 2);
  for (let i = 0; i < await options.count(); i++) {
    await options.nth(i).fill(`${runId} ${i === 0 ? 'correct' : 'wrong'} ${index}-${i}`);
  }
  await page.locator('input[name="CorrectOptionIndex"][value="0"]').check();
  await submit(page, page.locator('form.question-form button[type="submit"]'));
  assert.equal(new URL(page.url()).pathname.toLowerCase(), '/teacherquestionbank');
}

async function selectedIds(page) {
  return page.locator('[data-selected-row] [data-question-id]').evaluateAll(inputs => inputs.map(input => input.value));
}

async function checkPreview(page, id, type, status, ids, contents) {
  await page.goto(`/TeacherAssessmentPreview/Index/${id}`);
  const details = await page.locator('.assessment-details > div').evaluateAll(groups =>
    Object.fromEntries(groups.map(group => [group.querySelector('dt').textContent.trim(), group.querySelector('dd').textContent.trim()])));
  assert.equal(details['Loại bài'], type);
  assert.equal(details['Trạng thái'], status);
  assert.equal(details['Kỹ năng mục tiêu'], type === 'SkillAssessment' ? 'Grammar' : 'Không áp dụng');
  assert.equal(details['Thời lượng'], '20 phút');
  assert.equal(details['Lượt làm tối đa'], '2 lượt');
  assert.equal(details['Điểm đạt'], 'Không áp dụng');
  assert.equal(await page.locator('fieldset.exam-question-card').count(), 5);
  assert.deepEqual(await page.locator('.exam-question-content').allTextContents(), ids.map(questionId => contents.get(questionId)));
  assert.equal(await page.locator('[data-assessment-form], form[action*="/Assessment/Start"]').count(), 0);
  // The visible label/badge is the click target; the styled radio sits underneath it.
  await page.locator('label[for="preview-option-0-0"]').click();
  assert.equal(await page.locator('input[name="PreviewAnswers[0]"]:checked').count(), 1);
  await page.reload();
  assert.equal(await page.locator('input[name="PreviewAnswers[0]"]:checked').count(), 0, 'Preview selections are not saved.');
  await responsive(page, `${type}-preview-${status}`);
}

try {
  assert.equal(process.env.ELP_T2_UAT_ISOLATED, 'true', 'Set ELP_T2_UAT_ISOLATED=true only for a fresh isolated app/database.');
  assert.ok(password, 'Set the ephemeral LearningDemo__Password through environment.');
  assert.ok(process.env.ELP_UAT_URL, 'Set ELP_UAT_URL to the isolated local app.');
  const url = new URL(process.env.ELP_UAT_URL);
  assert.ok(['http:', 'https:'].includes(url.protocol) && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    && !url.username && !url.password, 'Only a loopback HTTP/HTTPS app without URL credentials is allowed.');
  assert.ok(process.env.ELP_UAT_RESULTS, 'Set ELP_UAT_RESULTS to the artifact directory.');
  output = resolve(process.env.ELP_UAT_RESULTS);
  await mkdir(output, { recursive: true });
  browser = await chromium.launch({ headless: true, ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
    ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {}) });
  const newPage = async () => {
    const context = await browser.newContext({ baseURL: url.origin, viewport: { width: 1440, height: 1000 } });
    contexts.push(context);
    await context.route('**/*', route => new URL(route.request().url()).origin === url.origin ? route.continue() : route.abort());
    const page = await context.newPage();
    page.setDefaultTimeout(20000);
    page.setDefaultNavigationTimeout(30000);
    page.on('pageerror', error => javascriptErrors.push(redact(error.message)));
    page.on('dialog', dialog => dialog.accept());
    return page;
  };
  const teacher = await newPage();
  const student = await newPage();
  const questionIds = [];
  const contents = new Map();
  let bank;

  await runCase('T2-login-and-seven-real-questions', async () => {
    assert.equal((await teacher.request.get('/health')).status(), 200, 'Isolated app must already be healthy.');
    await login(teacher, 'teacher1');
    await login(student, 'student1');
    for (let index = 1; index <= 7; index++) await createQuestion(teacher, index, index <= 5 ? 2 : 1);
  });

  for (const type of ['PlacementTest', 'SkillAssessment', 'PracticeExam']) {
    let id;
    let ids;
    await runCase(`${type}-create-edit-owned-draft`, async () => {
      await teacher.goto('/TeacherAssessments/Edit');
      await teacher.locator('#Input_Title').fill(`${runId} ${type}`);
      await teacher.locator('#Input_Description').fill(`Browser UAT ${type}`);
      await teacher.locator('#Input_AssessmentType').selectOption(type);
      await teacher.locator('#Input_TargetSkill').selectOption(type === 'SkillAssessment' ? 'Grammar' : '');
      await teacher.locator('#Input_DurationMinutes').fill('17');
      await teacher.locator('#Input_MaxAttempts').fill('3');
      await teacher.locator('#Input_PassingScore').fill('');
      await saveConfiguration(teacher);
      id = new URL(teacher.url()).pathname.split('/').pop();
      assert.match(id, /^[0-9a-f-]{36}$/i);
      await teacher.locator('#Input_Title').fill(`${runId} ${type} edited`);
      await teacher.locator('#Input_DurationMinutes').fill('20');
      await teacher.locator('#Input_MaxAttempts').fill('2');
      await saveConfiguration(teacher);
      await teacher.reload();
      assert.equal((await configuration(teacher)).AssessmentType, type);
      assert.equal((await configuration(teacher)).DurationMinutes, '20');
    });

    if (type === 'PlacementTest') {
      const invalid = [
        ['duration-zero', () => teacher.locator('#Input_DurationMinutes').fill('0')],
        ['attempts-zero', () => teacher.locator('#Input_MaxAttempts').fill('0')],
        ['score-negative', async () => { await teacher.locator('#Input_AssessmentType').selectOption('PracticeExam'); await teacher.locator('#Input_PassingScore').fill('-1'); }],
        ['score-over-100', async () => { await teacher.locator('#Input_AssessmentType').selectOption('PracticeExam'); await teacher.locator('#Input_PassingScore').fill('101'); }],
        ['placement-score', () => teacher.locator('#Input_PassingScore').fill('50')],
        ['skill-missing-target', () => teacher.locator('#Input_AssessmentType').selectOption('SkillAssessment')]
      ];
      for (const [label, change] of invalid) {
        await runCase(`metadata-server-reject-${label}`, () => invalidConfiguration(teacher, id, label, change));
      }
    } else {
      await runCase(`${type}-optional-score-and-inclusive-boundaries`, async () => {
        for (const score of ['0', '100', '']) {
          await teacher.locator('#Input_PassingScore').fill(score);
          await saveConfiguration(teacher);
          const storedScore = (await configuration(teacher)).PassingScore;
          if (score === '') assert.equal(storedScore, '');
          else assert.equal(Number(storedScore), Number(score));
        }
      });
    }

    await runCase(`${type}-select-save-and-reload-five-questions`, async () => {
      const href = await teacher.getByRole('link', { name: 'Soạn câu hỏi và points', exact: true }).getAttribute('href');
      await teacher.goto(href);
      await teacher.locator('#Search').fill(runId);
      await submit(teacher, teacher.getByRole('button', { name: 'Tìm / lọc', exact: true }));
      const matches = await teacher.locator('tr:has([data-add])').evaluateAll(rows => rows.map(row => ({
        id: row.querySelector('[data-add]').dataset.id,
        content: row.querySelector('[data-bank-content]').textContent,
        skill: row.querySelectorAll('td')[1].textContent.trim()
      })));
      assert.equal(matches.length, type === 'SkillAssessment' ? 5 : 7);
      if (!bank) {
        bank = matches;
        for (const question of bank) contents.set(question.id, question.content);
      }
      if (type === 'SkillAssessment') assert.ok(matches.every(question => question.skill === 'Grammar'));
      ids = (type === 'SkillAssessment' ? bank.filter(question => question.skill === 'Grammar')
        : [...bank.filter(question => question.skill === 'Grammar').slice(0, 3), ...bank.filter(question => question.skill === 'Vocabulary')])
        .map(question => question.id);
      assert.equal(ids.length, 5);
      for (const questionId of ids) await teacher.locator(`[data-add][data-id="${questionId}"]`).click();
      await submit(teacher, teacher.locator('[data-save]'));
      assert.deepEqual(await selectedIds(teacher), ids, 'Saved question identities and order survive reload.');
      questionIds.push(...ids);
      if (type === 'SkillAssessment') {
        // Tamper using the current browser session/CSRF: UI filtering alone is insufficient.
        const result = await teacher.evaluate(async foreignId => {
          const form = document.querySelector('#assessment-question-form');
          const items = [...form.querySelectorAll('[data-selected-row]')].map(row => ({
            questionId: row.querySelector('[data-question-id]').value,
            points: Number(row.querySelector('[data-points]').value)
          }));
          items[0].questionId = foreignId;
          const response = await fetch(form.action, {
            method: 'POST', headers: { 'Content-Type': 'application/json',
              RequestVerificationToken: form.querySelector('[name="__RequestVerificationToken"]').value },
            body: JSON.stringify({ expectedUpdatedAtUtc: form.querySelector('[data-version]').value || null, items })
          });
          return { status: response.status, body: await response.json() };
        }, bank.find(question => question.skill === 'Vocabulary').id);
        assert.equal(result.status, 400);
        assert.match(result.body.message, /TargetSkill/);
        await teacher.reload();
        assert.deepEqual(await selectedIds(teacher), ids, 'Wrong-skill request must not replace the saved paper.');
      }
    });

    await runCase(`${type}-draft-preview-publish-and-published-preview`, async () => {
      await checkPreview(teacher, id, type, 'Draft', ids, contents);
      await teacher.goto(`/TeacherAssessments/Edit/${id}`);
      await submit(teacher, teacher.getByRole('button', { name: 'Publish đề đã lưu', exact: true }));
      assert.equal(new URL(teacher.url()).pathname.toLowerCase(), '/teacherassessments');
      await checkPreview(teacher, id, type, 'Published', ids, contents);
      await invalidConfiguration(teacher, id, `${type}-published-duration-zero`, () => teacher.locator('#Input_DurationMinutes').fill('0'));
      if (type === 'SkillAssessment') {
        await invalidConfiguration(teacher, id, 'published-skill-mismatch', () => teacher.locator('#Input_TargetSkill').selectOption('Reading'));
      }
    });

    await runCase(`${type}-student-start-answer-submit-and-safe-result`, async () => {
      await student.goto(`/Assessment/Take/${id}`);
      await student.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }).click();
      await student.locator('[data-assessment-form]').waitFor();
      const questions = student.locator('[data-question]');
      assert.equal(await questions.count(), 5);
      for (let index = 0; index < 5; index++) {
        const [saved] = await Promise.all([
          student.waitForResponse(response => response.url().includes('/Assessment/Save/') && response.request().method() === 'POST'),
          questions.nth(index).locator('.exam-option-card').first().click()
        ]);
        assert.equal(saved.status(), 200);
        assert.equal((await saved.json()).saved, true);
      }
      await student.locator('[data-submit-button]').click();
      await student.locator('[data-submit-dialog][open]').waitFor();
      await Promise.all([
        student.waitForURL(/\/Assessment\/Result\//),
        student.locator('[data-confirm-submit]').click()
      ]);
      assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), '100/100');
      assert.equal(await student.locator('.assessment-passed, .assessment-failed').count(), 0, 'Optional score has no pass/fail badge.');
      const text = await student.locator('#main-content').innerText();
      assert.doesNotMatch(text, /CEFR|TOEIC|Beginner|Intermediate|Advanced|\b(?:A1|A2|B1|B2|C1|C2)\b/i);
      assert.doesNotMatch(text, new RegExp(`${runId} (?:correct|wrong)`), 'No individual answers leak into the result.');
      await responsive(student, `${type}-result`);
    });
  }
  assert.equal(new Set(questionIds).size, 7, 'All seven UI-created questions were exercised.');
  assert.deepEqual(javascriptErrors, [], 'No browser JavaScript errors.');
} catch (error) {
  failure = redact(error.message ?? error);
  console.error(`Assessment types UAT failed: ${failure}`);
  process.exitCode = 1;
} finally {
  if (output) {
    const report = { kind: 'Live Chromium UI against isolated ASP.NET app and SQL Server; not an HTML fixture',
      runId, startedAtUtc, finishedAtUtc: new Date().toISOString(), timezone: 'Asia/Bangkok',
      outcome: failure ? 'Failed' : 'Passed', cases, javascriptErrors, ...(failure ? { error: failure } : {}) };
    await writeFile(join(output, 'assessment-types-results.json'), redact(JSON.stringify(report, null, 2)) + '\n');
  }
  await Promise.all(contexts.map(context => context.close().catch(() => {})));
  await browser?.close();
}

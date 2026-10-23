import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { chromium } from 'playwright';

// Mutates data through the real UI. Run only against a fresh isolated app/SQL
// database, never against the developer's demo database or a shared environment.
const password = process.env.LearningDemo__Password ?? '';
const redact = value => password ? String(value).split(password).join('[REDACTED]') : String(value);
const runId = `T3-${Date.now()}-${randomUUID().slice(0, 8)}`;
const cases = [];
const javascriptErrors = [];
const contexts = [];
const startedAtUtc = new Date().toISOString();
let output;
let browser;
let failure;

async function runCase(id, action) {
  try {
    await action();
    cases.push({ id, outcome: 'Passed' });
    console.log(`${id}: Passed`);
  } catch (error) {
    const safe = redact(error.message ?? error);
    cases.push({ id, outcome: 'Failed', error: safe });
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

async function submit(page, button) {
  await Promise.all([page.waitForNavigation({ waitUntil: 'domcontentloaded' }), button.click()]);
}

async function captureResponsive(page, name) {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
    // Resizing starts the sidebar's closing transition; wait for its real final state.
    await page.waitForFunction(() => {
      const sidebar = document.getElementById('studio-sidebar');
      return !sidebar || innerWidth > 1024
        || (sidebar.getAttribute('aria-hidden') === 'true' && sidebar.getBoundingClientRect().right <= 1);
    });
    assert.ok(await page.locator('#main-content').isVisible());
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
      `${name}: no horizontal document overflow at ${width}px.`);
    await page.screenshot({ path: join(output, `${name}-${width}.png`), fullPage: true, animations: 'disabled' });
  }
  await page.setViewportSize({ width: 1440, height: 1000 });
}

const scopeUrl = id => `/TeacherAssessmentScope/Edit/${id}`;
const selectedCourse = page => page.locator('#Input_CourseId').inputValue();
const scopeVersion = page => page.locator('[name="Input.ExpectedUpdatedAtUtc"]').inputValue();
async function saveScope(page, courseId) {
  await page.locator('#Input_CourseId').selectOption(courseId);
  await submit(page, page.getByRole('button', { name: 'Lưu phạm vi', exact: true }));
  assert.equal(await page.locator('.validation-summary-errors').count(), 0, 'Valid scope must save.');
}

// A negative request keeps the real session and antiforgery token in the browser.
// Return only status/path: MVC cookie authorization may redirect to AccessDenied.
// Never export cookies, tokens, or complete response HTML.
async function postVisibleForm(page, selector) {
  return page.locator(selector).evaluate(async form => {
    const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
    return { status: response.status, path: new URL(response.url).pathname };
  });
}

async function createQuestion(page, number) {
  await page.goto('/TeacherQuestionBank/Create');
  await page.locator('#Level').selectOption('A1');
  await page.locator('#Content').fill(`${runId} Grammar ${number}`);
  await page.locator('#PrimarySkill').selectOption('2');
  const options = page.locator('[data-option-row] input[name$=".Content"]');
  assert.ok(await options.count() >= 2);
  for (let index = 0; index < await options.count(); index++) {
    await options.nth(index).fill(`${runId} answer ${number}-${index}`);
  }
  await page.locator('input[name="CorrectOptionIndex"][value="0"]').check();
  await submit(page, page.locator('form.question-form button[type="submit"]'));
  assert.equal(new URL(page.url()).pathname.toLowerCase(), '/teacherquestionbank');
}

async function assertScopeLabels(page, assessmentId, courseTitle) {
  await page.goto('/TeacherAssessments');
  const row = page.locator('tr').filter({ hasText: `${runId} PracticeExam` });
  assert.equal(await row.count(), 1);
  assert.match(await row.innerText(), courseTitle ? new RegExp(courseTitle) : /Độc lập \(Independent\)/);
  assert.equal(await row.getByRole('link', { name: 'Phạm vi / Course', exact: true }).count(), 1);
  await page.goto(`/TeacherAssessmentPreview/Index/${assessmentId}`);
  const scope = page.locator('.assessment-details > div').filter({ has: page.locator('dt', { hasText: 'Phạm vi' }) });
  assert.match(await scope.innerText(), courseTitle ? new RegExp(courseTitle) : /độc lập/i);
}

try {
  assert.equal(process.env.ELP_T3_UAT_ISOLATED, 'true', 'Set ELP_T3_UAT_ISOLATED=true only for an isolated app/database.');
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
  const otherTeacher = await newPage();
  const student = await newPage();
  const outsider = await newPage();
  let assessmentId;
  let foreignCourseId;
  let freeCourseId;
  let paidCourseId;
  let stale;

  await runCase('login-four-demo-accounts-and-create-foreign-course', async () => {
    assert.equal((await teacher.request.get('/health')).status(), 200);
    await login(teacher, 'teacher1');
    await login(otherTeacher, 'teacher2');
    await login(student, 'student1');
    await login(outsider, 'student2');
    await otherTeacher.goto('/TeacherCourses/Edit');
    await otherTeacher.locator('#Input_Title').fill(`${runId} foreign course`);
    await otherTeacher.locator('[name="Input.Skills"][value="Grammar"]').check();
    // Free-course price is intentionally read-only and initialized to zero by the UI.
    assert.equal(await otherTeacher.locator('#Input_Price').inputValue(), '0');
    await submit(otherTeacher, otherTeacher.getByRole('button', { name: 'Lưu khóa học', exact: true }));
    assert.match(new URL(otherTeacher.url()).pathname, /\/TeacherCourses\/Manage\/[0-9a-f-]{36}$/i);
    foreignCourseId = new URL(otherTeacher.url()).pathname.split('/').pop();
  });

  await runCase('create-five-questions-and-publish-independent-assessment', async () => {
    for (let number = 1; number <= 5; number++) await createQuestion(teacher, number);
    await teacher.goto('/TeacherAssessments/Edit');
    await teacher.locator('#Input_Title').fill(`${runId} PracticeExam`);
    await teacher.locator('#Input_AssessmentType').selectOption('PracticeExam');
    await teacher.locator('#Input_DurationMinutes').fill('20');
    await teacher.locator('#Input_MaxAttempts').fill('3');
    await submit(teacher, teacher.getByRole('button', { name: 'Lưu cấu hình', exact: true }));
    assessmentId = new URL(teacher.url()).pathname.split('/').pop();
    assert.match(assessmentId, /^[0-9a-f-]{36}$/i);
    await teacher.goto(await teacher.getByRole('link', { name: 'Soạn câu hỏi và points', exact: true }).getAttribute('href'));
    await teacher.locator('#Search').fill(runId);
    await submit(teacher, teacher.getByRole('button', { name: 'Tìm / lọc', exact: true }));
    const add = teacher.locator('[data-add]');
    assert.equal(await add.count(), 5);
    for (let index = 0; index < 5; index++) await add.nth(index).click();
    await submit(teacher, teacher.locator('[data-save]'));
    await teacher.goto(`/TeacherAssessments/Edit/${assessmentId}`);
    await submit(teacher, teacher.getByRole('button', { name: 'Publish đề đã lưu', exact: true }));
    assert.equal(new URL(teacher.url()).pathname.toLowerCase(), '/teacherassessments');
    await assertScopeLabels(teacher, assessmentId, null);
    await teacher.goto(scopeUrl(assessmentId));
    assert.equal(await selectedCourse(teacher), '');
    assert.equal(await teacher.locator('[data-scope-editor]').getAttribute('data-can-change'), 'true');
    assert.equal((await teacher.locator('[data-current-scope]').innerText()).trim(), 'Độc lập (Independent)');
    const options = await teacher.locator('#Input_CourseId option').evaluateAll(items => items.map(item => ({ value: item.value, text: item.textContent })));
    freeCourseId = options.find(item => item.text.includes('Demo · Everyday English'))?.value;
    paidCourseId = options.find(item => item.text.includes('Demo · Business English'))?.value;
    assert.ok(freeCourseId && paidCourseId, 'Both owned published demo courses must be selectable.');
    assert.ok(!options.some(item => item.value === foreignCourseId), 'A foreign course is never offered.');
    await captureResponsive(teacher, 'independent-scope');
  });

  await runCase('independent-to-free-course-revokes-outsider-even-on-old-start-form', async () => {
    assert.equal((await outsider.goto(`/Assessment/Take/${assessmentId}`)).status(), 200);
    assert.equal(await outsider.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }).count(), 1);
    await saveScope(teacher, freeCourseId);
    const deniedStart = await postVisibleForm(outsider, 'form[action*="/Assessment/Start/"]');
    assert.ok(deniedStart.status === 403 || deniedStart.status === 404
      || deniedStart.path === '/Account/AccessDenied',
      'A previously opened independent Start form cannot bypass the new course scope.');
    assert.equal((await outsider.goto(`/Assessment/Take/${assessmentId}`)).status(), 404);
    await outsider.goto('/Assessment');
    assert.equal(await outsider.locator(`a[href*="/Assessment/Take/${assessmentId}"]`).count(), 0);
    assert.equal((await student.goto(`/Assessment/Take/${assessmentId}`)).status(), 200, 'Enrolled student can view.');
    await assertScopeLabels(teacher, assessmentId, 'Demo · Everyday English');
  });

  await runCase('switch-owned-courses-and-detach-back-to-independent', async () => {
    await teacher.goto(scopeUrl(assessmentId));
    await saveScope(teacher, paidCourseId);
    await teacher.reload();
    assert.equal(await selectedCourse(teacher), paidCourseId);
    assert.equal((await student.goto(`/Assessment/Take/${assessmentId}`)).status(), 404,
      'A paid course without successful payment/enrollment does not grant access.');
    await assertScopeLabels(teacher, assessmentId, 'Demo · Business English');
    await teacher.goto(scopeUrl(assessmentId));
    await saveScope(teacher, '');
    await teacher.reload();
    assert.equal(await selectedCourse(teacher), '');
    assert.equal((await outsider.goto(`/Assessment/Take/${assessmentId}`)).status(), 200);
    await assertScopeLabels(teacher, assessmentId, null);
  });

  await runCase('two-tabs-cannot-overwrite-the-latest-scope', async () => {
    stale = await teacher.context().newPage();
    await stale.goto(scopeUrl(assessmentId));
    const beforeVersion = await scopeVersion(stale);
    await teacher.goto(scopeUrl(assessmentId));
    await saveScope(teacher, freeCourseId);
    await stale.locator('#Input_CourseId').selectOption(paidCourseId);
    await submit(stale, stale.getByRole('button', { name: 'Lưu phạm vi', exact: true }));
    assert.ok(await stale.locator('.validation-summary-errors').isVisible(), 'Stale submission shows a clear error.');
    assert.match(await stale.locator('.validation-summary-errors').innerText(), /tải lại/i);
    await teacher.goto(scopeUrl(assessmentId));
    assert.equal(await selectedCourse(teacher), freeCourseId);
    assert.notEqual(await scopeVersion(teacher), beforeVersion);
  });

  await runCase('foreign-teacher-and-forged-foreign-course-are-rejected', async () => {
    assert.equal((await otherTeacher.goto(scopeUrl(assessmentId))).status(), 404);
    const version = await scopeVersion(teacher);
    await teacher.locator('#Input_CourseId').evaluate((select, value) => {
      select.add(new Option('Foreign course test', value));
      select.value = value;
    }, foreignCourseId);
    await submit(teacher, teacher.getByRole('button', { name: 'Lưu phạm vi', exact: true }));
    assert.ok(await teacher.locator('.validation-summary-errors').isVisible());
    await teacher.goto(scopeUrl(assessmentId));
    assert.equal(await selectedCourse(teacher), freeCourseId);
    assert.equal(await scopeVersion(teacher), version, 'Invalid POST preserves saved scope and version.');
  });

  await runCase('existing-course-final-is-readonly', async () => {
    await teacher.goto('/TeacherAssessments');
    const row = teacher.locator('tr').filter({ hasText: 'Demo · Bài thi cuối khóa Everyday English' });
    await teacher.goto(await row.getByRole('link', { name: 'Phạm vi / Course', exact: true }).getAttribute('href'));
    assert.equal(await teacher.locator('#assessment-scope-form').count(), 0);
    assert.equal(await teacher.locator('[data-scope-editor]').getAttribute('data-is-final'), 'true');
    assert.match(await teacher.locator('[data-scope-lock]').innerText(), /Final/);
  });

  await runCase('first-attempt-locks-scope-and-rejects-an-already-open-form', async () => {
    await stale.goto(scopeUrl(assessmentId));
    await stale.locator('#Input_CourseId').selectOption(paidCourseId);
    assert.equal((await student.goto(`/Assessment/Take/${assessmentId}`)).status(), 200);
    await student.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }).click();
    await student.locator('[data-assessment-form]').waitFor();
    const attemptUrl = student.url();
    assert.equal(await student.locator('[data-question]').count(), 5);
    await teacher.goto(scopeUrl(assessmentId));
    assert.equal(await teacher.locator('#assessment-scope-form').count(), 0);
    assert.equal(await teacher.locator('[data-scope-editor]').getAttribute('data-has-attempt'), 'true');
    assert.match(await teacher.locator('[data-scope-lock]').innerText(), /Attempt|lượt làm/i);
    await captureResponsive(teacher, 'scope-locked-after-attempt');
    await submit(stale, stale.getByRole('button', { name: 'Lưu phạm vi', exact: true }));
    assert.equal(await stale.locator('#assessment-scope-form').count(), 0, 'Rejected stale POST cannot reopen editing.');
    await assertScopeLabels(teacher, assessmentId, 'Demo · Everyday English');
    await student.reload();
    assert.equal(student.url(), attemptUrl, 'Rejected scope change does not destroy the active attempt.');
    assert.equal(await student.locator('[data-question]').count(), 5);
  });
  assert.deepEqual(javascriptErrors, [], 'No browser JavaScript errors.');
} catch (error) {
  failure = redact(error.message ?? error);
  console.error(`Assessment scope UAT failed: ${failure}`);
  process.exitCode = 1;
} finally {
  if (output) {
    const report = { kind: 'Live Chromium UI against isolated ASP.NET app and SQL Server; not an HTML fixture',
      runId, startedAtUtc, finishedAtUtc: new Date().toISOString(), timezone: 'Asia/Bangkok',
      outcome: failure ? 'Failed' : 'Passed', cases, javascriptErrors, ...(failure ? { error: failure } : {}) };
    await writeFile(join(output, 'assessment-scope-results.json'), redact(JSON.stringify(report, null, 2)) + '\n');
  }
  await Promise.all(contexts.map(context => context.close().catch(() => {})));
  await browser?.close();
}

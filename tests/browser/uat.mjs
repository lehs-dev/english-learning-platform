import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { chromium } from 'playwright';

// Run against a fresh, dedicated demo database. This script never resets data.
const baseURL = process.env.ELP_UAT_URL ?? 'http://localhost:8081';
const password = process.env.LearningDemo__Password;
assert.ok(password, 'Set LearningDemo__Password through environment.');
// Playwright call logs may include values supplied to locator.fill on failure.
const redact = value => String(value).split(password).join('[REDACTED]');
const output = process.env.ELP_UAT_RESULTS ?? 'TestResults/browser-uat';
const gitState = () => ({
  candidateSha: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
  workingTreeClean: !execFileSync('git', ['status', '--porcelain'], { encoding: 'utf8' }).trim()
});
const source = process.env.ELP_UAT_CANDIDATE ? {
  candidateSha: process.env.ELP_UAT_CANDIDATE,
  workingTreeClean: process.env.ELP_UAT_CLEAN === 'true',
  sourceDigest: process.env.ELP_UAT_SOURCE_DIGEST,
  testedImageId: process.env.ELP_UAT_IMAGE_ID,
  provenance: 'Provided by caller after inspecting the built image and source'
} : gitState();
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const cases = [];
const errors = [];
const contexts = [];
const createPage = async () => {
  const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  contexts.push(context);
  // Demo text/scoring/payment are usable without third party media/network.
  await context.route('**/*', route => new URL(route.request().url()).origin === new URL(baseURL).origin
    ? route.continue() : route.abort());
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(redact(error.message)));
  page.setDefaultTimeout(15000);
  return page;
};
const screenshot = (page, name) => page.screenshot({ path: `${output}/${name}.png`, fullPage: true });
const login = async (page, actor) => {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(`${actor}@demo.example.test`);
  await page.locator('#Password').fill(password);
  await Promise.all([
    page.waitForURL(url => !url.pathname.includes('/Account/Login')),
    page.locator('form.account-form button[type=submit]').click()
  ]);
};
const runCase = async (id, actor, work) => {
  const startedAtUtc = new Date().toISOString();
  try {
    await work();
    cases.push({ id, actor, outcome: 'Passed', startedAtUtc, finishedAtUtc: new Date().toISOString() });
    console.log(`${id}: Passed (${actor})`);
  } catch (error) {
    const message = redact(error.message ?? error);
    cases.push({ id, actor, outcome: 'Failed', startedAtUtc, error: message });
    // Do not retain a cause containing Playwright's unredacted call log.
    throw new Error(message);
  }
};
let freeId, paidId, finalId, attemptId;
try {
  await runCase('UAT-01', 'Guest', async () => {
    const page = await createPage();
    let healthy = false;
    for (let retry = 0; retry < 45; retry++) {
      try {
        const response = await page.request.get('/health', { timeout: 2000 });
        if (response.status() === 200) { healthy = true; break; }
      } catch { /* The new web process may still be starting and seeding. */ }
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    assert.ok(healthy, 'Fresh web did not become healthy within the startup window.');
    assert.equal((await page.goto('/health')).status(), 200);
    await page.goto('/Courses');
    const freeCard = page.locator('article').filter({ hasText: 'Demo · Everyday English' });
    const paidCard = page.locator('article').filter({ hasText: 'Demo · Business English' });
    assert.equal(await freeCard.count(), 1);
    assert.equal(await paidCard.count(), 1);
    freeId = (await freeCard.locator('a[href*="/Courses/Details/"]').first().getAttribute('href')).split('/').pop();
    paidId = (await paidCard.locator('a[href*="/Courses/Details/"]').first().getAttribute('href')).split('/').pop();
    await screenshot(page, 'UAT-01-guest-catalog');
    await page.goto(`/Courses/Details/${paidId}`);
    assert.match(await page.locator('body').innerText(), /199[.,]000/);
    await screenshot(page, 'UAT-01-guest-paid-detail');
    await page.goto(`/Learning/Course/${freeId}`);
    assert.equal(new URL(page.url()).pathname, '/Account/Login');
    await screenshot(page, 'UAT-01-guest-login-required');
  });
  await runCase('UAT-02', 'Student 2: paid enrollment', async () => {
    const page = await createPage();
    await login(page, 'student2');
    await page.goto(`/Learning/Course/${paidId}`);
    assert.equal(new URL(page.url()).pathname, '/Account/AccessDenied');
    const csrf = await page.request.post('/Checkout/CreateOrder', { form: { CourseId: paidId } });
    assert.equal(csrf.status(), 400, 'POST without CSRF must fail.');
    await page.goto(`/Checkout?courseId=${paidId}`);
    await Promise.all([page.waitForURL(/\/Checkout\/Order\//), page.getByRole('button', { name: 'Tiếp tục thanh toán' }).click()]);
    await screenshot(page, 'UAT-02-pending-order');
    await Promise.all([page.waitForLoadState('load'), page.getByRole('button', { name: 'Mô phỏng thành công', exact: true }).click()]);
    await page.getByText('Thanh toán thử nghiệm thành công.', { exact: false }).waitFor();
    await screenshot(page, 'UAT-02-verified-payment');
    await page.reload();
    await page.getByRole('link', { name: 'Vào học', exact: true }).click();
    assert.match(new URL(page.url()).pathname, /\/Learning\/Course\//);
    await page.locator('a[href*="/Learning/Lesson/"]').first().click();
    assert.match(await page.locator('body').innerText(), /Good morning\. Let me introduce our team\./);
    await screenshot(page, 'UAT-02-paid-lesson');
    await page.goto('/Learning');
    assert.match(await page.locator('body').innerText(), /Business English/);
    await page.goto('/Checkout/History');
    assert.equal(await page.locator('tbody tr').count(), 1, 'Refresh must preserve one order.');
    await screenshot(page, 'UAT-02-one-transaction');
    await page.goto('/Admin/Transactions');
    assert.equal(new URL(page.url()).pathname, '/Account/AccessDenied');
    // Student 2 is not enrolled in the free final yet.
  });
  await runCase('UAT-03', 'Student 1: final and completion', async () => {
    const page = await createPage();
    await login(page, 'student1');
    await page.goto(`/Learning/Course/${freeId}`);
    assert.equal(await page.locator('.course-completed').count(), 0);
    const finalLink = page.locator('a[href*="/Assessment/Take/"]').first();
    finalId = (await finalLink.getAttribute('href')).split('/').pop();
    const lessonLinks = await page.locator('a[href*="/Learning/Lesson/"]').evaluateAll(links => [...new Set(links.map(link => link.getAttribute('href')))]);
    for (const link of lessonLinks.reverse()) {
      await page.goto(link);
      const complete = page.getByRole('button', { name: 'Đánh dấu hoàn thành', exact: true });
      if (await complete.count()) await Promise.all([page.waitForLoadState('load'), complete.click()]);
    }
    await page.goto(`/Learning/Course/${freeId}`);
    assert.equal(await page.locator('.course-completed').count(), 0, 'Lessons alone do not complete a final-linked course.');
    await screenshot(page, 'UAT-03-lessons-complete-final-required');
    await page.goto(`/Assessment/Take/${finalId}`);
    assert.doesNotMatch(await page.content(), /IsCorrect|Use &#x27;goes&#x27;/i);
    await page.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }).click();
    await page.locator('[data-assessment-form]').waitFor();
    attemptId = new URL(page.url()).searchParams.get('attemptId');
    assert.ok(attemptId);
    const deadline = await page.locator('[data-assessment-clock]').getAttribute('data-deadline');
    assert.equal(await page.locator('fieldset.assessment-question').count(), 5);
    await screenshot(page, 'UAT-03-final-taking');
    const choices = ['goes', 'are', 'finished', 'delighted', 'first'];
    for (let i = 0; i < choices.length; i++) {
      await Promise.all([
        page.waitForResponse(response => response.url().includes('/Assessment/Save/') && response.request().method() === 'POST' && response.status() === 200),
        page.locator('fieldset.assessment-question').nth(i).getByLabel(choices[i], { exact: true }).check()
      ]);
    }
    await page.reload();
    assert.equal(new URL(page.url()).searchParams.get('attemptId'), attemptId);
    assert.equal(await page.locator('[data-assessment-clock]').getAttribute('data-deadline'), deadline, 'Resume keeps the original deadline.');
    for (let i = 0; i < choices.length; i++) assert.ok(await page.locator('fieldset.assessment-question').nth(i).getByLabel(choices[i], { exact: true }).isChecked());
    await page.locator('[data-submit-button]').click();
    await page.waitForURL(/\/Assessment\/Result\//);
    assert.equal((await page.locator('.assessment-score').innerText()).replace(/\s/g, ''), '80/100');
    assert.match(await page.locator('body').innerText(), /Grammar/);
    assert.match(await page.locator('body').innerText(), /Vocabulary/);
    await screenshot(page, 'UAT-03-final-result-80');
    const resultTime = await page.locator('time').first().getAttribute('datetime');
    await page.reload();
    assert.equal(await page.locator('time').first().getAttribute('datetime'), resultTime);
    await page.goto(`/Learning/Course/${freeId}`);
    await page.locator('.course-completed').waitFor();
    await screenshot(page, 'UAT-03-course-completed');
    await page.setViewportSize({ width: 390, height: 844 });
    await screenshot(page, 'UAT-03-mobile-completed');
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'Course fits a mobile viewport.');
  });
  await runCase('UAT-04', 'Teacher 1: ownership and preview', async () => {
    const page = await createPage();
    await login(page, 'teacher1');
    await page.goto('/TeacherCourses');
    assert.equal(await page.locator('.course-card').count(), 3);
    await screenshot(page, 'UAT-04-teacher-own-courses');
    await page.goto(`/Learning/Course/${freeId}`);
    await page.locator('a[href*="/Learning/Lesson/"]').first().click();
    assert.match(await page.locator('body').innerText(), /Hello! Welcome/);
    await screenshot(page, 'UAT-04-teacher-preview');
    const other = await createPage();
    await login(other, 'teacher2');
    await other.goto('/TeacherCourses');
    assert.equal(await other.locator('.course-card').count(), 0);
    await other.goto(`/TeacherCourses/Edit/${freeId}`);
    assert.equal(new URL(other.url()).pathname, '/Account/AccessDenied');
    await screenshot(other, 'UAT-04-foreign-teacher-denied');
  });
  await runCase('UAT-05', 'Admin 1: transaction and user lookup', async () => {
    const page = await createPage();
    await login(page, 'admin1');
    await page.goto('/Admin/Transactions?search=student2%40demo.example.test');
    assert.equal(await page.locator('tbody tr').count(), 1);
    assert.match(await page.locator('tbody').innerText(), /Đã thanh toán/);
    await screenshot(page, 'UAT-05-admin-payment');
    await page.goto('/Admin/Users');
    assert.equal(await page.locator('tbody tr').count(), 5);
    await screenshot(page, 'UAT-05-admin-users');
    await page.goto(`/TeacherCourses/Edit/${freeId}`);
    assert.equal(new URL(page.url()).pathname, '/Account/AccessDenied');
    await page.goto(`/Assessment/Take/${finalId}`);
    assert.equal(new URL(page.url()).pathname, '/Account/AccessDenied');
    await screenshot(page, 'UAT-05-admin-content-denied');
  });
  await runCase('UAT-06', 'Student 2: free enrollment and result privacy', async () => {
    const page = await createPage();
    await login(page, 'student2');
    const deniedFinal = await page.goto(`/Assessment/Take/${finalId}`);
    assert.ok(deniedFinal.status() === 404 || new URL(page.url()).pathname === '/Account/AccessDenied');
    const deniedResult = await page.goto(`/Assessment/Result/${attemptId}`);
    assert.ok(deniedResult.status() === 404 || new URL(page.url()).pathname === '/Account/AccessDenied');
    await page.goto(`/Courses/Details/${freeId}`);
    await page.locator('form[action*="/Courses/Enroll"] button').click();
    await page.waitForURL(/\/Learning\/Course\//);
    await screenshot(page, 'UAT-06-free-enrollment');
  });
  assert.deepEqual(errors, [], 'No JavaScript errors during browser UAT.');
} finally {
  const report = {
    kind: 'Automated browser UAT; not a signed human/team rehearsal',
    ...source,
    finishedAtUtc: new Date().toISOString(), timezone: 'Asia/Saigon', baseURL, cases, javascriptErrors: errors
  };
  await writeFile(`${output}/results.json`, JSON.stringify(report, null, 2) + '\n');
  await Promise.all(contexts.map(context => context.close()));
  await browser.close();
  if (!process.env.ELP_UAT_CANDIDATE) assert.deepEqual(gitState(), source, 'Source changed during UAT; rerun against a stable candidate.');
}

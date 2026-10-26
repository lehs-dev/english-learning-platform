import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';

assert.equal(process.env.ELP_STUDENT_UAT_ISOLATED, 'true', 'Use an isolated seeded demo.');
const baseURL = process.env.ELP_UAT_URL;
assert.ok(baseURL && ['127.0.0.1', 'localhost'].includes(new URL(baseURL).hostname));
const password = process.env.LearningDemo__Password;
assert.ok(password);
const output = process.env.ELP_UAT_RESULTS;
assert.ok(output);
await mkdir(output, { recursive: true });
const source = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const uncommittedChanges = Boolean(execFileSync('git', ['status', '--porcelain'], { encoding: 'utf8' }).trim());
const browser = await chromium.launch({ headless: true });
const cases = [];
const errors = [];
const contexts = [];
const createPage = async () => {
  const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  contexts.push(context);
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  return page;
};
const login = async (page, actor) => {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(`${actor}@demo.example.test`);
  await page.locator('#Password').fill(password);
  await page.locator('form.account-form button[type=submit]').click();
  await page.waitForURL(url => !url.pathname.includes('/Account/Login'));
};
async function check(name, work) {
  try { await work(); cases.push({ name, passed: true }); console.log(`${name}: Passed`); }
  catch (error) { cases.push({ name, passed: false, error: String(error.message).split(password).join('[REDACTED]') }); throw error; }
}
try {
  const guest = await createPage();
  let course;
  await check('Guest catalog and protected route', async () => {
    assert.equal((await guest.goto('/Courses')).status(), 200);
    const link = guest.locator('a[href*="/Courses/Details/"]').first();
    const href = await link.getAttribute('href');
    course = href.split('/').pop();
    await guest.goto(href);
    await guest.screenshot({ path: `${output}/guest-course.png`, fullPage: true });
    await guest.goto(`/Learning/Course/${course}`);
    assert.equal(new URL(guest.url()).pathname, '/Account/Login');
  });
  const teacher = await createPage();
  let owned;
  await check('Teacher manages and previews own course', async () => {
    await login(teacher, 'teacher1');
    await teacher.goto('/TeacherCourses');
    const links = teacher.locator('a[href*="/TeacherCourses/Manage/"]');
    assert.ok(await links.count() > 0);
    const href = await links.first().getAttribute('href'); owned = href.split('/').pop();
    await teacher.goto(href);
    assert.ok(await teacher.locator('.module-panel').count() > 0);
    await teacher.screenshot({ path: `${output}/teacher-manage.png`, fullPage: true });
    await teacher.goto(`/Learning/Course/${owned}`);
    assert.ok(await teacher.locator('a[href*="/Learning/Lesson/"]').count() > 0);
  });
  await check('Foreign Teacher cannot edit own-course URL of Teacher1', async () => {
    const other = await createPage();
    await login(other, 'teacher2');
    await other.goto(`/TeacherCourses/Edit/${owned}`);
    assert.equal(new URL(other.url()).pathname, '/Account/AccessDenied');
    await other.screenshot({ path: `${output}/teacher-owner-denied.png` });
  });
  await check('Student cannot read Admin pages', async () => {
    const student = await createPage();
    await login(student, 'student2');
    await student.goto('/Admin/Users');
    assert.equal(new URL(student.url()).pathname, '/Account/AccessDenied');
  });
  await check('Admin sees transactions and cannot author Teacher course', async () => {
    const admin = await createPage();
    await login(admin, 'admin1');
    assert.equal((await admin.goto('/Admin')).status(), 200);
    assert.equal((await admin.goto('/Admin/Transactions')).status(), 200);
    assert.ok(await admin.locator('tbody tr').count() > 0);
    await admin.screenshot({ path: `${output}/admin-transactions.png`, fullPage: true });
    await admin.goto(`/TeacherCourses/Edit/${owned}`);
    assert.equal(new URL(admin.url()).pathname, '/Account/AccessDenied');
  });
  assert.deepEqual(errors, []);
} catch (error) {
  console.error(String(error.message).split(password).join('[REDACTED]'));
  process.exitCode = 1;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify({ baseURL, baseCommit: source, uncommittedChanges,
    finishedAtUtc: new Date().toISOString(), cases, javascriptErrors: errors }, null, 2));
  await Promise.all(contexts.map(context => context.close()));
  await browser.close();
}

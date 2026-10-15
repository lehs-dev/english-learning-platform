import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';

// Dedicated demo database only. All mutations go through the existing UI/HTTP
// actions as an authorized demo actor; this suite never resets existing data.
const baseURL = process.env.ELP_UAT_URL ?? 'http://127.0.0.1:5086';
const password = process.env.LearningDemo__Password;
assert.ok(password, 'Set LearningDemo__Password through environment.');
const output = process.env.ELP_UAT_RESULTS ?? 'TestResults/learning-commerce-uat';
const redact = value => String(value).split(password).join('[REDACTED]');
const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
const sourceState = async () => {
  const paths = git('ls-files', '--cached', '--others', '--exclude-standard', '--', 'src', 'tests', 'scripts', '.github')
    .split('\n').filter(Boolean).sort();
  const hash = createHash('sha256');
  for (const path of paths) {
    hash.update(path).update('\0').update(await readFile(path)).update('\0');
  }
  return { candidateSha: git('rev-parse', 'HEAD'), branch: git('branch', '--show-current'),
    workingTreeClean: !git('status', '--porcelain'), sourceDigest: hash.digest('hex') };
};
const source = await sourceState();
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const contexts = [];
const cases = [];
const errors = [];
const shots = [];
const data = {};
const startedAtUtc = new Date().toISOString();
const createPage = async actor => {
  const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  contexts.push(context);
  await context.route('**/*', route => new URL(route.request().url()).origin === new URL(baseURL).origin
    ? route.continue() : route.abort());
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', error => errors.push(redact(error.message)));
  if (actor) {
    await page.goto('/Account/Login');
    await page.locator('#Email').fill(`${actor}@demo.example.test`);
    await page.locator('#Password').fill(password);
    await submit(page, page.locator('form.account-form button[type=submit]'));
    assert.notEqual(new URL(page.url()).pathname, '/Account/Login');
  }
  return page;
};
const submit = async (page, button) => Promise.all([page.waitForNavigation({ waitUntil: 'load' }), button.click()]);
const screenshot = async (page, name) => {
  await page.screenshot({ path: `${output}/${name}.png`, fullPage: true });
  shots.push(`${name}.png`);
};
const csrf = async page => {
  const response = await page.request.get('/Profile');
  assert.equal(response.status(), 200);
  const token = (await response.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  assert.ok(token, 'Profile must provide a CSRF token for the same actor.');
  return token[1];
};
const post = async (page, path, form = {}, expected = 200) => {
  const response = await page.request.post(path, { form: { ...form, __RequestVerificationToken: await csrf(page) } });
  assert.equal(response.status(), expected, `Unexpected response at ${path}`);
  return response;
};
const denied = async (page, path) => {
  const response = await page.goto(path);
  assert.equal(response.status(), 403);
  assert.equal(new URL(page.url()).pathname, '/Account/AccessDenied');
};
const courseProgress = async (page, course, completed, total, timestamp) => {
  assert.equal((await page.goto(`/Learning/Course/${course.id}`)).status(), 200);
  if (total === 0) {
    assert.match(await page.locator('.learning-progress').innerText(), /Chưa có nội dung khả dụng/);
    assert.equal(await page.locator('.learning-progress progress').count(), 0);
  } else {
    const bar = page.locator('.learning-progress progress');
    assert.equal(Number(await bar.getAttribute('value')), completed);
    assert.equal(Number(await bar.getAttribute('max')), total);
  }
  if (timestamp) assert.equal(await page.locator('.page-heading time').getAttribute('datetime'), timestamp);
};
const complete = async (page, lesson, value) => {
  assert.equal((await page.goto(`/Learning/Lesson/${lesson}`)).status(), 200);
  const name = value ? 'Đánh dấu hoàn thành' : 'Đánh dấu chưa hoàn thành';
  const button = page.getByRole('button', { name, exact: true });
  if (await button.count()) await submit(page, button);
  else await post(page, `/Learning/Completion/${lesson}`, { completed: String(value) });
};
const contentPath = (course, kind, parent, id) => `/TeacherCourses/Content?courseId=${course.id}&kind=${kind}&parentId=${parent}${id ? `&id=${id}` : ''}`;
const resourceIdFromUrl = url => {
  const id = url.searchParams.get('id') ?? url.pathname.split('/').pop();
  assert.match(id, /^[a-f0-9-]{36}$/i, `Missing resource ID in ${url.pathname}`);
  return id;
};
const editLesson = async (teacher, course, lesson, title, status) => {
  await teacher.goto(contentPath(course, 'Lesson', course.moduleId, lesson));
  await teacher.locator('#Input_Title').fill(title);
  await teacher.locator('#Input_Status').selectOption(status);
  await submit(teacher, teacher.getByRole('button', { name: 'Lưu nội dung', exact: true }));
  assert.match(new URL(teacher.url()).pathname, /\/TeacherCourses\/Manage\//);
};
const editModule = async (teacher, course, visibility) => {
  await teacher.goto(contentPath(course, 'Module', course.id, course.moduleId));
  await teacher.locator('#Input_Visibility').selectOption(visibility);
  await submit(teacher, teacher.getByRole('button', { name: 'Lưu nội dung', exact: true }));
};
const changeStatus = async (teacher, course, name) => {
  await teacher.goto(`/TeacherCourses/Manage/${course.id}`);
  await submit(teacher, teacher.getByRole('button', { name, exact: true }));
};
const createCourse = async (teacher, paid) => {
  const course = { title: `Week 6 UAT · ${paid ? 'Paid' : 'Free'} Learning Commerce`, paid, lessons: [] };
  await teacher.goto('/TeacherCourses/Edit');
  await teacher.locator('#Input_Title').fill(course.title);
  await teacher.locator('#Input_Description').fill('Dữ liệu UAT riêng: ghi danh, thanh toán và tiến độ động.');
  await teacher.locator('#Input_IsPaid').selectOption(String(paid));
  if (paid) await teacher.locator('#Input_Price').fill('199000');
  await submit(teacher, teacher.getByRole('button', { name: 'Lưu khóa học', exact: true }));
  course.id = new URL(teacher.url()).pathname.split('/').pop();
  await teacher.goto(contentPath(course, 'Module', course.id));
  await teacher.locator('#Input_Title').fill('UAT module');
  await submit(teacher, teacher.getByRole('button', { name: 'Lưu nội dung', exact: true }));
  const moduleEdit = new URL(await teacher.getByRole('link', { name: 'Sửa Module', exact: true }).getAttribute('href'), baseURL);
  course.moduleId = resourceIdFromUrl(moduleEdit);
  for (let i = 0; i < 3; i++) {
    const title = `UAT lesson ${i + 1}`;
    await teacher.goto(contentPath(course, 'Lesson', course.moduleId));
    await teacher.locator('#Input_Title').fill(title);
    await submit(teacher, teacher.getByRole('button', { name: 'Lưu nội dung', exact: true }));
    const row = teacher.locator('.lesson-editor').filter({ has: teacher.locator('h4').filter({ hasText: title }) });
    const editUrl = new URL(await row.getByRole('link', { name: 'Sửa Lesson', exact: true }).getAttribute('href'), baseURL);
    const id = resourceIdFromUrl(editUrl);
    course.lessons.push(id);
    await teacher.goto(contentPath(course, 'Resource', id));
    await teacher.locator('#Input_Title').fill(`UAT text ${i + 1}`);
    await teacher.locator('#Input_ContentText').fill(`LC_UAT_PRIVATE_TEXT_${i + 1}: learn and track progress.`);
    await submit(teacher, teacher.getByRole('button', { name: 'Lưu nội dung', exact: true }));
    if (i < 2) await editLesson(teacher, course, id, title, '2');
  }
  await changeStatus(teacher, course, 'Publish');
  return course;
};
const runCase = async (id, actor, expected, work) => {
  const start = new Date().toISOString();
  const firstShot = shots.length;
  try {
    const actual = await work();
    cases.push({ id, actor, expected, actual, outcome: 'Passed', startedAtUtc: start,
      finishedAtUtc: new Date().toISOString(), screenshots: shots.slice(firstShot) });
    console.log(`${id}: Passed (${actor})`);
  } catch (error) {
    const pages = contexts.flatMap(context => context.pages()).filter(page => !new URL(page.url()).pathname.includes('/Account/Login'));
    for (let i = 0; i < pages.length; i++) await screenshot(pages[i], `${id}-failure-context-${i + 1}`);
    cases.push({ id, actor, expected, outcome: 'Failed', error: redact(error.message), startedAtUtc: start,
      stack: redact(error.stack), pageUrls: pages.map(page => redact(page.url())),
      screenshots: shots.slice(firstShot) });
    throw new Error(redact(error.message));
  }
};

try {
  const guest = await createPage();
  const teacher = await createPage('teacher1');
  const otherTeacher = await createPage('teacher2');
  const freeStudent = await createPage('student1');
  const paidStudent = await createPage('student2');
  await runCase('LC-UAT-01', 'Teacher 1', 'Create Free/Paid courses using forms; Published lessons only.', async () => {
    data.free = await createCourse(teacher, false);
    data.paid = await createCourse(teacher, true);
    await screenshot(teacher, 'LC-UAT-01-teacher-published-paid');
    return 'Created two Published courses, each with two Published lessons and one Draft text lesson; paid price 199000 VND.';
  });
  await runCase('LC-UAT-02', 'Guest', 'Public metadata excludes protected text; learning requires login.', async () => {
    await guest.goto('/Courses?search=Week%206%20UAT');
    assert.equal(await guest.locator('article.course-card').count(), 2);
    await screenshot(guest, 'LC-UAT-02-catalog');
    await guest.goto(`/Courses/Details/${data.paid.id}`);
    assert.doesNotMatch(await guest.content(), /LC_UAT_PRIVATE_TEXT|UAT lesson 3/);
    await screenshot(guest, 'LC-UAT-02-paid-metadata');
    await guest.goto(`/Learning/Lesson/${data.paid.lessons[0]}`);
    assert.equal(new URL(guest.url()).pathname, '/Account/Login');
    await screenshot(guest, 'LC-UAT-02-login-required');
    return 'Catalog/detail returned metadata only; direct protected lesson redirected to Login.';
  });
  await runCase('LC-UAT-03', 'Teacher 2', 'Foreign management/report/resource and valid-CSRF mutations denied.', async () => {
    await denied(otherTeacher, `/TeacherCourses/Manage/${data.paid.id}`);
    await post(otherTeacher, `/TeacherCourses/Archive/${data.paid.id}`, {}, 403);
    await denied(otherTeacher, `/TeacherCourses/Students/${data.paid.id}`);
    await screenshot(otherTeacher, 'LC-UAT-03-foreign-owner-denied');
    assert.equal((await guest.goto(`/Courses/Details/${data.paid.id}`)).status(), 200);
    return 'Foreign Teacher received 403; course remained public after rejected Archive POST.';
  });
  await runCase('LC-UAT-04', 'Student 1', 'Free enrollment requires CSRF; duplicate POST preserves access.', async () => {
    await denied(freeStudent, `/Learning/Lesson/${data.free.lessons[0]}`);
    assert.equal((await freeStudent.request.post(`/Courses/EnrollFree/${data.free.id}`)).status(), 400);
    await freeStudent.goto(`/Courses/Details/${data.free.id}`);
    await submit(freeStudent, freeStudent.getByRole('button', { name: 'Tham gia miễn phí', exact: true }));
    await post(freeStudent, `/Courses/EnrollFree/${data.free.id}`);
    await courseProgress(freeStudent, data.free, 0, 2);
    await screenshot(freeStudent, 'LC-UAT-04-free-enrolled');
    return 'CSRF omission returned 400; UI enrollment and repeated valid POST opened the same course at 0/2.';
  });
  await runCase('LC-UAT-05', 'Student 2', 'Pending/failure deny content; retry success and replay keep one grant.', async () => {
    await denied(paidStudent, `/Learning/Lesson/${data.paid.lessons[0]}`);
    await paidStudent.goto(`/Checkout?courseId=${data.paid.id}`);
    await submit(paidStudent, paidStudent.getByRole('button', { name: 'Tiếp tục thanh toán', exact: true }));
    data.failedOrder = new URL(paidStudent.url()).pathname.split('/').pop();
    await screenshot(paidStudent, 'LC-UAT-05-pending-order');
    const fabricated = await paidStudent.request.get(`/Checkout/Result/${data.failedOrder}?paid=true&status=Succeeded`);
    assert.match(await fabricated.text(), /Chờ thanh toán/);
    await submit(paidStudent, paidStudent.getByRole('button', { name: 'Mô phỏng thất bại', exact: true }));
    assert.match(await paidStudent.locator('body').innerText(), /Thanh toán thất bại/);
    await screenshot(paidStudent, 'LC-UAT-05-failed-order');
    await denied(paidStudent, `/Learning/Lesson/${data.paid.lessons[0]}`);
    await paidStudent.goto(`/Checkout?courseId=${data.paid.id}`);
    await submit(paidStudent, paidStudent.getByRole('button', { name: 'Tiếp tục thanh toán', exact: true }));
    data.successOrder = new URL(paidStudent.url()).pathname.split('/').pop();
    assert.notEqual(data.successOrder, data.failedOrder);
    await submit(paidStudent, paidStudent.getByRole('button', { name: 'Mô phỏng thành công', exact: true }));
    assert.match(await paidStudent.locator('body').innerText(), /Thanh toán thử nghiệm thành công/);
    for (let i = 0; i < 3; i++) await post(paidStudent, `/Checkout/Simulate/${data.successOrder}`, { scenario: 'success' });
    await paidStudent.reload();
    await screenshot(paidStudent, 'LC-UAT-05-success-after-replay');
    await paidStudent.goto('/Checkout/History');
    assert.equal(await paidStudent.locator('tbody tr').filter({ hasText: data.paid.title }).count(), 2);
    await screenshot(paidStudent, 'LC-UAT-05-two-orders');
    await courseProgress(paidStudent, data.paid, 0, 2);
    return 'Pending/failure blocked content; retry created a new order, success opened learning, three replay POSTs preserved success and two-order history.';
  });
  await runCase('LC-UAT-06', 'Students 1 and 2', 'Learn in any order; progress 50% then 100%; completion stable.', async () => {
    for (const [page, course] of [[freeStudent, data.free], [paidStudent, data.paid]]) {
      await complete(page, course.lessons[1], true);
      await courseProgress(page, course, 1, 2);
      await screenshot(page, `LC-UAT-06-${course.paid ? 'paid' : 'free'}-50-percent`);
      await complete(page, course.lessons[0], true);
      await courseProgress(page, course, 2, 2);
      course.completedAt = await page.locator('.page-heading time').getAttribute('datetime');
      assert.ok(course.completedAt);
      await screenshot(page, `LC-UAT-06-${course.paid ? 'paid' : 'free'}-completed`);
    }
    return 'Both courses moved 0/2 → 1/2 → 2/2 while studying lesson 2 first; historical completion timestamps recorded.';
  });
  await runCase('LC-UAT-07', 'Teacher 1 and Student 2', 'Hide/restore/add content changes progress; completion and Continue remain valid.', async () => {
    const course = data.paid;
    await editLesson(teacher, course, course.lessons[0], 'UAT lesson 1', '3');
    await denied(paidStudent, `/Learning/Lesson/${course.lessons[0]}`);
    const hiddenContinue = await paidStudent.request.get(`/Learning/Continue/${course.id}`);
    assert.equal(new URL(hiddenContinue.url()).pathname, `/Learning/Lesson/${course.lessons[1]}`);
    await courseProgress(paidStudent, course, 1, 1, course.completedAt);
    await screenshot(paidStudent, 'LC-UAT-07-hidden-lesson-progress');
    await editLesson(teacher, course, course.lessons[0], 'UAT lesson 1', '2');
    await courseProgress(paidStudent, course, 2, 2, course.completedAt);
    await editLesson(teacher, course, course.lessons[2], 'UAT lesson 3', '2');
    await courseProgress(paidStudent, course, 2, 3, course.completedAt);
    await screenshot(paidStudent, 'LC-UAT-07-added-lesson-66-percent');
    await complete(paidStudent, course.lessons[0], false);
    await courseProgress(paidStudent, course, 1, 3, course.completedAt);
    await editModule(teacher, course, '2');
    await courseProgress(paidStudent, course, 0, 0, course.completedAt);
    const emptyContinue = await paidStudent.request.get(`/Learning/Continue/${course.id}`);
    assert.equal(new URL(emptyContinue.url()).pathname, `/Learning/Course/${course.id}`);
    await screenshot(paidStudent, 'LC-UAT-07-empty-effective-lessons');
    await editModule(teacher, course, '1');
    await courseProgress(paidStudent, course, 1, 3, course.completedAt);
    return 'Progress followed 1/1 → 2/2 → 2/3 → 1/3 → empty → 1/3; completion timestamp unchanged and Continue never opened hidden content.';
  });
  await runCase('LC-UAT-08', 'Teacher 1 and both Students', 'Unpublish removes public access/new enrollment; existing students can continue.', async () => {
    for (const [page, newcomer, course] of [[freeStudent, paidStudent, data.free], [paidStudent, freeStudent, data.paid]]) {
      await changeStatus(teacher, course, 'Unpublish');
      assert.equal((await guest.goto(`/Courses/Details/${course.id}`)).status(), 404);
      await denied(newcomer, `/Learning/Lesson/${course.lessons[0]}`);
      if (course.paid) await post(newcomer, '/Checkout/CreateOrder', { CourseId: course.id });
      else await post(newcomer, `/Courses/EnrollFree/${course.id}`, {}, 404);
      await page.goto(`/Learning/Lesson/${course.lessons[0]}`);
      assert.match(await page.locator('body').innerText(), /LC_UAT_PRIVATE_TEXT_1/);
      await complete(page, course.lessons[0], false);
      await courseProgress(page, course, 1, course.paid ? 3 : 2, course.completedAt);
      await screenshot(page, `LC-UAT-08-${course.paid ? 'paid' : 'free'}-unpublished`);
    }
    return 'Both public details returned 404; non-enrolled students remained denied; existing students could read and set Incomplete after Unpublish.';
  });
  await runCase('LC-UAT-09', 'Teacher 1 and both Students', 'Archive preserves reading/history and denies forged progress writes.', async () => {
    for (const [page, course] of [[freeStudent, data.free], [paidStudent, data.paid]]) {
      await changeStatus(teacher, course, 'Archive');
      await page.goto(`/Learning/Lesson/${course.lessons[0]}`);
      assert.match(await page.locator('body').innerText(), /LC_UAT_PRIVATE_TEXT_1/);
      assert.equal(await page.locator('form[action*="/Learning/Completion/"]').count(), 0);
      await post(page, `/Learning/Completion/${course.lessons[0]}`, { completed: 'true' }, 403);
      await courseProgress(page, course, 1, course.paid ? 3 : 2, course.completedAt);
      await screenshot(page, `LC-UAT-09-${course.paid ? 'paid' : 'free'}-archived`);
    }
    await paidStudent.setViewportSize({ width: 390, height: 844 });
    await screenshot(paidStudent, 'LC-UAT-09-mobile-archived');
    assert.ok(await paidStudent.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
    return 'Archived courses stayed readable, Completion forms disappeared, valid-CSRF forged POSTs returned 403, progress and timestamps unchanged; mobile had no horizontal overflow.';
  });
  await runCase('LC-UAT-10', 'Teacher 1', 'Owner report preserves completion history; failed/replayed payments do not inflate revenue.', async () => {
    await teacher.goto(`/TeacherCourses/Students/${data.paid.id}`);
    const statistics = async () => (await teacher.locator('.stat-value').allTextContents()).map(value => Number(value.replace(/[^0-9]/g, '')));
    assert.deepEqual(await statistics(), [1, 1, 2, 199000]);
    assert.equal(await teacher.locator('tbody tr').count(), 1);
    assert.match(await teacher.locator('tbody tr').innerText(), /Demo student2/);
    assert.equal(await teacher.locator('tbody progress').getAttribute('value'), '1');
    assert.equal(await teacher.locator('tbody progress').getAttribute('max'), '3');
    await screenshot(teacher, 'LC-UAT-10-owner-commerce-progress-report');
    await teacher.goto(`/TeacherCourses/Students/${data.paid.id}?Search=no-such-demo-student`);
    assert.equal(await teacher.locator('tbody tr').count(), 0);
    assert.deepEqual(await statistics(), [1, 1, 2, 199000]);
    return 'Owner report showed one enrollment, one historical completion, two orders and 199000 VND successful receipts; filtering students left course aggregates unchanged.';
  });
  assert.deepEqual(errors, [], 'Browser JavaScript errors must be resolved.');
  assert.deepEqual(await sourceState(), source, 'Source changed during UAT; repeat against a stable snapshot.');
} finally {
  const report = { ...source, startedAtUtc, finishedAtUtc: new Date().toISOString(), timezone: 'Asia/Saigon',
    browser: browser.version(), viewport: '1440x1000; mobile 390x844', baseURL, data, cases,
    pageErrors: errors, screenshots: shots, automated: true };
  await writeFile(`${output}/results.json`, JSON.stringify(report, null, 2));
  for (const context of contexts) await context.close();
  await browser.close();
}

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

// Real MVC forms on a fresh, privately seeded Development database only.
// Run each --group in a separate process to keep Chromium and app memory bounded.
const groups = ['auth', 'learning', 'practice', 'assessment', 'checkout', 'responsive'];
const args = process.argv.slice(2);
const selected = args.length === 0 ? 'all'
  : args.length === 2 && args[0] === '--group' ? args[1]
    : args.length === 1 && args[0].startsWith('--group=') ? args[0].slice(8) : null;
assert.ok(selected === 'all' || groups.includes(selected),
  'Usage: node student-flow-uat.mjs --group auth|learning|practice|assessment|checkout|responsive|all');
assert.equal(process.env.ELP_STUDENT_UAT_ISOLATED, 'true',
  'ELP_STUDENT_UAT_ISOLATED=true is required for a fresh private demo database.');
assert.ok(process.env.ELP_UAT_URL, 'Set ELP_UAT_URL to the isolated local Development app.');
const appURL = new URL(process.env.ELP_UAT_URL);
assert.ok(['http:', 'https:'].includes(appURL.protocol)
  && ['localhost', '127.0.0.1', '[::1]'].includes(appURL.hostname)
  && !appURL.username && !appURL.password && appURL.pathname === '/' && !appURL.search && !appURL.hash,
  'ELP_UAT_URL must be a loopback HTTP(S) origin without credentials or a path.');
const baseURL = appURL.origin;
const demoPassword = process.env.LearningDemo__Password;
assert.ok(demoPassword, 'Set LearningDemo__Password to the password used by the private demo seed.');
const studentPassword = 'StudentFlow_Uat123!';
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = resolve(process.env.ELP_UAT_RESULTS ?? join(root, 'TestResults/student-flow-uat'));
const freeTitle = 'Demo · Everyday English';
const starterTitle = 'Demo · English Starter A1';
const paidTitle = 'Demo · Business English';
const privacy = value => {
  let safe = String(value ?? '');
  for (const password of [demoPassword, studentPassword]) {
    const html = password.replaceAll('&', '&amp;').replaceAll('"', '&quot;')
      .replaceAll("'", '&#x27;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
    for (const variant of [password, JSON.stringify(password).slice(1, -1), encodeURIComponent(password), html])
      safe = safe.split(variant).join('[REDACTED]');
  }
  return safe;
};
const git = (...gitArgs) => execFileSync('git', gitArgs, { cwd: root, encoding: 'utf8' }).trim();
async function sourceState() {
  const paths = execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--',
    'src', 'tests/browser/student-flow-uat.mjs', 'scripts/test-student-flow.sh'], { cwd: root, encoding: 'utf8' })
    .split('\0').filter(Boolean).sort();
  const hash = createHash('sha256');
  for (const path of paths) hash.update(path).update('\0').update(await readFile(join(root, path))).update('\0');
  return { head: git('rev-parse', 'HEAD'), digest: hash.digest('hex'), fileCount: paths.length };
}
const before = await sourceState();
await mkdir(output, { recursive: true });
const report = { group: selected, baseURL, startedAtUtc: new Date().toISOString(), timezone: 'Asia/Saigon',
  sourceBefore: before, groups: [], screenshots: [], notes: [
    'All account/enrollment/answer/payment mutations use the real Student UI. No SQL, fixtures or response mocks.',
    'Order expiry and deadline advancement are covered by integration tests; this suite does not alter server time.',
    'Screenshots omit all Account pages. Passwords, cookies and antiforgery tokens are not saved.'
  ] };
let browser;
let current;

async function go(page, path, status = 200) {
  const response = await page.goto(path, { waitUntil: 'domcontentloaded' });
  assert.equal(response?.status(), status, `Unexpected HTTP status at ${new URL(page.url()).pathname}`);
  return response;
}
async function navigate(page, control) {
  await Promise.all([page.waitForNavigation({ waitUntil: 'domcontentloaded' }), control.click()]);
}
async function authForm(page, action) {
  const dialog = page.locator('[data-auth-dialog][open]');
  await dialog.getByRole('heading', {
    name: action === 'Register' ? 'Tạo tài khoản' : 'Đăng nhập', exact: true
  }).waitFor({ state: 'visible' });
  await dialog.locator(`form.account-form[action$="/Account/${action}" i]`).waitFor({ state: 'visible' });
}
async function screenshot(page, name) {
  if (/^\/Account(?:\/|$)/i.test(new URL(page.url()).pathname)
    || await page.locator('input[type="password"]').count()) return;
  await page.evaluate(() => window.scrollTo(0, 0));
  const filename = `${current.name}-${name}.png`;
  await page.screenshot({ path: join(output, filename), fullPage: false, animations: 'disabled' });
  report.screenshots.push(filename);
}
async function step(name, work) {
  const result = { name, startedAtUtc: new Date().toISOString() };
  current.steps.push(result);
  try {
    await work();
    result.outcome = 'Passed';
    console.log(`${current.name}: ${name}: Passed`);
  } catch (error) {
    result.outcome = 'Failed';
    result.error = privacy(error.message ?? error);
    throw error;
  } finally { result.finishedAtUtc = new Date().toISOString(); }
}
async function credentials(work) {
  try { return await work(); }
  catch { throw new Error('Registration/sign-in action failed; credential details withheld.'); }
}
async function signIn(page, email, credential = studentPassword) {
  await credentials(async () => {
    await page.locator('#Email').fill(email);
    await page.locator('#Password').fill(credential);
    await navigate(page, page.locator('form.account-form button[type="submit"]'));
    assert.notEqual(new URL(page.url()).pathname.toLowerCase(), '/account/login');
  });
}
async function register(page, { fromLogin = false } = {}) {
  const email = `student-uat-${current.name}-${randomUUID()}@example.test`;
  if (fromLogin) {
    const currentUrl = page.url();
    await page.locator('.auth-switch a').click();
    await authForm(page, 'Register');
    assert.equal(page.url(), currentUrl, 'Switching auth forms must keep the current page.');
  } else {
    await go(page, '/Account/Register');
    await authForm(page, 'Register');
  }
  await credentials(async () => {
    await page.locator('#FullName').fill('Student UAT');
    await page.locator('#Email').fill(email);
    await page.locator('#Password').fill(studentPassword);
    await page.locator('#ConfirmPassword').fill(studentPassword);
    const currentUrl = page.url();
    await page.locator('form.account-form button[type="submit"]').click();
    await authForm(page, 'Login');
    assert.equal(page.url(), currentUrl, 'Registration must show login inside the current popup.');
    assert.match(await page.locator('.notice-success').innerText(), /Đăng ký thành công/);
  });
  await signIn(page, email);
  return email;
}
async function logout(page) {
  await go(page, '/Learning/Dashboard');
  await page.locator('[data-account-menu] summary').click();
  await navigate(page, page.locator('[data-account-menu] .logout-item'));
  assert.equal(new URL(page.url()).pathname, '/Account/Login');
}
async function detail(page, title) {
  await go(page, `/Courses?Search=${encodeURIComponent(title)}`);
  const card = page.locator('article.course-card').filter({ has: page.getByRole('heading', { name: title, exact: true }) });
  assert.equal(await card.count(), 1, `Expected seeded course: ${title}`);
  await navigate(page, card.locator('h3 a'));
  const path = new URL(page.url()).pathname;
  assert.match(path, /^\/Courses\/Details\/[a-f0-9-]{36}$/i);
  return { id: path.split('/').pop(), path, title };
}
async function enroll(page, title) {
  const course = await detail(page, title);
  await navigate(page, page.getByRole('button', { name: /^Tham gia miễn phí ngay/ }));
  assert.equal(new URL(page.url()).pathname, `/Learning/Course/${course.id}`);
  course.lessonPaths = await page.locator('.course-outline a[href*="/Learning/Lesson/"]')
    .evaluateAll(links => [...new Set(links.map(link => new URL(link.href).pathname))]);
  assert.ok(course.lessonPaths.length > 0, 'Enrolled course must contain a visible lesson.');
  return course;
}
async function progress(page, done, total) {
  const bar = page.locator('.learning-progress progress').first();
  assert.equal(Number(await bar.getAttribute('value')), done);
  assert.equal(Number(await bar.getAttribute('max')), total);
}
async function practicePath(page, title) {
  await go(page, '/Practice');
  const card = page.locator('article').filter({ has: page.getByRole('heading', { name: title, exact: true }) });
  assert.equal(await card.count(), 1, `Expected seeded practice: ${title}`);
  const link = card.locator('a[href*="/Practice/Take/"]');
  const path = await link.getAttribute('href');
  await navigate(page, link);
  assert.ok(await page.locator('[data-practice-question]').count() >= 2);
  return path;
}
async function chooseFirstOptions(page) {
  const questions = page.locator('[data-practice-question]');
  for (let i = 0; i < await questions.count(); i++)
    await questions.nth(i).locator('label.exam-option-card').first().click();
}
async function submitPractice(page) {
  await navigate(page, page.getByRole('button', { name: 'Nộp bài & Xem giải thích', exact: true }));
  assert.match(new URL(page.url()).pathname, /^\/Practice\/Result\//);
  assert.ok(await page.locator('.answer-review').count() >= 2);
  assert.match(await page.locator('main').innerText(), /Đáp án chính xác:/i);
  assert.ok(await page.locator('.answer-explanation').count() >= 1);
  return new URL(page.url()).pathname;
}
const expectedAnswers = new Map([
  ['She ___ to school every day.', 'goes'],
  ['They ___ watching a film now.', 'are'],
  ['Yesterday, I ___ my homework.', 'finished'],
  ["Choose the word that means 'very happy'.", 'delighted'],
  ["The opposite of 'early' is ___.", 'late'],
  ['She usually ___ to the office by bus.', 'goes'],
  ['They ___ dinner when I called last night.', 'were having'],
  ['I have worked here ___ 2022.', 'since'],
  ['If it ___ tomorrow, we will take the train.', 'rains'],
  ['You ___ wear a seat belt in a car.', 'must'],
  ['The meeting was postponed, so it will happen ___ than planned.', 'later'],
  ['A person who designs buildings is an ___.', 'architect'],
  ['Please ___ the form before Friday.', 'complete'],
  ['The train was crowded, so there were no ___ seats.', 'available'],
  ['Mia bought fruit at the market because it was fresh and ___.', 'reasonable'],
  ['Where does Mia live?', 'Near the town centre'],
  ['How does Mia travel to work on weekdays?', 'On foot'],
  ['What did Mia buy last Saturday?', 'Apples and bread'],
  ['When did Mia meet Leo?', 'At noon'],
  ['Where are Mia and Leo planning to go next weekend?', 'The museum'],
  ['What time does City Library open from Monday to Friday?', '9:00'],
  ['What time does the library open on Saturday?', '10:00'],
  ['Where does the reading club meet?', 'Room Two'],
  ['What time does the reading club meet?', '4:30'],
  ['What should visitors bring to the library?', 'A library card']
]);
async function correctOption(page, index) {
  const question = page.locator('[data-question]').nth(index);
  const content = (await question.locator('.exam-question-content').innerText()).trim();
  const expected = expectedAnswers.get(content);
  assert.ok(expected, 'Assessment question must match the checked-in private demo fixture.');
  // Match the option's text itself; it is rendered as a span in the current Razor view.
  const labels = question.locator('label.exam-option-card');
  for (let i = 0; i < await labels.count(); i++) {
    if ((await labels.nth(i).locator('.exam-option-text').innerText()).trim() === expected) {
      return { label: labels.nth(i), value: await labels.nth(i).locator('input').getAttribute('value') };
    }
  }
  throw new Error('Correct seeded option is missing from the rendered question.');
}
async function saveChoice(page, index) {
  const option = await correctOption(page, index);
  const responsePromise = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname.startsWith('/Assessment/Save/'));
  await option.label.click();
  const response = await responsePromise;
  assert.equal(response.status(), 200, 'Autosave must reach the real server.');
  const request = response.request();
  const payload = await new Response(request.postDataBuffer(), {
    headers: { 'Content-Type': request.headers()['content-type'] }
  }).formData();
  assert.deepEqual(payload.getAll('Answers.Index'), [String(index)], 'Autosave must send only the changed MVC index.');
  assert.equal(payload.get(`Answers[${index}].SelectedOptionId`), option.value);
  assert.equal((await response.json()).saved, true);
  await page.waitForFunction(() => document.querySelector('[data-save-status]')?.textContent === 'Đã lưu đáp án.');
  return option.value;
}
async function checkChoice(page, index, value) {
  assert.equal(await page.locator(`[data-question] input[name="Answers[${index}].SelectedOptionId"]:checked`).inputValue(), value);
}
async function checkAudio(page) {
  const audio = page.locator('audio').first();
  assert.equal(await audio.count(), 1, 'The seeded listening stimulus must render an audio control.');
  const src = new URL(await audio.getAttribute('src'), baseURL);
  assert.equal(src.origin, baseURL, 'UAT listening must use the privately seeded local audio store.');
  const response = await page.request.get(src.href, { headers: { Range: 'bytes=0-1023' } });
  assert.ok([200, 206].includes(response.status()), 'Authorized Student must be able to fetch listening audio.');
  assert.match(response.headers()['content-type'] ?? '', /^audio\//i);
  assert.ok((await response.body()).length > 44, 'Audio response must contain playable data.');
  await audio.evaluate(element => { element.preload = 'metadata'; element.load(); });
  await page.waitForFunction(() => [...document.querySelectorAll('audio')].some(audio => audio.readyState >= 1));
}

const work = {
  async auth(page) {
    let course;
    await step('Guest selected course survives UI registration and sign-in', async () => {
      course = await detail(page, freeTitle);
      const currentUrl = page.url();
      await page.getByRole('link', { name: /^Đăng nhập để tham gia/ }).click();
      await authForm(page, 'Login');
      assert.equal(page.url(), currentUrl, 'Opening login must keep the selected course visible.');
      assert.equal(await page.locator('input[name="ReturnUrl"]').inputValue(), course.path);
      await register(page, { fromLogin: true });
      assert.equal(new URL(page.url()).pathname, course.path);
      await screenshot(page, 'selected-course-after-signup');
    });
    await step('Demo button preserves configured password characters and authenticates', async () => {
      await logout(page);
      await credentials(async () => {
        await page.locator('[data-demo-email="student1@demo.example.test"]').click();
        assert.ok(await page.locator('#Password').inputValue() === demoPassword,
          'Demo password autofill must preserve every configured character.');
        await navigate(page, page.locator('form.account-form button[type="submit"]'));
        assert.equal(new URL(page.url()).pathname, '/Learning/Dashboard');
      });
      await screenshot(page, 'demo-dashboard');
    });
  },
  async learning(page) {
    await step('Register a fresh Student and enroll through the free course UI', async () => { await register(page); });
    let course;
    await step('Lesson completion toggles persist and update course progress', async () => {
      course = await enroll(page, freeTitle);
      await progress(page, 0, course.lessonPaths.length);
      await go(page, course.lessonPaths[0]);
      await navigate(page, page.getByRole('button', { name: 'Hoàn thành bài học', exact: true }));
      await progress(page, 1, course.lessonPaths.length);
      await navigate(page, page.getByRole('button', { name: 'Đánh dấu chưa hoàn thành', exact: true }));
      await progress(page, 0, course.lessonPaths.length);
      await navigate(page, page.getByRole('button', { name: 'Hoàn thành bài học', exact: true }));
      await page.reload({ waitUntil: 'domcontentloaded' });
      await progress(page, 1, course.lessonPaths.length);
      for (let index = 1; index < course.lessonPaths.length; index++) {
        await go(page, course.lessonPaths[index]);
        await navigate(page, page.getByRole('button', { name: 'Hoàn thành bài học', exact: true }));
        await progress(page, index + 1, course.lessonPaths.length);
      }
      await screenshot(page, 'completed-last-lesson');
    });
    await step('Last lesson opens the Final instructions and all lessons is distinct from Course completed', async () => {
      const final = page.locator('[aria-labelledby="lesson-final-title"] a[href*="/Assessment/Take/"]');
      assert.equal(await final.count(), 1);
      await navigate(page, final);
      assert.equal(await page.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }).count(), 1);
      await go(page, `/Learning/Course/${course.id}`);
      await progress(page, course.lessonPaths.length, course.lessonPaths.length);
      assert.match(await page.locator('main').innerText(), /Hãy làm bài thi cuối khóa để hoàn tất khóa học/);
      assert.doesNotMatch(await page.locator('.course-study-room > .card-v2').first().innerText(), /Đã hoàn thành khóa học/);
      await screenshot(page, 'all-lessons-final-pending');
    });
    await step('Pass the seeded five-question Final and retain the recorded Course completion after reload', async () => {
      await navigate(page, page.locator('[aria-labelledby="final-assessment-title"] a[href*="/Assessment/Take/"]'));
      await navigate(page, page.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }));
      assert.equal(await page.locator('[data-question]').count(), 5);
      for (let index = 0; index < 5; index++) await saveChoice(page, index);
      await page.locator('[data-submit-button]').click();
      await page.locator('[data-submit-dialog][open]').waitFor();
      await navigate(page, page.locator('[data-confirm-submit]'));
      assert.match((await page.locator('.assessment-score').innerText()).replaceAll(/\s/g, ''), /^100\/100$/);
      assert.match(await page.locator('.assessment-passed').innerText(), /Đạt chuẩn yêu cầu/);
      await navigate(page, page.getByRole('link', { name: /^Về phòng học & Tiến độ/ }));
      assert.equal(new URL(page.url()).pathname, `/Learning/Course/${course.id}`);
      await progress(page, course.lessonPaths.length, course.lessonPaths.length);
      const hero = page.locator('.course-study-room > .card-v2').first();
      assert.match(await hero.innerText(), /Đã hoàn thành khóa học/);
      const recordedAt = await page.locator('.learning-progress time[datetime]').getAttribute('datetime');
      assert.ok(recordedAt, 'A passing Final after all lessons must record the historical completion time.');
      await page.reload({ waitUntil: 'domcontentloaded' });
      assert.equal(await page.locator('.learning-progress time[datetime]').getAttribute('datetime'), recordedAt);
      assert.match(await hero.innerText(), /Đã hoàn thành khóa học/);
      await screenshot(page, 'course-completed-after-final');
    });
  },
  async practice(page) {
    let course, resultPath;
    await step('Practice hub lists only available enrolled practices', async () => {
      await register(page);
      await go(page, '/Practice');
      assert.match(await page.locator('main').innerText(), /Chưa có bài luyện nào khả dụng/);
      course = await enroll(page, starterTitle);
      await practicePath(page, 'Demo · Checkpoint: Around town');
      assert.ok(await page.locator('.exam-stimulus-box').count() >= 1, 'Reading practice must include its passage.');
      assert.doesNotMatch(await page.locator('main').innerText(), /Đáp án chính xác:/i);
    });
    await step('Submit gives question feedback, explanations and the reading passage; retry works', async () => {
      await chooseFirstOptions(page);
      resultPath = await submitPractice(page);
      assert.ok(await page.locator('.answer-review .exam-stimulus-box').count() >= 1);
      await screenshot(page, 'reading-feedback');
      await navigate(page, page.getByRole('link', { name: /^Luyện lại bài này/ }));
      assert.equal(await page.locator('[data-practice-question] input[type="radio"]:checked:not([value=""])').count(), 0);
      await chooseFirstOptions(page);
      assert.equal(await submitPractice(page), resultPath);
      await navigate(page, page.getByRole('link', { name: /^Tiếp tục học/ }));
      assert.ok(course.lessonPaths.includes(new URL(page.url()).pathname));
      await go(page, '/Practice');
      const row = page.locator('table tbody tr').filter({ hasText: 'Demo · Checkpoint: Around town' });
      assert.equal(await row.count(), 1, 'Practice keeps one latest result after retry.');
      await navigate(page, row.getByRole('link', { name: 'Xem lại', exact: true }));
      assert.equal(new URL(page.url()).pathname, resultPath);
    });
    await step('Another enrolled Student cannot read the first Student practice result', async () => {
      await logout(page);
      await register(page);
      await enroll(page, starterTitle);
      await go(page, resultPath, 404);
    });
  },
  async assessment(page, context) {
    let takePath, resultPath, count;
    const saved = new Map();
    await step('Fresh Student starts the real mixed Placement form with passage and local listening audio', async () => {
      await register(page);
      await go(page, '/Assessment');
      const card = page.locator('article').filter({ has: page.getByRole('heading', { name: 'Demo · Placement Test A1', exact: true }) });
      await navigate(page, card.locator('a[href*="/Assessment/Take/"]'));
      await navigate(page, page.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }));
      takePath = new URL(page.url()).pathname + new URL(page.url()).search;
      count = await page.locator('[data-question]').count();
      assert.equal(count, 15);
      assert.equal(await page.locator('input[name="Answers[1].AssessmentQuestionId"]').count(), 1);
      assert.ok(await page.locator('.exam-stimulus-box').count() >= 3);
      await checkAudio(page);
    });
    await step('Second and last answers autosave with MVC indices; stale second tab preserves them', async () => {
      const second = await context.newPage();
      try {
        await go(second, takePath);
        saved.set(1, await saveChoice(page, 1));
        saved.set(count - 1, await saveChoice(page, count - 1));
        saved.set(0, await saveChoice(second, 0));
        await page.reload({ waitUntil: 'domcontentloaded' });
        for (const [index, value] of saved) await checkChoice(page, index, value);
        await second.reload({ waitUntil: 'domcontentloaded' });
        for (const [index, value] of saved) await checkChoice(second, index, value);
      } finally { await second.close(); }
    });
    await step('Submit grades the persisted answers; history has one attempt and hides answer keys', async () => {
      for (let index = 0; index < count; index++) if (!saved.has(index)) await saveChoice(page, index);
      await page.locator('[data-submit-button]').click();
      await page.locator('[data-submit-dialog][open]').waitFor();
      await navigate(page, page.locator('[data-confirm-submit]'));
      resultPath = new URL(page.url()).pathname;
      assert.match(resultPath, /^\/Assessment\/Result\//);
      assert.match((await page.locator('.assessment-score').innerText()).replaceAll(/\s/g, ''), /^100\/100$/);
      const html = await page.content();
      assert.doesNotMatch(html, /IsCorrect|CorrectOptionId|CorrectAnswer|SelectedOptionId|Use since with a starting point/);
      assert.equal(await page.locator('.answer-review, .answer-explanation, [data-question]').count(), 0);
      await screenshot(page, 'graded-private-result');
      await navigate(page, page.getByRole('link', { name: 'Lịch sử các lượt làm', exact: true }));
      assert.equal(await page.locator('.assessment-table tbody tr').count(), 1);
      assert.match(await page.locator('.assessment-table tbody').innerText(), /Đã chấm/);
      assert.equal(await page.locator(`a[href="${resultPath}"]`).count(), 1);
      await screenshot(page, 'attempt-history');
    });
    await step('Another Student cannot read the finalized attempt', async () => {
      await logout(page);
      await register(page);
      await go(page, resultPath, 404);
    });
  },
  async checkout(page) {
    let course;
    const orders = [];
    const createOrder = async () => {
      await navigate(page, page.getByRole('button', { name: /^Tiếp tục thanh toán/ }));
      const order = new URL(page.url()).pathname.split('/').pop();
      assert.match(order, /^[a-f0-9-]{36}$/i);
      assert.ok(!orders.includes(order), 'Terminal payment retry must create a fresh order.');
      orders.push(order);
      assert.match(await page.locator('.checkout-status').innerText(), /Đang chờ xử lý thanh toán/);
    };
    await step('Pending payment grants no paid learning access', async () => {
      await register(page);
      course = await detail(page, paidTitle);
      await navigate(page, page.getByRole('link', { name: /^Đăng ký & Thanh toán/ }));
      await createOrder();
      assert.equal((await page.request.get(`/Learning/Course/${course.id}`)).status(), 403);
      await screenshot(page, 'pending-order');
    });
    await step('Failed and cancelled orders keep access blocked and retry creates new orders', async () => {
      for (const [scenario, status] of [['failed', /Giao dịch thất bại/], ['cancelled', /Đã hủy giao dịch/]]) {
        await navigate(page, page.locator(`button[name="scenario"][value="${scenario}"]`));
        assert.match(await page.locator('.checkout-status').innerText(), status);
        assert.equal((await page.request.get(`/Learning/Course/${course.id}`)).status(), 403);
        await navigate(page, page.getByRole('link', { name: /^Thử thanh toán lại/ }));
        await createOrder();
      }
    });
    await step('Sandbox success opens paid lessons and history retains all three outcomes', async () => {
      await navigate(page, page.locator('button[name="scenario"][value="success"]'));
      assert.match(await page.locator('.checkout-status').innerText(), /Thanh toán thành công/);
      assert.equal(await page.locator('.checkout-status--expired').count(), 0);
      await screenshot(page, 'success-order');
      await navigate(page, page.getByRole('link', { name: /^Vào phòng học ngay/ }));
      assert.equal(new URL(page.url()).pathname, `/Learning/Course/${course.id}`);
      await page.locator('.course-outline summary').first().click();
      await navigate(page, page.locator('.course-outline a[href*="/Learning/Lesson/"]').first());
      assert.match(await page.locator('main').innerText(), /Good morning. Let me introduce our team/);
      await go(page, '/Checkout/History');
      assert.equal(await page.locator('.transaction-history-table tbody tr').count(), 3);
      for (const [index, status] of [[0, 'Thất bại'], [1, 'Đã hủy'], [2, 'Đã thanh toán']]) {
        const row = page.locator('tbody tr').filter({ hasText: orders[index] });
        assert.equal(await row.getByText(status, { exact: true }).count(), 1);
      }
      await screenshot(page, 'payment-history');
    });
  },
  async responsive(page) {
    let course, resultPath;
    await step('Prepare a fresh Student lesson and real practice result through the UI', async () => {
      await register(page);
      course = await enroll(page, freeTitle);
      await practicePath(page, 'Demo · Quick Check: Hello and welcome');
      await chooseFirstOptions(page);
      resultPath = await submitPractice(page);
    });
    for (const width of [390, 320]) {
      await step(`Mobile ${width}px dashboard, practice, course, lesson and result fit and sidebar restores focus`, async () => {
        await page.setViewportSize({ width, height: 844 });
        const paths = ['/Learning/Dashboard', '/Practice', `/Learning/Course/${course.id}`, course.lessonPaths[0], resultPath];
        for (let index = 0; index < paths.length; index++) {
          await go(page, paths[index]);
          const dimensions = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth,
            document: document.documentElement.scrollWidth }));
          assert.ok(dimensions.document <= dimensions.viewport + 1,
            `Document overflows at ${width}px on ${new URL(page.url()).pathname}: ${JSON.stringify(dimensions)}`);
          const toggle = page.locator('[data-sidebar-toggle]');
          await toggle.click();
          await page.locator('#student-sidebar[role="dialog"]').waitFor({ state: 'visible' });
          assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
          await page.keyboard.press('Escape');
          assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
          assert.equal(await toggle.evaluate(element => document.activeElement === element), true);
          await screenshot(page, `${width}px-view-${index + 1}`);
        }
      });
    }
  }
};

try {
  browser = await chromium.launch({ headless: true, args: ['--disable-dev-shm-usage'],
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {}) });
  for (const name of selected === 'all' ? groups : [selected]) {
    current = { name, outcome: 'Running', startedAtUtc: new Date().toISOString(), steps: [], javascriptErrors: [] };
    report.groups.push(current);
    const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 },
      timezoneId: 'Asia/Saigon', ignoreHTTPSErrors: appURL.protocol === 'https:' });
    await context.route('**/*', route => new URL(route.request().url()).origin === baseURL
      ? route.continue() : route.abort());
    context.on('page', page => {
      page.setDefaultTimeout(15000);
      page.setDefaultNavigationTimeout(30000);
      page.on('pageerror', error => {
        if (current.javascriptErrors.length < 20) current.javascriptErrors.push(privacy(error.message));
      });
    });
    try {
      const page = await context.newPage();
      await work[name](page, context);
      assert.deepEqual(current.javascriptErrors, [], 'Student pages must not emit uncaught JavaScript errors.');
      current.outcome = 'Passed';
    } catch (error) {
      current.outcome = 'Failed';
      current.error = privacy(error.message ?? error);
      for (const [index, page] of context.pages().entries()) {
        try { await screenshot(page, `failure-${index + 1}`); }
        catch { /* Preserve the original failure if the page already closed. */ }
      }
      process.exitCode = 1;
      console.error(`${name}: Failed: ${privacy(error.message ?? error)}`);
    } finally {
      current.finishedAtUtc = new Date().toISOString();
      await context.close();
    }
  }
} catch (error) {
  report.error = privacy(error.message ?? error);
  process.exitCode = 1;
} finally {
  if (browser) await browser.close();
  report.finishedAtUtc = new Date().toISOString();
  report.sourceAfter = await sourceState();
  report.sourceUnchanged = before.digest === report.sourceAfter.digest && before.head === report.sourceAfter.head;
  if (!report.sourceUnchanged) {
    report.error = 'Relevant source changed during UAT; rerun against a stable source tree.';
    process.exitCode = 1;
  }
  report.outcome = process.exitCode ? 'Failed' : 'Passed';
  await writeFile(join(output, `student-flow-${selected}-results.json`), privacy(JSON.stringify(report, null, 2)) + '\n');
  console.log(`Student ${selected}: ${report.outcome}. Report: ${join(output, `student-flow-${selected}-results.json`)}`);
}

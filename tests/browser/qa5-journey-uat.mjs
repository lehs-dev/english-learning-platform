import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { resolve, join } from 'node:path';

// UAT trên web thật. Chỉ setup accounts/Course nền bằng demo seeder; toàn bộ đề,
// câu hỏi, audio, câu trả lời và kết quả dưới đây đều tạo qua giao diện, không SQL.
assert.equal(process.env.ELP_QA5_UAT_ISOLATED, 'true', 'Use scripts/qa5-browser-uat.ps1 for a disposable environment.');
const baseURL = process.env.ELP_UAT_URL;
assert.equal(baseURL, 'http://127.0.0.1:18085', 'QA5 only targets its private app port, never the working demo.');
const password = process.env.LearningDemo__Password;
assert.ok(password);
const redact = value => String(value).split(password).join('[REDACTED]');
const output = resolve(process.env.ELP_UAT_RESULTS);
const root = resolve('../..');
const runId = `QA5-${randomUUID().slice(0, 8)}`;
const cases = [], javascriptErrors = [], contexts = [], screenshots = [];
const startedAtUtc = new Date().toISOString();
let failure, browser, currentPage;
const evidence = { runId, practiceId: null, assessmentId: null, attempts: [] };

// Hash cả tracked và untracked source để không gán SHA base cho code chưa commit.
async function sourceState() {
  const git = args => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
  const files = [...new Set(git(['ls-files', '--cached', '--others', '--exclude-standard', '-z'])
    .split('\0').filter(name => /^(src\/|tests\/browser\/|scripts\/qa5-browser-uat\.ps1$)/.test(name)))].sort();
  const hash = createHash('sha256');
  for (const file of files) hash.update(file + '\0').update(await readFile(join(root, file)));
  return { baseCommit: git(['rev-parse', 'HEAD']), workingTreeClean: !git(['status', '--porcelain']), sourceDigest: hash.digest('hex') };
}
const source = await sourceState();
await mkdir(output, { recursive: true });
async function step(id, actor, action) {
  const start = new Date().toISOString();
  try {
    await action();
    cases.push({ id, actor, outcome: 'Passed', startedAtUtc: start, finishedAtUtc: new Date().toISOString() });
    console.log(`${id}: Passed (${actor})`);
  } catch (error) {
    const message = redact(error.message ?? error);
    cases.push({ id, actor, outcome: 'Failed', startedAtUtc: start, error: message });
    throw new Error(message);
  }
}
async function pageFor(actor) {
  const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 1000 } });
  contexts.push(context);
  await context.route('**/*', route => new URL(route.request().url()).origin === baseURL ? route.continue() : route.abort());
  const page = await context.newPage();
  page.on('pageerror', error => javascriptErrors.push(redact(error.message)));
  page.on('dialog', dialog => dialog.accept());
  page.setDefaultTimeout(15000);
  await login(page, actor);
  return page;
}
async function login(page, actor) {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(`${actor}@demo.example.test`);
  await page.locator('#Password').fill(password);
  await navigateSubmit(page, page.locator('form.account-form button[type=submit]'));
  assert.ok(!new URL(page.url()).pathname.includes('/Account/Login'));
}
async function navigateSubmit(page, button) {
  await Promise.all([page.waitForNavigation({ waitUntil: 'domcontentloaded' }), button.click()]);
}
async function capture(page, name) {
  const file = `${name}.png`;
  await page.screenshot({ path: join(output, file), fullPage: true, animations: 'disabled' });
  screenshots.push(file);
}
async function responsive(page, name) {
  currentPage = page;
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
    await page.waitForFunction(() => {
      const sidebar = document.getElementById('studio-sidebar');
      return !sidebar || innerWidth > 1024 || (sidebar.getAttribute('aria-hidden') === 'true' && sidebar.getBoundingClientRect().right <= 1);
    });
    assert.ok(await page.locator('#main-content').isVisible(), `${name}: content visible at ${width}px`);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name}: document overflow at ${width}px`);
    await capture(page, `${name}-${width}`);
    if (width === 390 && await page.locator('.assessment-table-wrap').count()) {
      const table = page.locator('.assessment-table-wrap').first();
      assert.equal(await table.getAttribute('tabindex'), '0', 'Overflow table must support keyboard focus.');
      assert.ok(await table.evaluate(element => element.scrollWidth > element.clientWidth), 'Mobile table keeps readable columns.');
      await table.evaluate(element => { element.scrollLeft = element.scrollWidth; });
      assert.ok(await table.evaluate(element => element.scrollLeft > 0), 'Mobile table must really scroll.');
      await capture(page, `${name}-390-table-end`);
    }
    if (width === 390 && await page.locator('[data-sidebar-toggle]').isVisible()) {
      await page.locator('[data-sidebar-toggle]').click();
      await page.locator('[data-sidebar-toggle][aria-expanded=true]').waitFor();
      await page.keyboard.press('Escape');
      await page.locator('[data-sidebar-toggle][aria-expanded=false]').waitFor();
    }
  }
  await page.setViewportSize({ width: 1440, height: 1000 });
}
async function playAudio(page) {
  const player = page.locator('audio').first();
  await player.evaluate(async audio => { await audio.play(); });
  await page.waitForFunction(() => { const audio = document.querySelector('audio'); return audio && audio.currentTime > 0.05 && audio.readyState >= 2; });
  await player.evaluate(audio => audio.pause());
}
function wav() {
  const samples = 8000, buffer = Buffer.alloc(44 + samples * 2);
  buffer.write('RIFF', 0); buffer.writeUInt32LE(buffer.length - 8, 4); buffer.write('WAVEfmt ', 8);
  buffer.writeUInt32LE(16, 16); buffer.writeUInt16LE(1, 20); buffer.writeUInt16LE(1, 22);
  buffer.writeUInt32LE(8000, 24); buffer.writeUInt32LE(16000, 28); buffer.writeUInt16LE(2, 32); buffer.writeUInt16LE(16, 34);
  buffer.write('data', 36); buffer.writeUInt32LE(samples * 2, 40);
  for (let i = 0; i < samples; i++) buffer.writeInt16LE(Math.round(4000 * Math.sin(2 * Math.PI * 220 * i / 8000)), 44 + i * 2);
  return buffer;
}
const passage = `${runId}: Alex visits the library on Saturday. Alex borrows an English book.`;
const prompts = ['Who visits the library?', 'When does Alex visit?', 'What does Alex borrow?', 'What do you hear in the clip?', 'Does the clip contain spoken words?'];
const right = ['Alex', 'Saturday', 'An English book', 'A continuous tone', 'No spoken words'];
const wrong = ['Pat', 'Monday', 'A football', 'Silence', 'A spoken sentence'];
const questionIds = [], optionIds = [];
let courseId, lessonId, passageId, audioId, teacher, student, other;

async function preview(page, kind, id, status) {
  if (kind === 'Practice') {
    await page.goto(`/TeacherPracticeQuestions/Index/${id}`);
    await page.getByRole('link', { name: 'Preview Practice đã lưu', exact: true }).click();
    await page.waitForURL(`/TeacherPracticePreview/Index/${id}`);
  } else await page.goto(`/Teacher${kind}Preview/Index/${id}`);
  assert.ok((await page.locator('#main-content').innerText()).includes(status));
  assert.equal(await page.locator('.exam-question-card').count(), 5);
  assert.ok((await page.locator('#main-content').innerText()).includes(passage));
  assert.equal(await page.locator('audio').count(), 2);
  await playAudio(page);
  await page.locator('.exam-option-card').first().click();
  assert.equal(await page.locator('input[name="PreviewAnswers[0]"]:checked').count(), 1);
  await page.reload();
  assert.equal(await page.locator('input[name="PreviewAnswers[0]"]:checked').count(), 0);
  assert.equal(await page.locator('[data-practice-form], [data-assessment-form]').count(), 0);
  await responsive(page, `${kind.toLowerCase()}-preview-${status.toLowerCase()}`);
}
async function answerAssessment(page, correctIndices) {
  for (let i = 0; i < 5; i++) {
    const question = page.locator('[data-question]').nth(i);
    const label = question.locator('.exam-option-card').filter({ hasText: correctIndices.includes(i) ? right[i] : wrong[i] }).first();
    const [saved] = await Promise.all([
      page.waitForResponse(response => response.url().includes('/Assessment/Save/') && response.request().method() === 'POST'), label.click()
    ]);
    assert.equal(saved.status(), 200);
    assert.equal((await saved.json()).saved, true);
  }
}
// Resume có thể render ngay tại URL không có query attemptId; form POST mới xác định lượt thật.
async function takingAttempt(page) {
  return new URL(await page.locator('[data-assessment-form]').getAttribute('action'), baseURL).pathname.split('/').pop();
}
async function safeResult(page) {
  const html = await page.content();
  assert.equal(await page.locator('.assessment-table tbody tr').count(), 1, 'Only Reading (3 questions) has enough evidence; Listening (2) stays hidden.');
  assert.doesNotMatch(html, /QA5_PRIVATE_NOTE|SelectedOptionId|IsCorrect/);
  for (const id of optionIds) assert.ok(!html.includes(id), 'Result must not disclose individual option IDs.');
  for (const answer of right) assert.ok(!(await page.locator('#main-content').innerText()).includes(answer), 'Result must not disclose answer text.');
}
try {
  browser = await chromium.launch({ headless: true, args: ['--autoplay-policy=no-user-gesture-required'] });
  teacher = await pageFor('teacher1'); student = await pageFor('student1'); other = await pageFor('teacher2');
  currentPage = teacher;
  await step('QA5-01-stimuli-and-five-owned-questions', 'Teacher', async () => {
    await teacher.goto('/TeacherCourses');
    const courseLink = teacher.locator('.course-card').filter({ hasText: 'Demo · Everyday English' }).locator('a[href*="/TeacherCourses/Manage/"]').first();
    courseId = (await courseLink.getAttribute('href')).split('/').pop();
    await teacher.goto('/TeacherStimuli/Create');
    await teacher.locator('#StimulusType_Passage').check();
    await teacher.locator('#Title').fill(`${runId} passage`);
    await navigateSubmit(teacher, teacher.locator('form.editor-form button[type=submit]'));
    assert.ok(await teacher.locator('.validation-summary-errors').isVisible(), 'Empty passage must show a server error.');
    await responsive(teacher, 'stimulus-validation');
    await teacher.locator('#ContentText').fill(passage);
    await navigateSubmit(teacher, teacher.locator('form.editor-form button[type=submit]'));
    // Sau tạo, form chỉ liệt kê ngữ liệu; người dùng vẫn phải tự chọn dropdown.
    passageId = await teacher.locator('#StimulusId option').filter({ hasText: `${runId} passage` }).getAttribute('value');
    assert.ok(passageId);
    await teacher.goto('/TeacherStimuli/Create');
    await teacher.locator('#StimulusType_Audio').check();
    await teacher.locator('#Title').fill(`${runId} tone audio`);
    await teacher.locator('#audioFile').setInputFiles({ name: 'qa5-tone.wav', mimeType: 'audio/wav', buffer: wav() });
    await navigateSubmit(teacher, teacher.locator('form.editor-form button[type=submit]'));
    audioId = await teacher.locator('#StimulusId option').filter({ hasText: `${runId} tone audio` }).getAttribute('value');
    assert.ok(audioId && audioId !== passageId);
    for (let i = 0; i < 5; i++) {
      await teacher.goto('/TeacherQuestionBank/Create');
      await teacher.locator('#Content').fill(`${runId} Q${i + 1}: ${prompts[i]}`);
      await teacher.locator('#Level').selectOption('A1');
      await teacher.locator('#PrimarySkill').selectOption(i < 3 ? '3' : '4');
      await teacher.locator('#StimulusId').selectOption(i < 3 ? passageId : audioId);
      await teacher.locator('#Explanation').fill(`QA5_PRIVATE_NOTE_${i}: ${right[i]} is the answer for the supplied stimulus.`);
      const options = teacher.locator('[data-option-row] input[name$=".Content"]');
      for (let j = 0; j < await options.count(); j++) await options.nth(j).fill(j === 0 ? right[i] : j === 1 ? wrong[i] : `${wrong[i]} alternative ${j}`);
      await teacher.locator('[name=CorrectOptionIndex][value="0"]').check();
      await navigateSubmit(teacher, teacher.locator('form.question-form button[type=submit]'));
      assert.equal(new URL(teacher.url()).pathname, '/TeacherQuestionBank');
    }
    await teacher.locator('#Search').fill(runId);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Tìm / lọc', exact: true }));
    const rows = teacher.locator('[data-question-row]');
    assert.equal(await rows.count(), 5);
    for (let i = 0; i < 5; i++) {
      const row = rows.filter({ hasText: `${runId} Q${i + 1}:` });
      const link = row.locator('a[href*="/TeacherQuestionBank/Details/"]');
      questionIds.push((await link.getAttribute('href')).split('/').pop());
    }
    await responsive(teacher, 'question-bank');
    for (const i of [0, 3]) {
      await teacher.goto(`/TeacherQuestionBank/Details/${questionIds[i]}`);
      if (i === 3) await playAudio(teacher);
      else assert.ok((await teacher.locator('#main-content').innerText()).includes(passage));
      await responsive(teacher, `question-detail-${i < 3 ? 'passage' : 'audio'}`);
    }
  });

  await step('QA5-02-practice-create-select-preview-publish', 'Teacher', async () => {
    await teacher.goto(`/TeacherPractices/Create?courseId=${courseId}`);
    await teacher.locator('#Input_Title').fill(`${runId} Practice`);
    await teacher.locator('#Input_Description').fill('Practice feedback and latest-only UAT.');
    lessonId = await teacher.locator('#Input_LessonId option').filter({ hasText: 'Hello and welcome' }).getAttribute('value');
    await teacher.locator('#Input_LessonId').selectOption(lessonId);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Lưu bài luyện tập nháp', exact: true }));
    await teacher.goto(`/TeacherPractices?Search=${runId}`);
    const link = teacher.locator('tbody tr').filter({ hasText: `${runId} Practice` }).locator('a[href*="/TeacherPracticeQuestions/Index/"]');
    evidence.practiceId = (await link.getAttribute('href')).split('/').pop();
    await teacher.goto(await link.getAttribute('href'));
    await teacher.locator('#Search').fill(runId);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Tìm / lọc', exact: true }));
    for (const id of questionIds) await teacher.locator(`[data-add-question][data-question-id="${id}"]`).click();
    await navigateSubmit(teacher, teacher.locator('[data-save-button]'));
    assert.deepEqual(await teacher.locator('[data-selected-question] input[name=QuestionIds]').evaluateAll(inputs => inputs.map(input => input.value)), questionIds);
    await responsive(teacher, 'practice-editor');
    await preview(teacher, 'Practice', evidence.practiceId, 'Draft');
    const foreign = await other.goto(`/TeacherPracticePreview/Index/${evidence.practiceId}`);
    assert.equal(foreign.status(), 404);
    await teacher.goto(`/TeacherPracticeQuestions/Index/${evidence.practiceId}`);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Publish Practice', exact: true }));
    assert.ok((await teacher.locator('#main-content').innerText()).includes('Published'));
    await preview(teacher, 'Practice', evidence.practiceId, 'Published');
  });

  await step('QA5-03-practice-retake-feedback-and-latest-result', 'Student', async () => {
    currentPage = student;
    for (const correctCount of [3, 5]) {
      await student.goto(`/Practice/Take/${evidence.practiceId}`);
      assert.equal(await student.locator('[data-practice-question]').count(), 5);
      await playAudio(student);
      for (let i = 0; i < 5; i++) await student.locator('[data-practice-question]').nth(i).locator('.exam-option-card')
        .filter({ hasText: i < correctCount ? right[i] : wrong[i] }).first().click();
      if (correctCount === 3) await responsive(student, 'practice-taking');
      await navigateSubmit(student, student.getByRole('button', { name: 'Nộp bài & Xem giải thích', exact: true }));
      assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), `${correctCount * 20}/100`);
      assert.equal(await student.locator('.answer-review').count(), 5);
      assert.equal(await student.locator('.answer-explanation').count(), 5);
      assert.ok((await student.locator('#main-content').innerText()).includes('QA5_PRIVATE_NOTE_0'));
      await responsive(student, `practice-result-${correctCount * 20}`);
    }
    await student.goto(`/Practice/Result/${evidence.practiceId}`);
    assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), '100/100');
    await student.goto('/Learning/Dashboard');
    const practice = student.locator('#dashboard-practice-results').filter({ hasText: `${runId} Practice` });
    assert.equal(await practice.count(), 1, 'Only the latest Practice result is listed.');
    assert.match(await practice.innerText(), /100%/);
    assert.equal(await practice.locator(`a[href="/Practice/Result/${evidence.practiceId}"]`).count(), 1);
  });

  await step('QA5-04-assessment-draft-validation-points-preview-publish', 'Teacher', async () => {
    currentPage = teacher;
    await teacher.goto('/TeacherAssessments/Edit');
    await teacher.locator('#Input_Title').fill(`${runId} Assessment`);
    await teacher.locator('#Input_Description').fill('Weighted 60/85/70 history UAT.');
    await teacher.locator('#Input_AssessmentType').selectOption('PracticeExam');
    await teacher.locator('#Input_DurationMinutes').fill('20');
    await teacher.locator('#Input_MaxAttempts').fill('3');
    await teacher.locator('#Input_PassingScore').fill('80');
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Lưu cấu hình', exact: true }));
    evidence.assessmentId = new URL(teacher.url()).pathname.split('/').pop();
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Publish đề đã lưu', exact: true }));
    assert.ok(await teacher.locator('.validation-summary-errors').isVisible(), 'Empty saved paper must not Publish.');
    await responsive(teacher, 'assessment-publish-validation');
    await teacher.goto(`/TeacherAssessmentQuestions/Index/${evidence.assessmentId}`);
    await teacher.locator('#Search').fill(runId);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Tìm / lọc', exact: true }));
    for (const id of questionIds) await teacher.locator(`[data-add][data-id="${id}"]`).click();
    const points = [1, 3, 8, 5, 3];
    for (let i = 0; i < 5; i++) await teacher.locator('[data-selected-row] [data-points]').nth(i).fill(String(points[i]));
    await navigateSubmit(teacher, teacher.locator('[data-save]'));
    assert.deepEqual(await teacher.locator('[data-selected-row] [data-points]').evaluateAll(inputs => inputs.map(input => Number(input.value))), points);
    await responsive(teacher, 'assessment-question-points');
    await preview(teacher, 'Assessment', evidence.assessmentId, 'Draft');
    await teacher.goto(`/TeacherAssessmentScope/Edit/${evidence.assessmentId}`);
    await teacher.locator('#Input_CourseId').selectOption(courseId);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Lưu phạm vi', exact: true }));
    await teacher.goto(`/TeacherAssessments/Edit/${evidence.assessmentId}`);
    await navigateSubmit(teacher, teacher.getByRole('button', { name: 'Publish đề đã lưu', exact: true }));
    assert.equal(new URL(teacher.url()).pathname, '/TeacherAssessments');
    await preview(teacher, 'Assessment', evidence.assessmentId, 'Published');
    await student.goto(`/Assessment/Take/${evidence.assessmentId}`);
    assert.equal(await student.locator('[data-assessment-form]').count(), 0, 'Teacher preview did not Start a Student attempt.');
    assert.equal(await student.locator(`a[href*="/Assessment/Result/"]`).count(), 0);
  });

  await step('QA5-05-assessment-save-close-login-resume-submit-three-attempts', 'Student', async () => {
    currentPage = student;
    const patterns = [[0, 1, 2], [0, 2, 3, 4], [1, 2, 4]];
    const scores = [60, 85, 70];
    for (let turn = 0; turn < 3; turn++) {
      await student.goto(`/Assessment/Take/${evidence.assessmentId}`);
      await navigateSubmit(student, student.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }));
      await student.locator('[data-assessment-form]').waitFor();
      const attemptId = new URL(student.url()).searchParams.get('attemptId');
      const deadline = await student.locator('[data-assessment-clock]').getAttribute('data-deadline');
      assert.ok(attemptId && deadline);
      if (turn === 0) optionIds.push(...await student.locator('[data-question] .exam-option-radio[value]:not([value=""])').evaluateAll(inputs => inputs.map(input => input.value)));
      await answerAssessment(student, patterns[turn]);
      assert.doesNotMatch(await student.content(), /QA5_PRIVATE_NOTE|IsCorrect/);
      if (turn === 0) {
        await playAudio(student);
        await student.locator('[data-question-link="5"]').click();
        assert.equal(await takingAttempt(student), attemptId);
        await responsive(student, 'assessment-taking-saved');
        await student.close();
        student = await pageFor('student1'); // Phiên mới thật, không tái dùng storageState/cookie cũ.
        await student.goto(`/Assessment/Take/${evidence.assessmentId}`);
        await student.locator('[data-assessment-form]').waitFor();
        assert.equal(await takingAttempt(student), attemptId);
        assert.equal(await student.locator('[data-assessment-clock]').getAttribute('data-deadline'), deadline);
        await student.reload();
        for (let i = 0; i < 5; i++) assert.ok(await student.locator('[data-question]').nth(i)
          .locator('.exam-option-card').filter({ hasText: patterns[turn].includes(i) ? right[i] : wrong[i] }).first().locator('input').isChecked());
        await responsive(student, 'assessment-resumed');
      }
      await student.locator('[data-submit-button]').click();
      await student.locator('[data-submit-dialog][open]').waitFor();
      if (turn === 0) {
        await student.locator('[data-cancel-submit]').click();
        assert.equal(await takingAttempt(student), attemptId);
        await student.locator('[data-submit-button]').click();
      }
      await navigateSubmit(student, student.locator('[data-confirm-submit]'));
      assert.equal(new URL(student.url()).pathname, `/Assessment/Result/${attemptId}`);
      assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), `${scores[turn]}/100`);
      await safeResult(student);
      evidence.attempts.push({ attemptId, deadline, score: scores[turn] });
      await responsive(student, `assessment-result-${scores[turn]}`);
    }
  });

  await step('QA5-06-history-representative-dashboard-profile-and-safe-results', 'Student', async () => {
    currentPage = student;
    await student.goto(`/Assessment/History/${evidence.assessmentId}`);
    assert.equal(await student.locator('tbody tr').count(), 3);
    for (const attempt of evidence.attempts) {
      const row = student.locator('tbody tr').filter({ has: student.locator(`a[href="/Assessment/Result/${attempt.attemptId}"]`) });
      assert.equal((await row.locator('td').nth(3).innerText()).trim(), String(attempt.score));
    }
    await responsive(student, 'assessment-history');
    const highest = evidence.attempts[1].attemptId;
    for (const [path, name] of [['/Learning/Dashboard', 'dashboard'], ['/Profile', 'profile']]) {
      await student.goto(path);
      const result = student.locator(`[data-representative-attempt="${highest}"]`);
      assert.equal(await result.count(), 1);
      assert.match(await result.innerText(), /85%/);
      assert.match(await result.innerText(), /75%/); // Reading của lượt 85%, không phải 91,67% của lượt mới 70%.
      assert.equal(await student.locator(`[data-representative-attempt="${evidence.attempts[2].attemptId}"]`).count(), 0);
      await responsive(student, name);
      await result.locator(`a[href="/Assessment/Result/${highest}"]`).first().click();
      await student.waitForURL(`/Assessment/Result/${highest}`);
      await safeResult(student);
    }
    assert.deepEqual(javascriptErrors, []);
    assert.deepEqual(await sourceState(), source, 'Source changed while UAT was running.');
  });
} catch (error) {
  failure = redact(error.message ?? error);
  console.error(`QA5 failed: ${failure}`);
  if (currentPage && !currentPage.isClosed()) await capture(currentPage, 'failure-page').catch(() => {});
  process.exitCode = 1;
} finally {
  await writeFile(join(output, 'qa5-results.json'), redact(JSON.stringify({ kind: 'Live Chromium UI + isolated ASP.NET/SQL; no business SQL edits',
    ...source, startedAtUtc, finishedAtUtc: new Date().toISOString(), timezone: 'Asia/Bangkok', baseURL,
    outcome: failure ? 'Failed' : 'Passed', cases, javascriptErrors, screenshots, evidence,
    ...(failure ? { error: failure } : {}) }, null, 2)) + '\n');
  await Promise.all(contexts.map(context => context.close().catch(() => {})));
  await browser?.close();
}

import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const baseURL = process.env.ELP_UAT_URL;
const password = process.env.LearningDemo__Password;
assert.ok(baseURL && password, 'Set ELP_UAT_URL to an isolated seeded app and LearningDemo__Password to its demo password.');
const output = process.env.ELP_UAT_RESULTS ?? 'artifacts/estudy2-uat';
await mkdir(output, { recursive: true });

const marker = `Estudy UAT ${Date.now()}`;
const assessmentTitle = `${marker} bài kiểm tra`;
const deckTitle = `${marker} thẻ từ`;
const definitions = [
    { type: '1', title: 'Choose the adjective in “a quiet room”.', options: ['quiet', 'room'] },
    { type: '2', title: 'I ___ here last year.', accepted: ['lived', 'was living'] },
    { type: '3', title: 'Match the animals to their Vietnamese names.', items: [['Dog', 'Chó'], ['Cat', 'Mèo']] },
    { type: '4', title: 'Put the instructions in the right order.', items: [['Boil water'], ['Add tea'], ['Serve']] },
    { type: '1', title: 'Choose the opposite of late.', options: ['early', 'later'] }
];

const evidence = {};
const browser = await chromium.launch({ headless: true, args: ['--disable-gpu'] });
const errors = [];
const submit = (page, button) => Promise.all([page.waitForNavigation(), button.click()]);
const pathname = page => new URL(page.url()).pathname;
const lastSegment = url => new URL(url, baseURL).pathname.split('/').at(-1);

async function login(account) {
    const context = await browser.newContext({ baseURL, viewport: { width: 1360, height: 900 } });
    const page = await context.newPage();
    page.setDefaultTimeout(10000);
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/Account/Login');
    await page.locator('#Email').fill(`${account}@demo.example.test`);
    await page.locator('#Password').fill(password);
    await submit(page, page.locator('form.account-form button[type=submit]'));
    assert.notEqual(pathname(page), '/Account/Login', `${account} login`);
    return page;
}

async function captureMobile(page, filename) {
    await page.setViewportSize({ width: 390, height: 844 });
    const widths = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth]);
    assert.ok(widths[0] <= widths[1], `Horizontal overflow at 390px on ${pathname(page)} (${widths.join(' > ')})`);
    await page.screenshot({ path: `${output}/${filename}.png`, animations: 'disabled' });
    await page.setViewportSize({ width: 1360, height: 900 });
}

async function answer(question, definition, correct = true) {
    if (definition.type === '1') {
        await question.locator('.exam-option-card').filter({ hasText: definition.options[correct ? 0 : 1] }).first().click();
        return;
    }
    if (definition.type === '2') {
        await question.locator('input[type=text]').fill('  LIVED  ');
        return;
    }
    const selects = question.locator('[data-structured-select]');
    for (let index = 0; index < await selects.count(); index++) {
        const select = selects.nth(index);
        if (definition.type === '3') {
            const left = (await select.locator('xpath=../label').textContent()).trim();
            const match = definition.items.findIndex(pair => pair[0] === left);
            await select.selectOption({ label: definition.items[correct ? match : 1 - match][1] });
        } else {
            await select.selectOption({ label: definition.items[index][0] });
        }
    }
    assert.ok(await question.locator('[data-structured-answer]').inputValue(), 'The browser serialized the complete response');
}

async function submitAssessment(page) {
    await page.locator('[data-submit-button]').click();
    await page.locator('[data-submit-dialog][open]').waitFor();
    await submit(page, page.locator('[data-confirm-submit]'));
    assert.match(pathname(page), /\/Assessment\/Result\//);
}

try {
    const teacher = await login('teacher1');
    const student = await login('student1');
    const unrelatedStudent = await login('student2');
    const unrelatedTeacher = await login('teacher2');

    await unrelatedStudent.goto('/LearningPath');
    assert.match(await unrelatedStudent.locator('main').innerText(), /Hãy đánh giá năng lực trước/);

    await teacher.goto('/TeacherFlashcards/Create');
    const courseId = await teacher.locator('#Input_CourseId option').filter({ hasText: 'Demo · English Starter A1' }).getAttribute('value');
    assert.ok(courseId);

    for (const [index, definition] of definitions.entries()) {
        await teacher.goto('/TeacherQuestionBank/Create');
        const form = teacher.locator('#question-editor-form');
        await form.locator('#QuestionType').selectOption(definition.type);
        await form.locator('#Content').fill(`${marker} Q${index + 1}. ${definition.title}`);
        await form.locator('#Level').selectOption('A1');
        await form.locator('#PrimarySkill').selectOption('3');
        const passage = form.locator('#StimulusId option').filter({ hasText: 'Reading passage' }).first();
        await form.locator('#StimulusId').selectOption(await passage.getAttribute('value'));
        await form.locator('#Explanation').fill(`ESTUDY_PRIVATE_NOTE_${index}`);
        if (definition.options) {
            for (const [option, value] of definition.options.entries()) {
                await form.locator(`[name="Options[${option}].Content"]`).fill(value);
            }
            await form.locator('[name=CorrectOptionIndex][value="0"]').check();
        } else if (definition.accepted) {
            await form.locator('[name="AcceptedAnswers[0]"]').fill(definition.accepted[0]);
            await form.locator('[data-add-accepted]').click();
            await form.locator('[name="AcceptedAnswers[1]"]').fill(definition.accepted[1]);
        } else {
            const rows = form.locator('[data-structured-row]');
            for (const [row, [left, right]] of definition.items.entries()) {
                if (row >= await rows.count()) await form.locator('[data-add-structured]').click();
                await rows.nth(row).locator('[data-structured-left]').fill(left);
                if (right) await rows.nth(row).locator('[data-structured-right]').fill(right);
            }
        }
        await submit(teacher, form.locator('button[type=submit]'));
        assert.equal(pathname(teacher), '/TeacherQuestionBank', `Created type ${definition.type}`);
    }

    await teacher.goto(`/TeacherQuestionBank?Search=${encodeURIComponent(marker)}`);
    const questionIds = [];
    for (let index = 0; index < definitions.length; index++) {
        const row = teacher.locator('[data-question-row]').filter({ hasText: `${marker} Q${index + 1}.` });
        questionIds.push(lastSegment(await row.locator('a[href*="/TeacherQuestionBank/Details/"]').getAttribute('href')));
    }
    evidence.questionTypesAuthored = ['MCQ', 'FillBlank', 'Matching', 'Ordering'];

    await teacher.goto(`/TeacherPractices/Create?courseId=${courseId}`);
    await teacher.locator('#Input_Title').fill(`${marker} Practice`);
    const moduleId = await teacher.locator('#Input_ModuleId option[value]:not([value=""])').first().getAttribute('value');
    await teacher.locator('#Input_ModuleId').selectOption(moduleId);
    await submit(teacher, teacher.locator('form.practice-create-form button[type=submit]'));
    assert.match(pathname(teacher), /\/TeacherPracticeQuestions\//);
    const practiceId = lastSegment(teacher.url());
    await teacher.goto(`/TeacherPracticeQuestions/Index/${practiceId}?Search=${encodeURIComponent(marker)}`);
    for (const id of questionIds) await teacher.locator(`[data-add-question][data-question-id="${id}"]`).click();
    await submit(teacher, teacher.locator('[data-save-button]'));
    await submit(teacher, teacher.locator(`form[action*="/TeacherPracticeQuestions/Publish/"] button[type="submit"]`));

    const achievementXp = async page => {
        await page.goto('/Achievements');
        return Number((await page.locator('.estudy-stats article h2').first().innerText()).match(/\d+/)[0]);
    };
    const beforePractice = await achievementXp(student);
    for (let repetition = 0; repetition < 2; repetition++) {
        await student.goto(`/Practice/Take/${practiceId}`);
        const questions = student.locator('[data-practice-question]');
        assert.equal(await questions.count(), definitions.length);
        assert.doesNotMatch(await student.content(), /was living|ESTUDY_PRIVATE_NOTE/);
        if (repetition === 0) {
            const columns = await student.locator('.practice-reading-grid').evaluate(form => getComputedStyle(form).gridTemplateColumns.split(' ').length);
            assert.equal(columns, 2);
            await student.setViewportSize({ width: 390, height: 844 });
            await student.locator('#practice-passages summary').click();
            assert.equal(await student.locator('#practice-passages').getAttribute('open'), null);
            await questions.nth(1).locator('a[href="#practice-passages"]').click();
            assert.equal(new URL(student.url()).hash, '#practice-passages');
            assert.notEqual(await student.locator('#practice-passages').getAttribute('open'), null);
            await captureMobile(student, 'practice-reading-390');
        }
        for (const [index, definition] of definitions.entries()) {
            await student.locator(`[data-practice-link="${index + 1}"]`).click();
            await answer(questions.nth(index), definition);
        }
        await submit(student, student.locator('.exam-submit-btn'));
        assert.match(pathname(student), /\/Practice\/Result\//);
        assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), '100/100');
        assert.match(await student.locator('main').innerText(), /was living|ESTUDY_PRIVATE_NOTE/);
        assert.equal(await achievementXp(student), beforePractice + 15, 'Practice XP once per Practice per day');
    }
    evidence.practice = { score: 100, xpAfterRetake: 15, readingViewport: 390 };

    await teacher.goto(`/TeacherAssessments/Edit?courseId=${courseId}`);
    await teacher.locator('#Input_Title').fill(assessmentTitle);
    await teacher.locator('#Input_AssessmentType').selectOption('PracticeExam');
    await teacher.locator('#Input_DurationMinutes').fill('20');
    await teacher.locator('#Input_MaxAttempts').fill('2');
    await teacher.locator('#Input_PassingScore').fill('80');
    await submit(teacher, teacher.locator('[data-configuration-form] button[type=submit]'));
    assert.match(pathname(teacher), /\/TeacherAssessments\/Edit\//);
    const assessmentId = lastSegment(teacher.url());
    await teacher.goto(`/TeacherAssessmentQuestions/Index/${assessmentId}?Search=${encodeURIComponent(marker)}`);
    for (const id of questionIds) await teacher.locator(`[data-add][data-id="${id}"]`).click();
    for (const [index, points] of [1, 2, 3, 4, 5].entries()) {
        await teacher.locator('[data-selected-row] [data-points]').nth(index).fill(String(points));
    }
    await submit(teacher, teacher.locator('[data-save]'));
    await teacher.goto(`/TeacherAssessments/Edit/${assessmentId}`);
    await submit(teacher, teacher.locator('[data-publication-form] button[type=submit]'));

    await student.goto(`/Assessment/Take/${assessmentId}`);
    await submit(student, student.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }));
    const sheet = student.locator('[data-assessment-form]');
    await sheet.waitFor();
    assert.doesNotMatch(await student.content(), /was living|ESTUDY_PRIVATE_NOTE/);
    for (const [index, definition] of definitions.entries()) await answer(sheet.locator('[data-question]').nth(index), definition, index !== 2);
    await student.waitForFunction(() => /Đã lưu/i.test(document.querySelector('[data-save-status]')?.textContent ?? ''));
    await student.reload();
    assert.equal(await sheet.locator('[data-structured-answer]').count(), 2);
    assert.ok((await sheet.locator('[data-structured-answer]').evaluateAll(inputs => inputs.map(input => input.value))).every(Boolean));
    assert.equal(await sheet.locator('[data-assessment-text]').inputValue(), '  LIVED  ');
    await submitAssessment(student);
    assert.equal((await student.locator('.assessment-score').innerText()).replace(/\s/g, ''), '80/100');
    await student.screenshot({ path: `${output}/assessment-weighted-result.png`, animations: 'disabled' });
    evidence.assessment = { score: 80, earnedPoints: 12, availablePoints: 15, resumed: true };

    await student.goto('/Assessment');
    const grammar = student.locator('article').filter({
        has: student.getByRole('heading', { name: 'Demo · Skill Assessment Grammar', exact: true })
    });
    await grammar.getByRole('link', { name: /Xem thông tin/ }).click();
    await submit(student, student.getByRole('button', { name: 'Bắt đầu làm bài', exact: true }));
    await submitAssessment(student);
    await student.goto('/LearningPath');
    const duration = student.locator('#learning-path-duration');
    assert.deepEqual(await duration.locator('option:not([disabled])').evaluateAll(options => options.map(o => o.value)), ['7', '14', '30']);
    await duration.selectOption('30');
    await submit(student, student.locator('.learning-path-duration button[type=submit]'));
    assert.equal(await student.locator('.learning-path-day').count(), 30);
    await submit(student, student.locator('.learning-path-day').first().getByRole('button', { name: 'Bỏ qua' }).first());
    assert.equal(await student.locator('.learning-path-day').first().getByText('Đã bỏ qua', { exact: true }).count(), 1);
    evidence.learningPath = { durations: [7, 14, 30], selected: 30, skipped: true };

    await teacher.goto('/TeacherFlashcards/Create');
    await teacher.locator('#Input_Title').fill(deckTitle);
    await teacher.locator('#Input_CourseId').selectOption(courseId);
    await submit(teacher, teacher.locator('form.fc-form button[type=submit]'));
    assert.match(pathname(teacher), /\/TeacherFlashcards\/Edit\//);
    const deckId = lastSegment(teacher.url());
    for (let card = 1; card <= 5; card++) {
        await teacher.locator('#new-front').fill(`Word ${card}`);
        await teacher.locator('#new-back').fill(`Meaning ${card}`);
        await submit(teacher, teacher.locator('form').filter({ has: teacher.locator('#new-front') }).locator('button[type=submit]'));
    }
    await submit(teacher, teacher.getByRole('button', { name: 'Xuất bản bộ thẻ' }));
    assert.equal((await unrelatedStudent.goto(`/Flashcards/Study/${deckId}`)).status(), 404);
    const beforeFlashcards = await achievementXp(student);
    await student.goto(`/Flashcards/Study/${deckId}`);
    for (const [index, rating] of ['Good', 'Hard', 'Again', 'Good', 'Good'].entries()) {
        assert.equal(await student.locator('.fc-heading strong').innerText(), String(5 - index));
        await student.locator('[data-flashcard-flip]').press('Enter');
        assert.equal(await student.locator('[data-flashcard-flip]').getAttribute('aria-expanded'), 'true');
        if (index === 0) await captureMobile(student, 'flashcard-flipped-390');
        await submit(student, student.locator(`button[name=rating][value=${rating}]`));
    }
    assert.match(await student.locator('main').innerText(), /đã ôn hết các thẻ đến hạn/);
    await student.reload();
    assert.equal(await student.locator('.fc-heading strong').innerText(), '0');
    const xpAfterDeck = await achievementXp(student);
    assert.equal(xpAfterDeck, beforeFlashcards + 15, '5 flashcard XP and 10 daily-goal XP');
    assert.match(await student.locator('.estudy-stats').innerText(), /1 ngày/);
    evidence.flashcards = { reviewed: 5, due: 0, xpAddedIncludingDailyGoal: 15 };

    const analytics = `/TeacherAnalytics?CourseId=${courseId}`;
    assert.equal((await teacher.goto(analytics)).status(), 200);
    const stats = await teacher.locator('main').innerText();
    assert.match(stats, /100%/);
    assert.match(stats, /80%/);
    assert.doesNotMatch(stats, /@demo\.example\.test|PaymentId/);
    await unrelatedTeacher.goto(analytics);
    assert.match(pathname(unrelatedTeacher), /\/Account\/AccessDenied/);
    evidence.analytics = { ownerAllowed: true, otherTeacherDenied: true };

    await student.goto('/LearningPath');
    const today = student.locator('.learning-path-today');
    await today.locator('.learning-path-actions a').first().click();
    if (pathname(student).startsWith('/Practice/Take/')) {
        await submit(student, student.locator('.exam-submit-btn'));
        assert.match(pathname(student), /\/Practice\/Result\//);
    } else {
        assert.match(pathname(student), /\/Learning\/Lesson\//);
        await submit(student, student.locator('form[action*="/Learning/Completion/"] button[type=submit]'));
    }
    await student.goto('/LearningPath');
    assert.equal(await today.getByText('Đã hoàn thành', { exact: true }).count(), 1);
    evidence.learningPath.completed = true;
    assert.deepEqual(errors, []);
    await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2));
    console.log(JSON.stringify(evidence));
} catch (error) {
    throw new Error(String(error.stack ?? error).split(password).join('[REDACTED]'));
} finally {
    await browser.close();
}

import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

// Chạy trên database trình diễn riêng; tạo câu hỏi mới, không đặt lại dữ liệu.
const baseURL = process.env.ELP_UAT_URL ?? 'http://127.0.0.1:5108';
const password = process.env.LearningDemo__Password;
assert.ok(password, 'Cần cấu hình LearningDemo__Password.');
const output = process.env.ELP_UAT_RESULTS ?? 'artifacts/question-bank-review';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ baseURL, viewport: { width: 1920, height: 1080 } });
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
page.setDefaultTimeout(15000);
const submit = button => Promise.all([page.waitForNavigation(), button.click()]);
const levels = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2'];
const prefix = 'Mẫu câu hỏi ' + Date.now();
const prompts = ['She ___ to school every day.', 'Choose the opposite of early.', 'Where does Anna go on Sundays?', 'Which animal can you hear?'];
const report = { created: 0, desktop: false, mobile: false, filtering: false, editing: false, navigation: false };

try {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill('teacher1@demo.example.test');
  await page.locator('#Password').fill(password);
  await Promise.all([page.waitForURL(url => !url.pathname.includes('/Account/Login')), page.locator('form.account-form button[type=submit]').click()]);

  for (let index = 0; index < 12; index++) {
    await page.goto('/TeacherQuestionBank/Create');
    assert.deepEqual(await page.locator('#Level option').evaluateAll(options => options.map(o => o.value).filter(Boolean)), levels);
    assert.equal(await page.locator('[name=Difficulty]').count(), 0);
    await page.locator('#Content').fill(`${prefix} ${index + 1}: ${prompts[index % prompts.length]}`);
    await page.locator('#PrimarySkill').selectOption(String(index % 4 + 1));
    await page.locator('#Level').selectOption(levels[index % levels.length]);
    await page.locator('[name="Options[0].Content"]').fill('Lựa chọn thứ nhất');
    await page.locator('[name="Options[1].Content"]').fill('Lựa chọn thứ hai');
    await page.locator('[name=CorrectOptionIndex][value="0"]').check();
    await submit(page.locator('form.question-form button[type=submit]'));
    assert.equal(new URL(page.url()).pathname, '/TeacherQuestionBank');
    report.created++;
  }

  await page.locator('#Search').fill(prefix);
  await submit(page.getByRole('button', { name: 'Tìm / lọc', exact: true }));
  assert.equal(await page.locator('[data-question-row]').count(), 10);
  assert.match(await page.locator('.question-bank-list-footer').innerText(), /Hiển thị 1–10 \/ 12/);
  await Promise.all([page.waitForNavigation(), page.getByRole('link', { name: 'Trang 2', exact: true }).click()]);
  assert.equal(await page.locator('[data-question-row]').count(), 2);
  assert.equal(await page.locator('#Search').inputValue(), prefix);
  await page.locator('#Level').selectOption('B1');
  await submit(page.getByRole('button', { name: 'Tìm / lọc', exact: true }));
  assert.equal(await page.locator('[data-question-row]').count(), 2);
  assert.equal(new URL(page.url()).searchParams.get('PageNumber'), '1');
  assert.deepEqual(await page.locator('.question-level').allTextContents(), ['B1', 'B1']);
  report.filtering = true;

  await Promise.all([page.waitForNavigation(), page.locator('[data-question-row]').first().getByRole('link', { name: 'Sửa', exact: true }).click()]);
  assert.equal(await page.locator('[name=Difficulty]').count(), 0);
  await page.locator('#Level').selectOption('C2');
  await page.screenshot({ path: `${output}/bieu-mau-cau-hoi.png`, fullPage: true });
  await submit(page.locator('form.question-form button[type=submit]'));
  assert.match(new URL(page.url()).pathname, /TeacherQuestionBank\/Details/);
  assert.match(await page.locator('.question-bank-metadata').innerText(), /C2/);
  assert.doesNotMatch(await page.locator('#main-content').innerText(), /Độ khó|Practice\/Assessment|Question\/Stimulus/);
  report.editing = true;

  await page.goto('/TeacherQuestionBank?Search=' + encodeURIComponent(prefix));
  await page.screenshot({ path: `${output}/ngan-hang-cau-hoi-may-tinh.png`, fullPage: true });
  assert.ok(await page.locator('.question-bank-table').isVisible());
  assert.equal(await page.locator('[name=Difficulty]').count(), 0);
  assert.doesNotMatch(await page.locator('.question-bank-table thead').innerText(), /Độ khó/);
  report.desktop = true;
  const toggle = page.locator('[data-sidebar-toggle]');
  assert.equal(await toggle.isVisible(), false);
  assert.ok(await page.locator('#studio-sidebar').isVisible());
  report.navigation = true;

  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${output}/ngan-hang-cau-hoi-dien-thoai.png`, fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth));
  await toggle.click();
  assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
  await page.keyboard.press('Escape');
  assert.equal(await toggle.getAttribute('aria-expanded'), 'false');
  report.mobile = true;
  assert.deepEqual(errors, []);
  await writeFile(`${output}/ket-qua.json`, JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report));
} catch (error) {
  throw new Error(String(error.stack ?? error).split(password).join('[REDACTED]'));
} finally {
  await browser.close();
}

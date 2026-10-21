import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Kiểm riêng launcher QA6, không lấy password từ report hoặc ghi state đăng nhập.
const require = createRequire(new URL('../tests/browser/package.json', import.meta.url));
const { chromium } = require('playwright');
const password = process.env.LearningDemo__Password;
assert.ok(password, 'Pass the demo password through process environment.');
const baseURL = 'http://127.0.0.1:18080';
const output = resolve('TestResults/qa6/demo-smoke.json');
const cases = [];
const startedAtUtc = new Date().toISOString();
const project = 'elp-qa6-verify-20261021';
const docker = args => execFileSync('docker', args, { encoding: 'utf8' }).trim();
const migrationExitCode = docker(['inspect', '--format', '{{.State.ExitCode}}', `${project}-migrate-1`]);
const sqlHealth = docker(['inspect', '--format', '{{.State.Health.Status}}', `${project}-db-1`]);
assert.equal(migrationExitCode, '0');
assert.equal(sqlHealth, 'healthy');
const browser = await chromium.launch({ headless: true });
try {
  for (const [actor, path] of [
    ['teacher1', '/TeacherQuestionBank'], ['teacher2', '/TeacherPractices'],
    ['student1', '/Courses'], ['student2', '/Courses'], ['admin1', '/Admin/Users']
  ]) {
    const context = await browser.newContext({ baseURL });
    const page = await context.newPage();
    await page.goto('/Account/Login');
    await page.locator('#Email').fill(`${actor}@demo.example.test`);
    await page.locator('#Password').fill(password);
    await Promise.all([page.waitForNavigation(), page.locator('form.account-form button[type=submit]').click()]);
    assert.ok(!new URL(page.url()).pathname.includes('/Account/Login'), `${actor}: login failed`);
    const response = await page.goto(path);
    assert.equal(response.status(), 200, `${actor}: role page unavailable`);
    assert.ok(!new URL(page.url()).pathname.includes('AccessDenied'));
    cases.push({ actor, path, outcome: 'Passed' });
    await context.close();
  }
  await mkdir(resolve('TestResults/qa6'), { recursive: true });
  await writeFile(output, JSON.stringify({
    scope: 'Fresh Compose migration + seed + five real browser logins; no business data edits',
    baseCommit: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
    workingTreeClean: !execFileSync('git', ['status', '--porcelain'], { encoding: 'utf8' }).trim(),
    startedAtUtc, finishedAtUtc: new Date().toISOString(), baseURL,
    setup: { migrationExitCode, sqlHealth }, cases, outcome: 'Passed'
  }, null, 2));
  console.log('QA6 demo smoke: five actor logins and role pages passed. No password/state exported.');
} finally {
  await browser.close();
}

import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const password = process.env.LearningDemo__Password;
assert.ok(password, 'Set LearningDemo__Password via environment.');
const baseURL = process.env.ELP_UAT_URL ?? 'http://localhost:8081';
const output = 'TestResults/admin-uat';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const email = `admin-uat-${Date.now()}@example.test`;
const teacherPassword = 'TestOnly_Admin123!';
const results = [];
try {
  const page = await browser.newPage({ baseURL, viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/Account/Login');
  await page.locator('#Email').fill('admin1@demo.example.test');
  await page.locator('#Password').fill(password);
  await page.locator('form.account-form button[type=submit]').click();
  await page.waitForURL(url => !url.pathname.includes('/Account/Login'));
  await page.goto('/Admin');
  assert.equal(await page.locator('.admin-summary article').count(), 4);
  await page.screenshot({ path: `${output}/dashboard-desktop.png`, fullPage: true });
  results.push('Dashboard');
  await page.goto('/Admin/CreateTeacher');
  await page.locator('#FullName').fill('Dương UAT Teacher');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(teacherPassword);
  await page.getByRole('button', { name: 'Tạo Teacher', exact: true }).click();
  await page.waitForURL('**/Admin/Users');
  await page.goto(`/Admin/Users?search=${email}&role=Teacher`);
  let row = page.locator('tbody tr').filter({ hasText: email });
  assert.equal(await row.count(), 1);
  await page.screenshot({ path: `${output}/users-desktop.png`, fullPage: true });
  await row.locator('[data-open-admin-dialog]').click();
  let dialog = page.locator('dialog[open]');
  await dialog.locator('select[name=Status]').selectOption('Locked');
  await dialog.getByRole('button', { name: 'Hủy', exact: true }).click();
  assert.equal(await page.locator('dialog[open]').count(), 0);
  await row.locator('[data-open-admin-dialog]').click();
  assert.equal(await dialog.locator('select[name=Status]').inputValue(), 'Active');
  await page.screenshot({ path: `${output}/edit-dialog-desktop.png` });
  await dialog.locator('select[name=Status]').selectOption('Locked');
  await dialog.getByRole('button', { name: 'Lưu thay đổi' }).click();
  await page.goto(`/Admin/Users?search=${email}&status=Locked`);
  assert.equal(await page.locator('tbody tr').count(), 1);
  results.push('Create Teacher, search, role/status filters, lock');
  for (const status of ['Active', 'Disabled', 'Active']) {
    row = page.locator('tbody tr').filter({ hasText: email });
    await row.locator('[data-open-admin-dialog]').click();
    dialog = page.locator('dialog[open]');
    await dialog.locator('select[name=Status]').selectOption(status);
    await dialog.getByRole('button', { name: 'Lưu thay đổi' }).click();
    await page.goto(`/Admin/Users?search=${email}&status=${status}`);
    assert.equal(await page.locator('tbody tr').count(), 1);
  }
  row = page.locator('tbody tr').filter({ hasText: email });
  await row.locator('[data-open-admin-dialog]').click();
  await page.locator('dialog[open] select[name=Role]').selectOption('Student');
  await page.locator('dialog[open]').getByRole('button', { name: 'Lưu thay đổi' }).click();
  await page.goto(`/Admin/Users?search=${email}&role=Student`);
  assert.equal(await page.locator('tbody tr').count(), 1);
  results.push('Unlock, disable, reactivate, change role');
  await page.goto(`/Admin/Audit?search=${email}`);
  assert.equal(await page.locator('tbody tr').count(), 6);
  assert.ok(!(await page.content()).includes(teacherPassword));
  await page.screenshot({ path: `${output}/audit-desktop.png`, fullPage: true });
  results.push('Audit contains creation and all five updates, no passwords');
  await page.setViewportSize({ width: 390, height: 844 });
  for (const [path, name] of [['/Admin', 'dashboard'], ['/Admin/Users', 'users'], ['/Admin/CreateTeacher', 'create-teacher'], ['/Admin/Transactions', 'transactions'], ['/Admin/Audit', 'audit']]) {
    await page.goto(path);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), `${path}: page overflow`);
    await page.screenshot({ path: `${output}/${name}-mobile.png`, fullPage: true });
    if (name === 'users') {
      await page.locator('[data-open-admin-dialog]').first().click();
      await page.screenshot({ path: `${output}/edit-dialog-mobile.png` });
      assert.ok(await page.locator('dialog[open]').evaluate(el => el.scrollWidth <= el.clientWidth + 1));
      await page.keyboard.press('Escape');
      assert.equal(await page.locator('dialog[open]').count(), 0);
    }
  }
  assert.deepEqual(errors, []);
  results.push('Five Admin pages: 390px, no document overflow or JavaScript errors');
  await writeFile(`${output}/results.json`, JSON.stringify({ baseURL, finishedAtUtc: new Date().toISOString(), results }, null, 2));
  console.log(`Admin UAT passed: ${results.length} groups.`);
} catch (error) {
  console.error(String(error.message).split(password).join('[REDACTED]').split(teacherPassword).join('[REDACTED]'));
  process.exitCode = 1;
} finally { await browser.close(); }

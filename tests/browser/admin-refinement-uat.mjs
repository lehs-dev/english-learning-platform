import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const base=new URL(process.env.ELP_UAT_URL??'http://localhost:8080');
assert.ok(['localhost','127.0.0.1','[::1]'].includes(base.hostname));
const email=process.env.ELP_ADMIN_EMAIL,password=process.env.ELP_ADMIN_PASSWORD;
assert.ok(email&&password,'Local admin credentials required.');
const root=fileURLToPath(new URL('../../',import.meta.url));
const directory=`TestResults/admin-refinement-${new Date().toISOString().replace(/[:.]/g,'-')}`;
await mkdir(path.join(root,directory),{recursive:true});
const report={passed:false,readOnly:true,checks:[],screenshots:[]};
const browser=await chromium.launch({headless:true});
try{
 const page=await browser.newPage({baseURL:base.origin,viewport:{width:1440,height:1000}});
 const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.goto('/Account/Login');await page.locator('#Email').fill(email);await page.locator('#Password').fill(password);
 await Promise.all([page.waitForURL(url=>!url.pathname.startsWith('/Account/Login')),page.locator('form.account-form button[type=submit]').click()]);
 const visit=async route=>{const response=await page.goto(route);assert.equal(response.status(),200,route);await page.locator('body.admin-body').waitFor();};
 const submit=async()=>{await Promise.all([page.waitForNavigation(),page.locator('form.admin-filters button[type=submit]').click()]);};
 await visit('/Admin');
 assert.equal(await page.locator('.admin-heading a[href*="CreateTeacher"]').count(),0);
 report.checks.push('Dashboard has no redundant create-teacher action');
 await visit('/Admin/Revenue');
 assert.equal(await page.locator('#period').inputValue(),'month');
 assert.equal(await page.locator('.admin-heading a').count(),0);
 assert.deepEqual(await page.locator('#period option').evaluateAll(options=>options.map(o=>o.value)),['all','week','month','year','custom']);
 const today=new Date(`${await page.locator('#period').getAttribute('data-vietnam-today')}T00:00:00Z`);
 for(const period of ['week','month','year']){
  await page.locator('#period').selectOption(period);
  const start=await page.locator('#fromDate').inputValue(),end=await page.locator('#toDate').inputValue();
  assert.ok(start&&end&&start<=end);
  if(period==='week'){assert.equal(new Date(start+'T00:00:00Z').getUTCDay(),1);assert.equal(new Date(end+'T00:00:00Z').getUTCDay(),0);}
  if(period==='month')assert.equal(start,`${today.getUTCFullYear()}-${String(today.getUTCMonth()+1).padStart(2,'0')}-01`);
  if(period==='year')assert.equal(start,`${today.getUTCFullYear()}-01-01`);
  await submit();assert.equal(new URL(page.url()).searchParams.get('period'),period);
  const human=value=>value.split('-').reverse().join('/');
  assert.ok((await page.locator('[data-revenue-range]').innerText()).includes(human(start)));
  assert.ok((await page.locator('[data-revenue-range]').innerText()).includes(human(end)));
  assert.equal(await page.locator('polyline.admin-chart-line').count(),1);
  report.checks.push(`${period}: calendar dates, range label and line chart`);
 }
 await page.locator('#groupBy').selectOption('month');await submit();
 const monthly=JSON.parse(await page.locator('.admin-chart-data').textContent());
 assert.equal(monthly.length,12);assert.ok(monthly.every(p=>/^\d{2}\/\d{4}$/.test(p.label)));
 await page.locator('.admin-chart-point').last().click();
 assert.ok((await page.locator('.admin-chart-detail').innerText()).includes('₫'));
 report.checks.push('Monthly line chart has all twelve calendar months and point details');
 await page.locator('#fromDate').fill('2026-10-23');await page.locator('#toDate').fill('2026-10-24');
 assert.equal(await page.locator('#period').inputValue(),'custom');await submit();
 assert.equal(new URL(page.url()).searchParams.get('fromDate'),'2026-10-23');
 report.checks.push('Editing dates switches preset to custom and preserves manual range');
 await visit('/Admin/Courses');
 assert.equal(await page.locator('.admin-heading a').count(),0);
 await page.locator('#fromDate').fill('2026-10-01');await page.locator('#toDate').fill('2026-10-31');
 await page.locator('#status').selectOption('Published');await page.locator('#sort').selectOption('Popular');await submit();
 for(const [key,value] of [['fromDate','2026-10-01'],['toDate','2026-10-31'],['sort','Popular'],['status','Published']])assert.equal(new URL(page.url()).searchParams.get(key),value);
 const totals=await page.locator('tbody tr td:nth-child(4)').allTextContents();
 const counts=totals.map(s=>Number(s.match(/\d+/)?.[0]??0));
 assert.deepEqual(counts,[...counts].sort((a,b)=>b-a));
 await page.locator('#sort').selectOption('TeacherPopular');await submit();
 assert.equal(await page.locator('#sort').inputValue(),'TeacherPopular');
 report.checks.push('Course date/status filters and enrollment-based sorting retain selections');
 await visit('/Admin/Courses');
 assert.equal(await page.locator('.admin-report-explanation').count(),0);
 assert.ok((await page.locator('#course-sort-help').innerText()).includes('UTC+7'));
 assert.ok((await page.locator('thead').innerText()).includes('Giảng viên'));
 await visit('/Courses');
 assert.equal(await page.locator('#discovery-title').innerText(),'Danh mục khóa học công khai');
 assert.equal(await page.locator('#admin-sidebar a[href="/Courses"]').innerText(),'Xem danh mục công khai');
 const cards=page.locator('.course-card');
 assert.ok(await cards.count()>0);
 for(const card of await cards.all())assert.equal(await card.locator('.course-cover > strong').innerText(),await card.locator('.course-card-body h3').innerText());
 report.checks.push('Public catalog is distinct from Admin lists and course covers show actual titles');
 await visit('/Admin/Enrollments');
 await page.locator('#fromDate').fill('2026-10-24');await page.locator('#toDate').fill('2026-10-24');
 await page.locator('#completed').selectOption('false');await submit();
 for(const [key,value] of [['fromDate','2026-10-24'],['toDate','2026-10-24'],['completed','false']])assert.equal(new URL(page.url()).searchParams.get(key),value);
 const dates=await page.locator('tbody tr td:nth-child(3)').allTextContents();
 assert.ok(dates.length>0);assert.ok(dates.every(value=>value.trim().startsWith('24/10/2026')));
 await page.locator('#fromDate').fill('2026-10-25');await submit();
 assert.ok(await page.locator('.validation-summary-errors').isVisible());
 assert.equal(await page.locator('tbody tr').count(),0);
 report.checks.push('Enrollment dates combine with completion filter and invalid ranges show no rows');
 await visit('/Admin/Users');await page.locator('.admin-heading a[href*="CreateUser"]').click();
 assert.deepEqual(await page.locator('#Role option').evaluateAll(options=>options.map(o=>o.value)),['Student','Teacher','Admin']);
 assert.equal(await page.locator('#Password').getAttribute('type'),'password');
 assert.equal(await page.locator('form[action*="CreateUser"] input[name=__RequestVerificationToken]').count(),1);
 assert.equal(await page.locator('.auth-shell .auth-aside').count(),1);
 assert.equal(await page.locator('.auth-shell .auth-form-panel').count(),1);
 await page.locator('[data-password-toggle="Password"]').click();
 assert.equal(await page.locator('#Password').getAttribute('type'),'text');
 await page.locator('[data-password-toggle="Password"]').click();
 assert.equal(await page.locator('#Password').getAttribute('type'),'password');
 report.checks.push('Create-user form exposes three roles and retains antiforgery and password input');
 for(const width of [320,390,768,1440]){
  await page.setViewportSize({width,height:width>=1000?1000:844});
  for(const [route,name] of [['/Admin/Revenue?period=month&groupBy=day','revenue'],['/Admin/Courses?sort=TeacherPopular','courses'],['/Admin/CreateUser','create-user'],['/Admin/Enrollments?fromDate=2026-10-24&toDate=2026-10-24','enrollments'],['/Courses','public-catalog']]){
   await visit(route);await page.evaluate(()=>document.fonts.ready);
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${name} ${width}px overflow`);
   if(name==='revenue')assert.equal(await page.locator('polyline.admin-chart-line').count(),1);
   const screenshot=`${name}-${width}.png`;await page.screenshot({path:path.join(root,directory,screenshot),fullPage:true});
   report.screenshots.push(screenshot);report.checks.push(`${name} responsive ${width}px`);
  }
 }
 const guestContext=await browser.newContext({baseURL:base.origin,viewport:{width:390,height:844}});
 const guest=await guestContext.newPage();
 const guestResponse=await guest.goto('/Courses');assert.equal(guestResponse.status(),200);
 assert.equal(await guest.locator('#discovery-title').innerText(),'Khám phá khóa học');
 assert.ok((await guest.locator('.course-library-heading').innerText()).includes('mục tiêu của bạn'));
 assert.ok(await guest.locator('.course-card').count()>0);
 assert.ok(await guest.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
 await guestContext.close();report.checks.push('Guest keeps learner-facing public catalog and responsive cards');
 assert.deepEqual(errors,[]);report.passed=true;
 console.log(`Admin refinement UAT passed: ${report.checks.length} checks. Evidence: ${directory}`);
}catch(error){report.error=String(error.stack??error).split(password).join('[REDACTED]').split(email).join('[REDACTED]');console.error(report.error);process.exitCode=1;}
finally{await writeFile(path.join(root,directory,'results.json'),JSON.stringify(report,null,2));await browser.close();}

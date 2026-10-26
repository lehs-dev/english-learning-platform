using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Application.Administration;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class AdminOverviewDetailTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Courses_FilterVietnamCreationDates_AndRankCourseAndTeacherPopularityInSql()
    {
        var admin=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Admin);
        var teacherA=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Teacher);
        var teacherB=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Teacher);
        var students=new List<Guid>();
        for(var i=0;i<3;i++)students.Add((await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Student)).Id);
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        var marker=$"rank-{Guid.NewGuid():N}";
        var start=new DateTimeOffset(2026,10,23,0,0,0,TimeSpan.FromHours(7));
        var end=start.AddDays(1);
        var before=new Course{Title=marker+"-before",OwnerTeacherUserId=teacherA.Id,CreatedAtUtc=start.AddTicks(-1)};
        var popular=new Course{Title=marker+"-popular",OwnerTeacherUserId=teacherA.Id,CreatedAtUtc=start};
        var newest=new Course{Title=marker+"-newest",OwnerTeacherUserId=teacherB.Id,CreatedAtUtc=end.AddTicks(-1)};
        var outside=new Course{Title=marker+"-outside",OwnerTeacherUserId=teacherB.Id,CreatedAtUtc=end};
        db.Courses.AddRange(before,popular,newest,outside);
        foreach(var student in students.Take(2))db.Enrollments.Add(new(){Course=popular,StudentUserId=student});
        db.Enrollments.Add(new(){Course=newest,StudentUserId=students[0]});
        foreach(var student in students)db.Enrollments.Add(new(){Course=outside,StudentUserId=student});
        await db.SaveChangesAsync();
        var dates=new AdminDateRange(new(2026,10,23),new(2026,10,23));
        foreach(var pair in new[]{(AdminCourseSort.Newest,newest.Id),(AdminCourseSort.Popular,popular.Id),(AdminCourseSort.TeacherPopular,newest.Id)})
        {
            var result=(await service.CoursesAsync(admin.Id,marker,CourseStatus.Draft,1,dates:dates,sort:pair.Item1))!;
            Assert.Equal(2,result.Total);
            Assert.Equal(pair.Item2,result.Items[0].Id);
            Assert.DoesNotContain(result.Items,c=>c.Id==before.Id||c.Id==outside.Id);
            Assert.Equal(4,Assert.Single(result.Items,c=>c.Id==newest.Id).TeacherEnrollmentCount);
            Assert.Equal(2,Assert.Single(result.Items,c=>c.Id==popular.Id).EnrollmentCount);
        }
        await Assert.ThrowsAsync<ArgumentException>(()=>service.CoursesAsync(admin.Id,marker,null,1,dates:new(new(2026,10,24),new(2026,10,23))));
        await Assert.ThrowsAsync<ArgumentException>(()=>service.CoursesAsync(admin.Id,marker,null,1,sort:(AdminCourseSort)99));
        using var client=IdentityTestHelpers.CreateClient(factory);
        using var login=await IdentityTestHelpers.LoginAsync(client,admin);
        var html=await client.GetStringAsync($"/Admin/Courses?search={marker}&fromDate=2026-10-23&toDate=2026-10-23&sort=Popular");
        Assert.Contains(popular.Title,html);
        Assert.DoesNotContain(outside.Title,html);
        var invalid=await client.GetStringAsync("/Admin/Courses?fromDate=2026-10-24&toDate=2026-10-23");
        Assert.Contains("validation-summary-errors",invalid);
        using var invalidSort=await client.GetAsync("/Admin/Courses?sort=99");
        Assert.Equal(HttpStatusCode.BadRequest,invalidSort.StatusCode);
    }

    [Fact]
    public async Task Courses_CombineSearchAndStatus_BeforeStablePaging_AndShowHistoricalEnrollmentCounts()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var locked = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var marker = $"courses-{Guid.NewGuid():N}";
        var time = new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        (await db.Users.SingleAsync(u => u.Id == locked.Id)).AccountStatus = AccountStatus.Locked;
        var expected = Enumerable.Range(0, 21).Select(i => new Course
        {
            Title = $"{marker}-{i}", OwnerTeacherUserId = teacher.Id,
            Status = CourseStatus.Draft, CreatedAtUtc = time
        }).ToArray();
        var wrongStatus = new Course { Title = marker, OwnerTeacherUserId = teacher.Id, Status = CourseStatus.Archived };
        var wrongSearch = new Course { Title = "Other course", OwnerTeacherUserId = teacher.Id, Status = CourseStatus.Draft };
        db.Courses.AddRange(expected);
        db.Courses.AddRange(wrongStatus, wrongSearch);
        db.Enrollments.AddRange(
            new Enrollment { Course = expected[0], StudentUserId = student.Id },
            new Enrollment { Course = expected[0], StudentUserId = locked.Id, CompletedAtUtc = time });
        await db.SaveChangesAsync();
        var trackedBefore = db.ChangeTracker.Entries().Count();

        var first = (await service.CoursesAsync(admin.Id, $" {marker} ", CourseStatus.Draft, 1))!;
        var second = (await service.CoursesAsync(admin.Id, marker, CourseStatus.Draft, 2))!;
        Assert.Equal(marker, first.Search);
        Assert.Equal(21, first.Total);
        Assert.Equal(2, first.Pages);
        Assert.Equal(20, first.Items.Count);
        Assert.Single(second.Items);
        var actual = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(expected.Select(c => c.Id).Order(), actual.Select(c => c.Id).Order());
        Assert.Equal(21, actual.Select(c => c.Id).Distinct().Count());
        Assert.All(actual, c =>
        {
            Assert.Equal(teacher.FullName, c.TeacherName);
            Assert.Equal(teacher.Email, c.TeacherEmail);
            Assert.Equal(CourseStatus.Draft, c.Status);
        });
        var counted = Assert.Single(actual, c => c.Id == expected[0].Id);
        Assert.Equal(2, counted.EnrollmentCount);
        Assert.Equal(1, counted.CompletedEnrollmentCount);
        Assert.All(actual.Where(c => c.Id != expected[0].Id), c => Assert.Equal(0, c.EnrollmentCount));
        Assert.Equal(first.Items.Select(c => c.Id), (await service.CoursesAsync(admin.Id, marker, CourseStatus.Draft, 0))!.Items.Select(c => c.Id));
        Assert.Equal(second.Items.Select(c => c.Id), (await service.CoursesAsync(admin.Id, marker, CourseStatus.Draft, 999))!.Items.Select(c => c.Id));
        Assert.Equal(expected[0].Id, Assert.Single((await service.CoursesAsync(admin.Id, expected[0].Id.ToString(), null, 1))!.Items).Id);
        Assert.Equal(trackedBefore, db.ChangeTracker.Entries().Count());

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var response = await client.GetAsync($"/Admin/Courses?search={marker}&status=Draft&fromDate=2026-10-24&toDate=2026-10-24&sort=Newest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        var next = NextPage(html, $"search={marker}", "status=Draft", "fromDate=2026-10-24", "toDate=2026-10-24", "sort=Newest");
        var nextHtml = await client.GetStringAsync(next);
        Assert.Contains(second.Items.Single().Title, nextHtml);
        Assert.DoesNotContain(wrongSearch.Title, html + nextHtml);
    }

    [Fact]
    public async Task Enrollments_FilterVietnamEnrollmentDay_IncludeBothBoundaries_AndRejectInvalidRange()
    {
        var admin=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Admin);
        var teacher=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Teacher);
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service=scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        var course=new Course{Title=$"date-enroll-{Guid.NewGuid():N}",OwnerTeacherUserId=teacher.Id};
        var start=new DateTimeOffset(2026,10,22,17,0,0,TimeSpan.Zero);
        var times=new[]{start.AddTicks(-1),start,start.AddDays(1).AddTicks(-1),start.AddDays(1)};
        var rows=new List<Enrollment>();
        foreach(var time in times)
        {
            var student=await IdentityTestHelpers.CreateUserAsync(factory,AppRoles.Student);
            var enrollment=new Enrollment{Course=course,StudentUserId=student.Id,EnrolledAtUtc=time,CompletedAtUtc=time.AddDays(2)};
            db.Enrollments.Add(enrollment);rows.Add(enrollment);
        }
        await db.SaveChangesAsync();
        var dates=new AdminDateRange(new(2026,10,23),new(2026,10,23));
        var result=(await service.EnrollmentsAsync(admin.Id,course.Title,true,1,courseId:course.Id,dates:dates))!;
        Assert.Equal(new[]{rows[1].Id,rows[2].Id}.Order(),result.Items.Select(e=>e.Id).Order());
        Assert.Equal(2,result.Total);
        Assert.Equal(3,(await service.EnrollmentsAsync(admin.Id,null,null,1,courseId:course.Id,dates:new(new(2026,10,23),null)))!.Total);
        Assert.Equal(3,(await service.EnrollmentsAsync(admin.Id,null,null,1,courseId:course.Id,dates:new(null,new(2026,10,23))))!.Total);
        await Assert.ThrowsAsync<ArgumentException>(()=>service.EnrollmentsAsync(admin.Id,null,null,1,dates:new(new(2026,10,24),new(2026,10,23))));
        using var client=IdentityTestHelpers.CreateClient(factory);
        using var login=await IdentityTestHelpers.LoginAsync(client,admin);
        foreach(var invalid in new[]{"fromDate=2026-10-24&toDate=2026-10-23","fromDate=bad","toDate=9999-12-31"})
        {
            using var response=await client.GetAsync($"/Admin/Enrollments?{invalid}");
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var html=await response.Content.ReadAsStringAsync();
            Assert.Contains("validation-summary-errors",html);
            Assert.DoesNotContain(course.Title,html);
        }
    }

    [Fact]
    public async Task Enrollments_CombineSearchCompletionAndCourse_KeepHistoricalRowsAndFiltersAcrossPages()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var marker = $"enrollments-{Guid.NewGuid():N}";
        var time = new DateTimeOffset(2026, 10, 24, 3, 0, 0, TimeSpan.Zero);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        var course = new Course { Title = "Historical archived course", OwnerTeacherUserId = teacher.Id, Status = CourseStatus.Archived };
        var otherCourse = new Course { Title = "Other course", OwnerTeacherUserId = teacher.Id };
        Enrollment Add(string name, Course parent, bool completed, AccountStatus status = AccountStatus.Active)
        {
            var student = new ApplicationUser { FullName = name, UserName = $"{Guid.NewGuid():N}@example.test",
                Email = $"{Guid.NewGuid():N}@example.test", AccountStatus = status };
            db.Users.Add(student);
            var enrollment = new Enrollment { StudentUserId = student.Id, Course = parent, EnrolledAtUtc = time,
                CompletedAtUtc = completed ? time.AddHours(1) : null };
            db.Enrollments.Add(enrollment);
            return enrollment;
        }
        var expected = Enumerable.Range(0, 21).Select(i => Add($"{marker}-{i}", course, true,
            i == 20 ? AccountStatus.Locked : AccountStatus.Active)).ToArray();
        var incomplete = Add(marker, course, false);
        Add(marker, otherCourse, true);
        Add("Unrelated student", course, true);
        await db.SaveChangesAsync();
        var trackedBefore = db.ChangeTracker.Entries().Count();

        var first = (await service.EnrollmentsAsync(admin.Id, marker, true, 1, courseId: course.Id))!;
        var second = (await service.EnrollmentsAsync(admin.Id, marker, true, 2, courseId: course.Id))!;
        Assert.Equal(21, first.Total);
        Assert.Equal(2, first.Pages);
        Assert.Equal(20, first.Items.Count);
        Assert.Single(second.Items);
        var actual = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(expected.Select(e => e.Id).Order(), actual.Select(e => e.Id).Order());
        Assert.Equal(21, actual.Select(e => e.Id).Distinct().Count());
        Assert.All(actual, e =>
        {
            Assert.Equal(course.Id, e.CourseId);
            Assert.Equal(course.Title, e.CourseTitle);
            Assert.Contains(marker, e.StudentName);
            Assert.EndsWith("@example.test", e.StudentEmail);
            Assert.NotNull(e.CompletedAtUtc);
        });
        Assert.Contains(actual, e => e.Id == expected[20].Id);
        Assert.Equal(incomplete.Id, Assert.Single((await service.EnrollmentsAsync(admin.Id, marker, false, 1, courseId: course.Id))!.Items).Id);
        Assert.Equal(first.Items.Select(e => e.Id), (await service.EnrollmentsAsync(admin.Id, marker, true, -1, courseId: course.Id))!.Items.Select(e => e.Id));
        Assert.Equal(second.Items.Select(e => e.Id), (await service.EnrollmentsAsync(admin.Id, marker, true, 999, courseId: course.Id))!.Items.Select(e => e.Id));
        Assert.Equal(expected[0].Id, Assert.Single((await service.EnrollmentsAsync(admin.Id, expected[0].Id.ToString(), null, 1))!.Items).Id);
        Assert.Equal(trackedBefore, db.ChangeTracker.Entries().Count());

        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var response = await client.GetAsync($"/Admin/Enrollments?search={marker}&completed=true&courseId={course.Id}&fromDate=2026-10-24&toDate=2026-10-24");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        var next = NextPage(html, $"search={marker}", "completed=True", $"courseId={course.Id}", "fromDate=2026-10-24", "toDate=2026-10-24");
        var nextHtml = await client.GetStringAsync(next);
        Assert.Contains(second.Items.Single().StudentName, nextHtml);
        Assert.DoesNotContain("Unrelated student", html + nextHtml);
    }

    private static string NextPage(string html, params string[] filters)
    {
        var next = Regex.Matches(html, "href=\"([^\"]+)\"")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Single(href => href.Contains("page=2"));
        foreach (var filter in filters) Assert.Contains(filter, next, StringComparison.OrdinalIgnoreCase);
        return next;
    }
}

// This class has a separate SQL database, so empty-data assertions do not depend on test ordering.
public sealed class AdminOverviewEmptyTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task EmptyBusinessTables_ReturnEmptyFirstPages_AndRenderBothLists()
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        var courses = (await service.CoursesAsync(admin.Id, null, null, 99))!;
        var enrollments = (await service.EnrollmentsAsync(admin.Id, null, null, 99))!;
        Assert.Equal(0, courses.Total);
        Assert.Equal(1, courses.Page);
        Assert.Equal(1, courses.Pages);
        Assert.Empty(courses.Items);
        Assert.Equal(0, enrollments.Total);
        Assert.Equal(1, enrollments.Page);
        Assert.Equal(1, enrollments.Pages);
        Assert.Empty(enrollments.Items);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        var dashboardHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin"));
        Assert.Contains("Chưa có lượt đăng ký để tính tỷ lệ hoàn thành.", dashboardHtml);
        Assert.Contains("Chưa có khóa học trong hệ thống.", dashboardHtml);
        Assert.DoesNotContain("<progress", dashboardHtml);
        foreach (var path in new[] { "/Admin/Courses", "/Admin/Enrollments", $"/Admin/Enrollments?courseId={Guid.NewGuid()}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        foreach (var path in new[] { "/Admin/Courses?status=999", "/Admin/Courses?status=unknown",
            "/Admin/Courses?page=bad", "/Admin/Enrollments?completed=unknown", "/Admin/Enrollments?courseId=bad" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => service.CoursesAsync(admin.Id, null, (CourseStatus)999, 1));
    }
}

public sealed class AdminOverviewAuthorizationTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Teacher)]
    public async Task NonAdmin_CannotReadEitherListThroughServiceOrMvc(string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        Assert.Null(await service.CoursesAsync(user.Id, null, null, 1));
        Assert.Null(await service.EnrollmentsAsync(user.Id, null, null, 1));
        Assert.Null(await service.CoursesAsync(Guid.NewGuid(), null, null, 1));
        Assert.Null(await service.EnrollmentsAsync(Guid.NewGuid(), null, null, 1));
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        foreach (var path in new[] { "/Admin/Courses", "/Admin/Enrollments" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task InactiveAdmin_LosesBothServiceAndExistingCookieAccess(AccountStatus status)
    {
        var admin = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Admin);
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == admin.Id)).AccountStatus = status;
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IAdminOverviewDetailQueryService>();
        Assert.Null(await service.CoursesAsync(admin.Id, null, null, 1));
        Assert.Null(await service.EnrollmentsAsync(admin.Id, null, null, 1));
        foreach (var path in new[] { "/Admin/Courses", "/Admin/Enrollments" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
    }

    [Theory]
    [InlineData("/Admin/Courses")]
    [InlineData("/Admin/Enrollments")]
    public async Task Guest_MustLogin(string path)
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }
}

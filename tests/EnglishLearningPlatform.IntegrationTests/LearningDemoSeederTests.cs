using System.Net;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Homework;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Learning;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Each test starts its own migrated database because the demo account names are intentionally fixed.
public sealed class LearningDemoSeederTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private const string FreeTitle = "Demo · Everyday English";
    private const string PaidTitle = "Demo · Business English";

    public Task InitializeAsync() => factory.InitializeAsync();
    public Task DisposeAsync() => ((IAsyncLifetime)factory).DisposeAsync();

    [Fact]
    public async Task FreshSeed_AllActorsAuthenticateAndRepeatWithoutDuplicatesOrPaidGrants()
    {
        await Seed();
        int[] counts;
        Guid freeId, paidId, finalId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var free = await db.Courses.SingleAsync(c => c.Title == FreeTitle);
            var paid = await db.Courses.Include(c => c.Modules).ThenInclude(m => m.Lessons)
                .SingleAsync(c => c.Title == PaidTitle);
            freeId = free.Id; paidId = paid.Id; finalId = free.FinalAssessmentId!.Value;
            Assert.True(paid.IsPaid); Assert.Equal(199000m, paid.Price); Assert.Equal(CourseStatus.Published, paid.Status);
            Assert.Contains(paid.Modules.SelectMany(m => m.Lessons), l => l.Status == LessonStatus.Published);
            counts = await Counts(db);
            // Recover the final pointer after an older or interrupted seed without recreating its assessment.
            free.FinalAssessmentId = null;
            await db.SaveChangesAsync();
        }
        await Seed();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Equal(counts, await Counts(db));
            Assert.Equal(5, await db.Users.CountAsync());
            Assert.Equal(AppRoles.All.OrderBy(r => r), await db.Roles.Select(r => r.Name!).OrderBy(r => r).ToListAsync());
            Assert.False(await db.Users.AnyAsync(u => u.Email == "guest@demo.example.test"));
            foreach (var (name, role) in new[]
                { ("admin1", AppRoles.Admin), ("teacher1", AppRoles.Teacher), ("teacher2", AppRoles.Teacher),
                  ("student1", AppRoles.Student), ("student2", AppRoles.Student) })
            {
                var user = (await users.FindByEmailAsync(name + "@demo.example.test"))!;
                Assert.Equal(role, Assert.Single(await users.GetRolesAsync(user)));
                Assert.True(await users.CheckPasswordAsync(user, IdentityTestHelpers.Password));
            }
            var final = await db.Assessments.Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Options)
                .Include(a => a.Questions).ThenInclude(q => q.Question).ThenInclude(q => q.Stimulus)
                .SingleAsync(a => a.Id == finalId);
            Assert.Equal(finalId, (await db.Courses.SingleAsync(c => c.Id == freeId)).FinalAssessmentId);
            Assert.Equal(freeId, final.CourseId); Assert.Equal(AssessmentStatus.Published, final.Status);
            Assert.Equal(AssessmentType.PracticeExam, final.AssessmentType); Assert.Null(final.TargetSkill);
            Assert.Equal(15, final.DurationMinutes); Assert.Equal(3, final.MaxAttempts); Assert.Equal(80m, final.PassingScore);
            Assert.Equal(5, final.Questions.Count);
            Assert.Equal(Enumerable.Range(0, 5), final.Questions.OrderBy(q => q.OrderIndex).Select(q => q.OrderIndex));
            Assert.Equal(3, final.Questions.Count(q => q.Question.PrimarySkill == EnglishSkill.Grammar));
            Assert.Equal(2, final.Questions.Count(q => q.Question.PrimarySkill == EnglishSkill.Vocabulary));
            Assert.All(final.Questions, q =>
            {
                Assert.Equal(1m, q.Points); Assert.Equal(QuestionStatus.Published, q.Question.Status);
                Assert.Equal(final.OwnerTeacherUserId, q.Question.OwnerTeacherUserId);
                Assert.Equal(4, q.Question.Options.Count); Assert.Single(q.Question.Options, o => o.IsCorrect);
            });
            // Mẫu demo mới phải qua cùng bộ quy tắc đang dùng khi Publish/Start.
            var course = await db.Courses.SingleAsync(c => c.Id == freeId);
            Assert.Empty(scope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>()
                .Validate(final, final.Questions.ToList(), course, new List<Course> { course }, isFirstPublish: false));
            Assert.Single(await db.Enrollments.ToListAsync());
            Assert.False(await db.Enrollments.AnyAsync(e => e.Course.IsPaid));
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.Payments.ToListAsync());
        }

        using (var guest = IdentityTestHelpers.CreateClient(factory))
        {
            using var catalog = await guest.GetAsync("/Courses");
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            Assert.Contains("Everyday English", await catalog.Content.ReadAsStringAsync());
            using var details = await guest.GetAsync($"/Courses/Details/{freeId}");
            Assert.Equal(HttpStatusCode.OK, details.StatusCode);
            using var lesson = await guest.GetAsync($"/Learning/Course/{freeId}");
            Assert.Equal(HttpStatusCode.Redirect, lesson.StatusCode);
            Assert.Contains("/Account/Login", lesson.Headers.Location!.ToString());
        }
        foreach (var (name, path) in new[]
            { ("admin1", "/api/probe/policy/single-active-role"),
              ("teacher1", $"/Learning/ManageCourse/{freeId}"), ("student1", $"/Learning/Course/{freeId}") })
        {
            using var scope = factory.Services.CreateScope();
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync(name + "@demo.example.test"))!;
            using var client = IdentityTestHelpers.CreateClient(factory);
            using var login = await IdentityTestHelpers.LoginAsync(client, user);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            using var actorPage = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, actorPage.StatusCode);
            if (name == "student1")
            {
                using var paidPage = await client.GetAsync($"/Learning/Course/{paidId}");
                Assert.Equal(HttpStatusCode.Redirect, paidPage.StatusCode);
                Assert.Contains("/Account/AccessDenied", paidPage.Headers.Location!.ToString());
            }
        }
    }

    [Fact]
    public async Task LegacySeed_BackfillsMissingFinalAndAdminWithoutReplacingExistingCourseData()
    {
        Guid teacherId, studentId, freeId, lessonId, enrollmentId, unrelatedId;
        var completed = DateTimeOffset.UtcNow.AddDays(-1);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var teacher = await CreateDemoUser(users, "teacher1", AppRoles.Teacher);
            var student = await CreateDemoUser(users, "student1", AppRoles.Student);
            teacherId = teacher.Id; studentId = student.Id;
            var lesson = new Lesson
            {
                Title = "Existing lesson",
                Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Keep this content." }]
            };
            var free = new Course
            {
                OwnerTeacherUserId = teacher.Id,
                Title = FreeTitle,
                Status = CourseStatus.Published,
                Description = "Edited course description",
                Modules = [new Module { Title = "Existing module", Lessons = [lesson] }]
            };
            var unrelated = new Course { OwnerTeacherUserId = teacher.Id, Title = "Teacher's own course", Description = "Untouched" };
            var enrollment = new Enrollment
            {
                Course = free,
                StudentUserId = student.Id,
                CompletedAtUtc = completed,
                LastAccessedLesson = lesson,
                LessonProgressEntries = [new LessonProgress { Lesson = lesson, IsCompleted = true }]
            };
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Courses.AddRange(free, unrelated, new Course
            {
                OwnerTeacherUserId = teacher.Id,
                Title = PaidTitle,
                IsPaid = true,
                Price = 199000,
                Status = CourseStatus.Published,
                Description = "Khóa trả phí mẫu; chưa tích hợp thanh toán."
            });
            db.Enrollments.Add(enrollment); await db.SaveChangesAsync();
            freeId = free.Id; lessonId = lesson.Id; enrollmentId = enrollment.Id; unrelatedId = unrelated.Id;
        }

        await Seed("NewDemo_123!");
        await Seed("NewDemo_123!");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var free = await db.Courses.SingleAsync(c => c.Id == freeId);
            Assert.Equal("Edited course description", free.Description); Assert.NotNull(free.FinalAssessmentId);
            Assert.Equal("Existing lesson", (await db.Lessons.SingleAsync(l => l.Id == lessonId)).Title);
            Assert.Equal("Keep this content.", (await db.LessonResources.SingleAsync(r => r.LessonId == lessonId)).ContentText);
            var enrollment = await db.Enrollments.Include(e => e.LessonProgressEntries).SingleAsync(e => e.Id == enrollmentId);
            Assert.Equal(completed, enrollment.CompletedAtUtc); Assert.Equal(lessonId, enrollment.LastAccessedLessonId);
            Assert.True(Assert.Single(enrollment.LessonProgressEntries).IsCompleted);
            Assert.Equal(1, await db.Enrollments.CountAsync(e => e.CourseId == freeId && e.StudentUserId == studentId));
            Assert.Equal(1, await db.Courses.CountAsync(c => c.Title == FreeTitle && c.OwnerTeacherUserId == teacherId));
            Assert.Equal(1, await db.Assessments.CountAsync(a => a.CourseId == freeId));
            Assert.Equal("Untouched", (await db.Courses.SingleAsync(c => c.Id == unrelatedId)).Description);
            Assert.Contains("Sandbox", (await db.Courses.SingleAsync(c => c.Title == PaidTitle)).Description!);
            Assert.False(await db.Enrollments.AnyAsync(e => e.Course.IsPaid));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var teacher = (await users.FindByIdAsync(teacherId.ToString()))!;
            Assert.True(await users.CheckPasswordAsync(teacher, IdentityTestHelpers.Password));
            Assert.False(await users.CheckPasswordAsync(teacher, "NewDemo_123!"));
            var admin = (await users.FindByEmailAsync("admin1@demo.example.test"))!;
            Assert.Equal(AppRoles.Admin, Assert.Single(await users.GetRolesAsync(admin)));
            Assert.True(await users.CheckPasswordAsync(admin, "NewDemo_123!"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatSeed_PreservesRealGradedHistoryAndCompletedEnrollment(bool legacyDefinition)
    {
        await Seed();
        Guid finalId, attemptId, enrollmentId;
        DateTimeOffset? completed;
        Dictionary<Guid, string?> passwords;
        List<Guid> questionIds, answerIds, skillResultIds;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var student = await db.Users.SingleAsync(u => u.Email == "student1@demo.example.test");
            var course = await db.Courses.SingleAsync(c => c.Title == FreeTitle);
            finalId = course.FinalAssessmentId!.Value;
            var lesson = await db.Lessons.SingleAsync(l => l.Module.CourseId == course.Id &&
                l.Module.Visibility == ModuleVisibility.Visible && l.Status == LessonStatus.Published);
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICourseService>()
                .SetLessonCompletedAsync(student.Id, lesson.Id, true)).Success);
            var attempts = scope.ServiceProvider.GetRequiredService<IAttemptService>();
            var started = await attempts.StartAsync(student.Id, finalId);
            Assert.Equal(AttemptStartStatus.Started, started.Status); attemptId = started.AttemptId!.Value;
            var questions = await db.AssessmentQuestions.Include(q => q.Question).ThenInclude(q => q.Options)
                .Where(q => q.AssessmentId == finalId).OrderBy(q => q.OrderIndex).ToListAsync();
            for (var i = 0; i < questions.Count; i++)
            {
                var selected = questions[i].Question.Options.First(o => o.IsCorrect == (i < 4));
                Assert.Equal(AttemptSaveStatus.Saved, await attempts.SaveAnswerAsync(student.Id, attemptId, questions[i].Id, selected.Id));
            }
            var result = await attempts.FinalizeAsync(student.Id, attemptId);
            Assert.Equal(AttemptFinalizeStatus.Graded, result.Status); Assert.True(result.Passed); Assert.Equal(80m, result.OverallScore);
            db.ChangeTracker.Clear();
            var enrollment = await db.Enrollments.SingleAsync(e => e.CourseId == course.Id && e.StudentUserId == student.Id);
            enrollmentId = enrollment.Id; completed = enrollment.CompletedAtUtc; Assert.NotNull(completed);
            var final = await db.Assessments.SingleAsync(a => a.Id == finalId);
            final.Description = "Teacher edited after a student completed the course.";
            if (legacyDefinition)
            {
                // Chỉ dựng fixture mô phỏng dữ liệu demo cũ, không đi qua chức năng Teacher.
                // Seeder mới không được tự đổi định nghĩa cũ đã có lịch sử chấm thật.
                final.AssessmentType = AssessmentType.SkillAssessment;
                final.TargetSkill = EnglishSkill.Grammar;
            }
            await db.SaveChangesAsync();
            questionIds = questions.Select(q => q.QuestionId).OrderBy(id => id).ToList();
            answerIds = (await db.AttemptAnswers.Where(a => a.AttemptId == attemptId).Select(a => a.Id).ToListAsync())
                .OrderBy(id => id).ToList();
            skillResultIds = (await db.AttemptSkillResults.Where(r => r.AttemptId == attemptId).Select(r => r.Id).ToListAsync())
                .OrderBy(id => id).ToList();
            passwords = await db.Users.ToDictionaryAsync(u => u.Id, u => u.PasswordHash);
        }
        await Seed("RotatedDemo_123!");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var final = await db.Assessments.SingleAsync(a => a.Id == finalId);
            Assert.Equal("Teacher edited after a student completed the course.", final.Description);
            Assert.Equal(legacyDefinition ? AssessmentType.SkillAssessment : AssessmentType.PracticeExam, final.AssessmentType);
            Assert.Equal(legacyDefinition ? EnglishSkill.Grammar : (EnglishSkill?)null, final.TargetSkill);
            Assert.Equal(finalId, (await db.Courses.SingleAsync(c => c.Title == FreeTitle)).FinalAssessmentId);
            Assert.Equal(completed, (await db.Enrollments.SingleAsync(e => e.Id == enrollmentId)).CompletedAtUtc);
            var attempt = await db.Attempts.SingleAsync(a => a.Id == attemptId);
            Assert.Equal(AttemptStatus.Graded, attempt.Status); Assert.Equal(80m, attempt.OverallScore); Assert.True(attempt.Passed);
            Assert.Equal(questionIds, (await db.AssessmentQuestions.Where(q => q.AssessmentId == finalId)
                .Select(q => q.QuestionId).ToListAsync()).OrderBy(id => id));
            Assert.Equal(answerIds, (await db.AttemptAnswers.Where(a => a.AttemptId == attemptId)
                .Select(a => a.Id).ToListAsync()).OrderBy(id => id));
            Assert.Equal(skillResultIds, (await db.AttemptSkillResults.Where(r => r.AttemptId == attemptId)
                .Select(r => r.Id).ToListAsync()).OrderBy(id => id));
            Assert.Equal(5, answerIds.Count); Assert.Equal(2, skillResultIds.Count);
            foreach (var user in await db.Users.ToListAsync()) Assert.Equal(passwords[user.Id], user.PasswordHash);
            Assert.Equal(1, await db.Attempts.CountAsync()); Assert.Equal(1, await db.Assessments.CountAsync());
            Assert.False(await db.Enrollments.AnyAsync(e => e.Course.IsPaid));
        }
    }

    [Fact]
    public async Task ExistingAdminWithUnexpectedRole_IsRejectedWithoutChangingIdentityOrSeedingData()
    {
        Guid adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await CreateDemoUser(users, "admin1", AppRoles.Admin); adminId = admin.Id;
            Assert.True((await users.RemoveFromRoleAsync(admin, AppRoles.Admin)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, AppRoles.Teacher)).Succeeded);
        }
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Seed());
        Assert.Contains("unexpected role", exception.Message);
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync()); Assert.False(await db.Courses.AnyAsync());
        var manager = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = (await manager.FindByIdAsync(adminId.ToString()))!;
        Assert.Equal(AppRoles.Teacher, Assert.Single(await manager.GetRolesAsync(existing)));
        Assert.True(await manager.CheckPasswordAsync(existing, IdentityTestHelpers.Password));
    }

    private async Task Seed(string password = IdentityTestHelpers.Password)
    {
        using var scope = factory.Services.CreateScope();
        await LearningDemoSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), password);
    }

    private static async Task<ApplicationUser> CreateDemoUser(UserManager<ApplicationUser> users, string name, string role)
    {
        var email = name + "@demo.example.test";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Existing " + name };
        Assert.True((await users.CreateAsync(user, IdentityTestHelpers.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    [Fact]
    public async Task FullSeed_WorkshopSupportsStudentAndTeacherAndPreservesEdits()
    {
        await SeedFullAsync();
        Guid courseId, studentId, teacherId, pendingId;
        var editedDeadline = DateTimeOffset.UtcNow.AddDays(10);
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AppDbContext>();
            var course = await db.Courses.SingleAsync(course => course.Title == "Tiếng Anh tại quán cà phê");
            courseId = course.Id;
            teacherId = course.OwnerTeacherUserId;
            Assert.Equal("teacher2@demo.example.test", await db.Users.Where(user => user.Id == teacherId)
                .Select(user => user.Email).SingleAsync());
            studentId = await db.Users.Where(user => user.Email == "student1@demo.example.test").Select(user => user.Id).SingleAsync();
            Assert.Equal(6, await db.Lessons.CountAsync(lesson => lesson.Module.CourseId == courseId));
            Assert.Equal(3, await db.LessonProgressEntries.CountAsync(progress => progress.Enrollment.CourseId == courseId &&
                progress.Enrollment.StudentUserId == studentId && progress.IsCompleted));

            var practiceId = await db.Practices.Where(practice => practice.Lesson!.Module.CourseId == courseId)
                .Select(practice => practice.Id).SingleAsync();
            var sheet = await services.GetRequiredService<IPracticeService>().GetSheetAsync(studentId, practiceId);
            Assert.Equal(LearningAccessResult.Allowed, sheet.Access);
            Assert.Equal(Enum.GetValues<QuestionType>(), sheet.Value!.Questions.Select(question => question.QuestionType));
            var audioUrl = Assert.Single(sheet.Value.Questions, question => question.QuestionType == QuestionType.Dictation).AudioUrl;
            Assert.NotNull(audioUrl);
            var audio = await services.GetRequiredService<IStimulusAudioStore>().ReadAsync(audioUrl.Split('/')[^1]);
            Assert.NotNull(audio);
            Assert.Equal("audio/wav", audio.ContentType);

            var decks = await services.GetRequiredService<IFlashcardService>().GetDecksAsync(studentId);
            Assert.Equal(6, Assert.Single(decks.Value!, deck => deck.CourseId == courseId).CardCount);
            var streak = await services.GetRequiredService<IGamificationService>().GetSummaryAsync(studentId);
            Assert.Equal(5, streak.Value!.LongestStreak);

            var inbox = await services.GetRequiredService<ICourseDiscussionService>().GetInboxAsync(teacherId, courseId, 1);
            Assert.Equal(2, inbox.Value!.Threads.Count);
            Assert.Single(inbox.Value.Threads, thread => thread.IsAwaitingTeacher);
            var homework = services.GetRequiredService<IWritingHomeworkService>();
            var work = await homework.GetCourseAsync(studentId, courseId, 1, CancellationToken.None);
            Assert.Equal(3, work.Value!.Assignments.Count);
            var pending = Assert.Single(work.Value.Assignments, task => !task.Submitted);
            Assert.Equal("Viết lời nhắn đặt món", pending.Title);
            Assert.True(pending.Open);
            Assert.NotNull(pending.DueAtUtc);
            pendingId = pending.Id;
            var reviewed = Assert.Single(work.Value.Assignments, task => task.Reviewed);
            var review = await homework.GetAssignmentAsync(studentId, reviewed.Id, 1, CancellationToken.None);
            Assert.Equal(92, Assert.Single(review.Value!.Submissions).Score);

            course.Title = "Khóa mẫu đã đổi tên";
            (await db.WritingAssignments.SingleAsync(assignment => assignment.Id == pendingId)).DueAtUtc = editedDeadline;
            var existingReview = await db.WritingSubmissions.SingleAsync(submission => submission.AssignmentId == reviewed.Id);
            existingReview.Feedback = "Giảng viên đã bổ sung nhận xét.";
            await db.SaveChangesAsync();
        }
        await SeedFullAsync();

        using var verification = factory.Services.CreateScope();
        var after = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await after.Courses.Where(course => course.OwnerTeacherUserId == teacherId).ToListAsync());
        Assert.Equal("Khóa mẫu đã đổi tên", await after.Courses.Where(course => course.Id == courseId)
            .Select(course => course.Title).SingleAsync());
        Assert.Equal(3, await after.WritingAssignments.CountAsync(assignment => assignment.CourseId == courseId));
        Assert.Equal(editedDeadline, await after.WritingAssignments.Where(assignment => assignment.Id == pendingId)
            .Select(assignment => assignment.DueAtUtc).SingleAsync());
        Assert.Equal(2, await after.WritingSubmissions.CountAsync(submission => submission.Assignment.CourseId == courseId));
        Assert.Contains(await after.WritingSubmissions.Where(submission => submission.Assignment.CourseId == courseId).ToListAsync(),
            submission => submission.Feedback == "Giảng viên đã bổ sung nhận xét.");
        Assert.Equal(2, await after.FlashcardReviews.CountAsync(review => review.StudentUserId == studentId));
        Assert.False(await after.Enrollments.AnyAsync(enrollment => enrollment.Course.IsPaid));
        Assert.Empty(await after.Payments.ToListAsync());

        async Task SeedFullAsync()
        {
            using var scope = factory.Services.CreateScope();
            await LearningDemoSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), IdentityTestHelpers.Password,
                scope.ServiceProvider.GetRequiredService<IStimulusAudioStore>());
        }
    }

    private static async Task<int[]> Counts(AppDbContext db) =>
    [
        await db.Users.CountAsync(), await db.Courses.CountAsync(), await db.Modules.CountAsync(),
        await db.Lessons.CountAsync(), await db.LessonResources.CountAsync(), await db.Enrollments.CountAsync(),
        await db.Assessments.CountAsync(), await db.AssessmentQuestions.CountAsync(), await db.Questions.CountAsync(),
        await db.QuestionOptions.CountAsync(), await db.Orders.CountAsync(), await db.Payments.CountAsync()
    ];
}

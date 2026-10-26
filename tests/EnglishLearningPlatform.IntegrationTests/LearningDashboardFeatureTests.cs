using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class LearningDashboardFeatureTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    private async Task<Data> SeedAsync(int practiceQuestionCount = 2)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { Title = "Reading course " + Guid.NewGuid().ToString("N"), OwnerTeacherUserId = teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        var module = new Module { Course = course, Title = "Visible module", Visibility = ModuleVisibility.Visible };
        var first = new Lesson { Module = module, Title = "First lesson", Status = LessonStatus.Published,
            Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Lesson content" }] };
        var second = new Lesson { Module = module, Title = "Second lesson", Status = LessonStatus.Published, OrderIndex = 1 };
        var enrollment = new Enrollment { StudentUserId = student.Id, Course = course, LastAccessedLesson = second };
        var practice = new Practice { Lesson = first, OwnerTeacherUserId = teacher.Id, Status = PracticeStatus.Published,
            Title = "Reading practice", Questions = Enumerable.Range(0, practiceQuestionCount).Select(i => new PracticeQuestion
            { OrderIndex = i, Question = Question(teacher.Id, EnglishSkill.Reading, i) }).ToList() };
        var assessment = new Assessment { Course = course, OwnerTeacherUserId = teacher.Id, Status = AssessmentStatus.Published,
            AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 10, PassingScore = 80,
            Title = "Reading assessment", Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion
            { OrderIndex = i, Points = 1, Question = Question(teacher.Id, i < 3 ? EnglishSkill.Reading : EnglishSkill.Grammar, i) }).ToList() };
        db.Enrollments.Add(enrollment);
        db.Lessons.AddRange(first, second);
        db.Practices.Add(practice);
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return new(teacher, student, course, module, first, second, enrollment, practice, assessment);
    }

    private static Question Question(Guid teacherId, EnglishSkill skill, int i) => new()
    {
        OwnerTeacherUserId = teacherId, PrimarySkill = skill, Status = QuestionStatus.Published, Content = "Question " + i,
        Explanation = "PRIVATE_PRACTICE_EXPLANATION", Options =
        [new QuestionOption { Content = "Correct " + i, IsCorrect = true }, new QuestionOption { Content = "Wrong " + i, OrderIndex = 1 }]
    };

    private static Attempt Graded(Data data, decimal overall, int correctReading, Guid? id = null, DateTimeOffset? finalized = null) => new()
    {
        Id = id ?? Guid.NewGuid(), StudentUserId = data.Student.Id, AssessmentId = data.Assessment.Id, Status = AttemptStatus.Graded,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20), DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        OverallScore = overall, Passed = overall >= 80, FinalizedAtUtc = finalized ?? DateTimeOffset.UtcNow.AddMinutes(-1),
        FinalizationReason = AttemptFinalizationReason.ManualSubmit,
        SkillResults = [new AttemptSkillResult { Skill = EnglishSkill.Reading, QuestionCount = 3, Score = Math.Round(correctReading * 100m / 3, 2) },
            new AttemptSkillResult { Skill = EnglishSkill.Grammar, QuestionCount = 2, Score = 0 }],
        Answers = data.Assessment.Questions.Where(q => q.Question.PrimarySkill == EnglishSkill.Reading).Take(correctReading)
            .Select(q => new AttemptAnswer { AssessmentQuestionId = q.Id, SelectedOptionId = q.Question.Options.Single(o => o.IsCorrect).Id }).ToList()
    };

    private static PracticeResponse[] Responses(Data data, bool correct = true) => data.Practice.Questions
        .Select(q => new PracticeResponse(q.Id, correct ? q.Question.Options.Single(o => o.IsCorrect).Id : null)).ToArray();

    [Fact]
    public async Task Dashboard_UsesRepresentativeAttemptWithSameSkillDataAndDeterministicTies()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var when = DateTimeOffset.UtcNow.AddMinutes(-1);
        var chosen = Graded(data, 80, 1, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), when);
        var pending = Graded(data, 100, 3);
        pending.Status = AttemptStatus.Submitted;
        var incomplete = Graded(data, 100, 3);
        incomplete.FinalizationReason = null;
        db.Attempts.AddRange(chosen, Graded(data, 80, 3, Guid.Parse("11111111-1111-1111-1111-111111111111"), when),
            Graded(data, 70, 3, finalized: when.AddMinutes(1)), Graded(data, 80, 3, finalized: when.AddMinutes(-1)),
            pending, incomplete);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id);
        var representative = Assert.Single(result.Value!.Results);
        Assert.Equal(chosen.Id, representative.AttemptId);
        Assert.Equal(80m, representative.OverallScore);
        var reading = Assert.Single(representative.Skills);
        Assert.Equal(EnglishSkill.Reading, reading.Skill);
        Assert.True(reading.IsWeak);
        var recommendation = Assert.Single(result.Value.Recommendations);
        Assert.Equal(chosen.Id, recommendation.AttemptId);
        Assert.Contains(recommendation.Contents, c => c.Id == data.Course.Id);
        Assert.Contains(recommendation.Contents, c => c.Id == data.Practice.Id);
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Null(db.Database.CurrentTransaction);

        // Cùng quy tắc tie-break phải đến được cả Dashboard lẫn Profile, không ghép Reading 100% từ lượt khác.
        using var client = await ClientAsync(data.Student);
        foreach (string path in new[] { "/Learning/Dashboard", "/Profile" })
        {
            string html = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains($"data-representative-attempt=\"{chosen.Id}\"", html);
            Assert.Contains($"/Assessment/Result/{chosen.Id}", html);
            Assert.DoesNotContain($"data-representative-attempt=\"{pending.Id}\"", html);
            Assert.DoesNotContain($"data-representative-attempt=\"{incomplete.Id}\"", html);
            Assert.Matches(@"Đọc hiểu\s*: 33[,.]33%", html);
            Assert.DoesNotMatch(@"Đọc hiểu\s*: 100%", html);
        }
        foreach (string path in new[] { "/Learning/Dashboard", "/Practice" })
        {
            string html = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains($"href=\"/Learning/Course/{data.Course.Id}\"", html);
            Assert.Contains($"href=\"/Practice/Take/{data.Practice.Id}\"", html);
            Assert.Contains("Căn cứ từ bài:", html);
        }
    }

    [Fact]
    public async Task DashboardAndProfile_KeepHighest85AndItsSkills_WhenLatestAttemptScores70()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var questions = await db.AssessmentQuestions.Where(q => q.AssessmentId == data.Assessment.Id)
            .Include(q => q.Question).ThenInclude(q => q.Options).OrderBy(q => q.OrderIndex).ToListAsync();
        // 12 câu, mỗi skill có đúng 3 câu; tổng vẫn là 20 điểm để kiểm tra 60/85/70%.
        decimal[] points = [3m, 2m, 1m, 3m, 2m, 1m, 3m, 2m, 1m, 1m, 0.5m, 0.5m];
        EnglishSkill[] skills = [EnglishSkill.Reading, EnglishSkill.Grammar, EnglishSkill.Listening, EnglishSkill.Vocabulary];
        var audio = new Stimulus { OwnerTeacherUserId = data.Teacher.Id, StimulusType = StimulusType.Audio,
            ResourceUrl = "https://example.com/r4-listening.mp3" };
        db.Stimuli.Add(audio);
        for (int index = 0; index < points.Length; index++)
        {
            if (index >= questions.Count)
            {
                var added = new AssessmentQuestion { AssessmentId = data.Assessment.Id, OrderIndex = index,
                    Question = Question(data.Teacher.Id, skills[index / 3], index) };
                db.AssessmentQuestions.Add(added);
                questions.Add(added);
            }
            questions[index].Points = points[index];
            questions[index].Question.PrimarySkill = skills[index / 3];
            if (skills[index / 3] == EnglishSkill.Listening)
                questions[index].Question.Stimulus = audio;
        }
        await db.SaveChangesAsync();

        var attempts = scope.ServiceProvider.GetRequiredService<IAttemptService>();
        // Có 3 bài đã chấm để bắt lỗi Profile vô tình chỉ hiện 2 bài giống preview Dashboard.
        var extraAttemptIds = new List<Guid>();
        for (int index = 0; index < 2; index++)
        {
            var extra = new Assessment { Title = $"R4 additional assessment {index}",
                OwnerTeacherUserId = data.Teacher.Id, CourseId = data.Course.Id, Status = AssessmentStatus.Published,
                AssessmentType = AssessmentType.PracticeExam, DurationMinutes = 15, MaxAttempts = 1,
                Questions = questions.Select(q => new AssessmentQuestion
                { QuestionId = q.QuestionId, OrderIndex = q.OrderIndex, Points = q.Points }).ToList() };
            db.Assessments.Add(extra);
            await db.SaveChangesAsync();
            var started = await attempts.StartAsync(data.Student.Id, extra.Id);
            Assert.Equal(AttemptStartStatus.Started, started.Status);
            var finalized = await attempts.FinalizeAsync(data.Student.Id, started.AttemptId!.Value);
            Assert.Equal(AttemptFinalizeStatus.Graded, finalized.Status);
            Assert.Equal(0m, finalized.OverallScore); // Điểm 0 thật khác với chưa có kết quả.
            extraAttemptIds.Add(started.AttemptId.Value);
        }

        int[][] correctQuestions = [[0, 1, 2, 3, 4, 5], [0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11],
            [1, 2, 4, 5, 6, 7, 8, 9, 10, 11]];
        decimal[] scores = [60m, 85m, 70m];
        var ids = new List<Guid>();
        for (int index = 0; index < correctQuestions.Length; index++)
        {
            var started = await attempts.StartAsync(data.Student.Id, data.Assessment.Id);
            Assert.Equal(AttemptStartStatus.Started, started.Status);
            Guid id = started.AttemptId!.Value;
            ids.Add(id);
            for (int questionIndex = 0; questionIndex < questions.Count; questionIndex++)
            {
                bool correct = correctQuestions[index].Contains(questionIndex);
                Guid optionId = questions[questionIndex].Question.Options.Single(option => option.IsCorrect == correct).Id;
                Assert.Equal(AttemptSaveStatus.Saved,
                    await attempts.SaveAnswerAsync(data.Student.Id, id, questions[questionIndex].Id, optionId));
            }
            var finalized = await attempts.FinalizeAsync(data.Student.Id, id);
            Assert.Equal(AttemptFinalizeStatus.Graded, finalized.Status);
            Assert.Equal(scores[index], finalized.OverallScore);
        }

        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var representatives = (await learning.GetRepresentativeResultsAsync(data.Student.Id)).Value!;
        Assert.Equal(3, representatives.Count);
        var highest = Assert.Single(representatives, result => result.AssessmentId == data.Assessment.Id);
        Assert.Equal(ids[1], highest.AttemptId);
        Assert.Equal(85m, highest.OverallScore);
        Assert.Equal(4, highest.Skills.Count);
        Assert.All(highest.Skills, skill => Assert.Equal(3, skill.QuestionCount));
        var reading = Assert.Single(highest.Skills, skill => skill.Skill == EnglishSkill.Reading);
        Assert.Equal(EnglishSkill.Reading, reading.Skill);
        Assert.Equal(100m, reading.Score);
        // Lượt 85% có Listening 50%; lượt 70% có Listening 100% nhưng không được ghép vào đại diện.
        Assert.Equal(50m, highest.Skills.Single(skill => skill.Skill == EnglishSkill.Listening).Score);
        Assert.Equal(50m, await db.AttemptSkillResults.Where(s => s.AttemptId == ids[2] && s.Skill == EnglishSkill.Reading)
            .Select(s => s.Score).SingleAsync());

        using var client = await ClientAsync(data.Student);
        foreach (string path in new[] { "/Learning/Dashboard", "/Profile" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            string responseHtml = await response.Content.ReadAsStringAsync();
            string html = WebUtility.HtmlDecode(responseHtml);
            Assert.Contains($"data-representative-attempt=\"{ids[1]}\"", html);
            Assert.DoesNotContain($"data-representative-attempt=\"{ids[2]}\"", html);
            Assert.Matches(@">85%<", html);
            Assert.Matches(@"Đọc hiểu\s*: 100%", html);
            Assert.Matches(@"Nghe hiểu\s*: 50%", html);
            Assert.Contains("Từ vựng", html);
            Assert.Contains("Ngữ pháp", html);
            int shownResults = System.Text.RegularExpressions.Regex.Matches(html, "data-representative-attempt=").Count;
            Assert.Equal(path == "/Profile" ? 3 : 2, shownResults);
            if (path == "/Profile")
            {
                foreach (Guid extraId in extraAttemptIds)
                    Assert.Contains($"/Assessment/Result/{extraId}", html);
                Assert.Contains(">0%<", html); // Vẫn hiển thị các bài đã chấm thật sự đạt 0%.
            }
            Assert.DoesNotContain("PRIVATE_PRACTICE_EXPLANATION", html);

            // Chỉ xuất HTML dữ liệu giả khi được yêu cầu để kiểm tra responsive, không lưu cookie/mật khẩu.
            string? exportDirectory = Environment.GetEnvironmentVariable("ELP_R4_UI_EXPORT");
            if (!string.IsNullOrWhiteSpace(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
                string safeHtml = System.Text.RegularExpressions.Regex.Replace(responseHtml,
                    "(name=\"__RequestVerificationToken\"[^>]*value=\")[^\"]*", "$1REDACTED");
                await File.WriteAllTextAsync(Path.Combine(exportDirectory, path == "/Profile" ? "profile.html" : "dashboard.html"), safeHtml);
            }
        }

        // Profile lỗi validation phải nạp lại kết quả từ server, bỏ qua điểm giả từ form.
        string token = await IdentityTestHelpers.GetTokenAsync(client, "/Profile");
        using var invalidProfile = await client.PostAsync("/Profile", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "", ["AssessmentResults[0].OverallScore"] = "100",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, invalidProfile.StatusCode);
        Assert.Contains($"data-representative-attempt=\"{ids[1]}\"", await invalidProfile.Content.ReadAsStringAsync());
        Assert.Equal(3, await db.Attempts.CountAsync(a => a.AssessmentId == data.Assessment.Id));

        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var outsider = await ClientAsync(other);
        foreach (string path in new[] { "/Learning/Dashboard", "/Profile" })
        {
            string html = await outsider.GetStringAsync($"{path}?studentId={data.Student.Id}");
            Assert.DoesNotContain($"data-representative-attempt=\"{ids[1]}\"", html);
        }
    }

    [Fact]
    public async Task Dashboard_RawWeightedScoreBelow60StillWeakWhenStoredDisplayScoreRoundsTo60()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reading = data.Assessment.Questions.Where(q => q.Question.PrimarySkill == EnglishSkill.Reading).ToArray();
        await db.AssessmentQuestions.Where(q => q.Id == reading[0].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 59999m));
        await db.AssessmentQuestions.Where(q => q.Id == reading[1].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 20000m));
        await db.AssessmentQuestions.Where(q => q.Id == reading[2].Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 20001m));
        var attempt = Graded(data, 60, 1);
        attempt.SkillResults.Single(s => s.Skill == EnglishSkill.Reading).Score = 60;
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id);
        Assert.True(Assert.Single(Assert.Single(result.Value!.Results).Skills).IsWeak);
        Assert.Single(result.Value.Recommendations);
    }

    [Fact]
    public async Task Recommendation_UsesMetadataAndExcludesUnenrolledArchivedDraftHiddenOrForeignOwnerContent()
    {
        var data = await SeedAsync();
        var foreignTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attempts.Add(Graded(data, 20, 0));
        var grammar = new Course { Title = "English Reading in name only", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Grammar }] };
        var archived = new Course { Title = "Archived Reading", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Archived, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        var unenrolled = new Course { Title = "Unenrolled Reading", OwnerTeacherUserId = data.Teacher.Id,
            Status = CourseStatus.Published, Skills = [new CourseSkill { Skill = EnglishSkill.Reading }] };
        db.Enrollments.AddRange(new Enrollment { Course = grammar, StudentUserId = data.Student.Id }, new Enrollment { Course = archived, StudentUserId = data.Student.Id });
        db.Courses.Add(unenrolled);
        foreach (var course in new[] { grammar, archived, unenrolled })
            db.Lessons.Add(new Lesson { Title = "Available lesson", Status = LessonStatus.Published,
                Module = new Module { Course = course, Title = "Module", Visibility = ModuleVisibility.Visible } });
        var hiddenModule = new Module { CourseId = data.Course.Id, Title = "Hidden", OrderIndex = 1, Visibility = ModuleVisibility.Hidden };
        var hidden = new Practice { Module = hiddenModule, Title = "Hidden practice", OwnerTeacherUserId = data.Teacher.Id,
            Status = PracticeStatus.Published, Questions = [new PracticeQuestion { Question = Question(data.Teacher.Id, EnglishSkill.Reading, 0) }] };
        var foreign = new Practice { LessonId = data.First.Id, Title = "Foreign practice", OwnerTeacherUserId = foreignTeacher.Id,
            Status = PracticeStatus.Published, Questions = [new PracticeQuestion { Question = Question(foreignTeacher.Id, EnglishSkill.Reading, 0) }] };
        var draft = new Assessment { CourseId = data.Course.Id, OwnerTeacherUserId = data.Teacher.Id, Title = "Draft assessment", Status = AssessmentStatus.Draft,
            AssessmentType = AssessmentType.PlacementTest, DurationMinutes = 15, MaxAttempts = 1 };
        db.Practices.AddRange(hidden, foreign);
        db.Assessments.Add(draft);
        await db.SaveChangesAsync();
        var recommendation = Assert.Single((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!.Recommendations);
        Assert.DoesNotContain(recommendation.Contents, c => new[] { grammar.Id, archived.Id, unenrolled.Id, hidden.Id, foreign.Id, draft.Id }.Contains(c.Id));
        var practice = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Null((await practice.GetSheetAsync(data.Student.Id, foreign.Id)).Value);
        Assert.Null((await practice.GetSheetAsync(data.Student.Id, hidden.Id)).Value);
    }

    [Fact]
    public async Task Dashboard_WithoutEvidenceDoesNotInventScoresOrRecommendations_AndScopesToStudent()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var dashboard = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.Single(dashboard.Courses);
        Assert.Empty(dashboard.Results);
        Assert.Empty(dashboard.Recommendations);
        var foreign = (await learning.DashboardAsync(other.Id)).Value!;
        Assert.Empty(foreign.Courses);
        Assert.Empty(foreign.Results);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.ActivitiesAsync(other.Id, data.Course.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.DashboardAsync(data.Teacher.Id)).Access);

        // Chỉ có lượt đang làm vẫn là "Chưa có kết quả", không phải representative 0%.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attempts.Add(new Attempt { StudentUserId = data.Student.Id, AssessmentId = data.Assessment.Id,
            Status = AttemptStatus.InProgress, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync();
        Assert.Empty((await learning.GetRepresentativeResultsAsync(data.Student.Id)).Value!);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.GetRepresentativeResultsAsync(data.Teacher.Id)).Access);
        using var client = await ClientAsync(data.Student);
        foreach (string path in new[] { "/Learning/Dashboard", "/Profile" })
        {
            string html = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains("Chưa có kết quả bài kiểm tra năng lực.", html);
            Assert.DoesNotContain("data-representative-attempt=", html);
        }
        using var outsider = await ClientAsync(other);
        string profile = await outsider.GetStringAsync($"/Profile?studentId={data.Student.Id}");
        Assert.DoesNotContain("data-representative-attempt=", profile);
        Assert.DoesNotContain(data.Assessment.Title, profile);
    }

    [Fact]
    public async Task Dashboard_ScoreExactly60IsNotWeak_AndMissingMatchingMetadataShowsEmptyRecommendation()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = data.Assessment.Questions.First(q => q.Question.PrimarySkill == EnglishSkill.Reading);
        await db.AssessmentQuestions.Where(q => q.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(q => q.Points, 3m));
        var attempt = Graded(data, 60, 1);
        attempt.SkillResults.Single(s => s.Skill == EnglishSkill.Reading).Score = 60;
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var result = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.False(Assert.Single(Assert.Single(result.Results).Skills).IsWeak);
        Assert.Empty(result.Recommendations);

        // Make the representative weak, then withdraw every matching piece of content.
        await db.AttemptAnswers.Where(a => a.AttemptId == attempt.Id).ExecuteDeleteAsync();
        await db.AttemptSkillResults.Where(s => s.AttemptId == attempt.Id && s.Skill == EnglishSkill.Reading)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Score, 0m));
        await db.CourseSkills.Where(s => s.CourseId == data.Course.Id).ExecuteDeleteAsync();
        await db.Practices.Where(p => p.Id == data.Practice.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, PracticeStatus.Unpublished));
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AssessmentStatus.Unpublished));
        result = (await learning.DashboardAsync(data.Student.Id)).Value!;
        Assert.Empty(Assert.Single(result.Recommendations).Contents);
    }

    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Locked)]
    public async Task Services_RecheckStudentAccountStatusBeforeReadingOrWriting(AccountStatus status)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Id == data.Student.Id).ExecuteUpdateAsync(u => u.SetProperty(u => u.AccountStatus, status));
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.DashboardAsync(data.Student.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.PracticeHubAsync(data.Student.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.AssessmentsAsync(data.Student.Id)).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.GetRepresentativeResultsAsync(data.Student.Id)).Access);
        var practice = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Equal(LearningAccessResult.Forbidden, (await practice.GetSheetAsync(data.Student.Id, data.Practice.Id)).Access);
        Assert.Equal(PracticeSubmitStatus.Forbidden, (await practice.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data))).Status);
        Assert.Equal(LearningAccessResult.Forbidden, (await practice.GetResultAsync(data.Student.Id, data.Practice.Id)).Access);
    }

    [Fact]
    public async Task Continue_RechecksEffectiveLessonAndEmptyCourse_AndRetainsHistoricalCompletion()
    {
        var data = await SeedAsync();
        using var client = await ClientAsync(data.Student);
        using var first = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Lesson/{data.Second.Id}", first.Headers.Location!.ToString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Lessons.Where(l => l.Id == data.Second.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, LessonStatus.Hidden));
        using var fallback = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Lesson/{data.First.Id}", fallback.Headers.Location!.ToString());
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.CompletedAtUtc, DateTimeOffset.UtcNow));
        await db.Lessons.Where(l => l.Id == data.First.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, LessonStatus.Hidden));
        using var empty = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal($"/Learning/Course/{data.Course.Id}", empty.Headers.Location!.ToString());
        var dashboard = (await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!;
        Assert.Null(Assert.Single(dashboard.Courses).Progress!.Percentage);
        Assert.Equal(1, dashboard.CompletedCourses);
        Assert.Equal(data.Second.Id, await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).Select(e => e.LastAccessedLessonId).SingleAsync());
    }

    [Fact]
    public async Task DashboardAndPracticeHub_UseRealPracticeResultsAndWeeklyCalendarActivities()
    {
        var data = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var gradedAttempt = Graded(data, 80, 2, finalized: now);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LessonProgressEntries.Add(new LessonProgress
            {
                EnrollmentId = data.Enrollment.Id, LessonId = data.First.Id, IsCompleted = true, UpdatedAtUtc = now
            });
            db.Attempts.Add(gradedAttempt);
            await db.SaveChangesAsync();
            Assert.Equal(PracticeSubmitStatus.Saved, (await scope.ServiceProvider
                .GetRequiredService<IPracticeService>().SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data))).Status);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
            var dashboard = (await learning.DashboardAsync(data.Student.Id)).Value!;
            var practiceResult = Assert.Single(dashboard.PracticeResults);
            Assert.Equal(data.Practice.Id, practiceResult.PracticeId);
            Assert.Equal(data.Practice.Title, practiceResult.Title);
            Assert.Equal(DayOfWeek.Monday, dashboard.WeekStartDate.DayOfWeek);
            Assert.Contains(dashboard.WeeklyActivities, activity =>
                activity.Kind == DashboardActivityKind.LessonCompleted && activity.TargetId == data.First.Id);
            Assert.Contains(dashboard.WeeklyActivities, activity =>
                activity.Kind == DashboardActivityKind.PracticeSubmitted && activity.TargetId == data.Practice.Id);
            Assert.Contains(dashboard.WeeklyActivities, activity =>
                activity.Kind == DashboardActivityKind.AssessmentFinalized && activity.TargetId == gradedAttempt.Id);
            Assert.All(dashboard.WeeklyActivities, activity =>
            {
                var localDate = DateOnly.FromDateTime(activity.OccurredAtUtc.ToOffset(TimeSpan.FromHours(7)).DateTime);
                Assert.InRange(localDate, dashboard.WeekStartDate, dashboard.WeekStartDate.AddDays(6));
            });

            var hub = (await learning.PracticeHubAsync(data.Student.Id)).Value!;
            var practiceLink = Assert.Single(hub.Practices);
            Assert.Equal(data.Practice.Title, practiceLink.Title);
            Assert.Equal(data.Course.Title, practiceLink.CourseTitle);
            Assert.Single(hub.Results);
        }

        using var client = await ClientAsync(data.Student);
        string dashboardHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(dashboardHtml, "data-calendar-day=").Count);
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(dashboardHtml, "data-calendar-panel=").Count);
        Assert.Contains("Luyện tập gần đây", dashboardHtml);
        Assert.Contains(data.Practice.Title, dashboardHtml);
        Assert.Contains("Xem tất cả (1)", dashboardHtml);
        Assert.Contains($"href=\"/Practice/Result/{data.Practice.Id}\"", dashboardHtml);
        // Dashboard không còn thẻ bài viết mẫu không có dữ liệu thật.
        Assert.DoesNotContain("Mastering Vocabulary", dashboardHtml);
        Assert.DoesNotContain("Embarking on English", dashboardHtml);

        using var hubResponse = await client.GetAsync("/Practice");
        Assert.Equal(HttpStatusCode.OK, hubResponse.StatusCode);
        string hubHtml = WebUtility.HtmlDecode(await hubResponse.Content.ReadAsStringAsync());
        Assert.Contains("Kết quả luyện tập gần đây", hubHtml);
        Assert.Contains(data.Practice.Title, hubHtml);
        Assert.Contains(data.Course.Title, hubHtml);
        Assert.Contains($"href=\"/Practice/Take/{data.Practice.Id}\"", hubHtml);
        Assert.Contains($"href=\"/Practice/Result/{data.Practice.Id}\"", hubHtml);
    }

    [Fact]
    public async Task CompletedLessons_ShowFinalAssessmentCta_AndContinueGoesToCourse()
    {
        var data = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Courses.Where(c => c.Id == data.Course.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.FinalAssessmentId, data.Assessment.Id));
            db.LessonProgressEntries.AddRange(
                new LessonProgress { EnrollmentId = data.Enrollment.Id, LessonId = data.First.Id, IsCompleted = true, UpdatedAtUtc = now },
                new LessonProgress { EnrollmentId = data.Enrollment.Id, LessonId = data.Second.Id, IsCompleted = true, UpdatedAtUtc = now });
            await db.SaveChangesAsync();
        }

        using var client = await ClientAsync(data.Student);
        using var lesson = await client.GetAsync($"/Learning/Lesson/{data.Second.Id}");
        Assert.Equal(HttpStatusCode.OK, lesson.StatusCode);
        string lessonHtml = WebUtility.HtmlDecode(await lesson.Content.ReadAsStringAsync());
        Assert.Contains("Làm bài thi cuối khóa", lessonHtml);
        Assert.Contains($"/Assessment/Take/{data.Assessment.Id}", lessonHtml);

        string courseHtml = WebUtility.HtmlDecode(await client.GetStringAsync($"/Learning/Course/{data.Course.Id}"));
        Assert.Contains("Bạn đã hoàn thành tất cả bài học.", courseHtml);
        Assert.Contains("Hãy làm bài thi cuối khóa để hoàn tất khóa học.", courseHtml);

        // Continue không được fallback về bài đầu khi mọi lesson đã hoàn thành.
        using var continuation = await client.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Equal(HttpStatusCode.Redirect, continuation.StatusCode);
        Assert.Equal($"/Learning/Course/{data.Course.Id}", continuation.Headers.Location!.ToString());

        string dashboardHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        Assert.Contains("Hoàn tất khóa", dashboardHtml);
        Assert.Contains($"href=\"/Learning/Course/{data.Course.Id}\"", dashboardHtml);
        Assert.DoesNotContain($"href=\"/Learning/Continue/{data.Course.Id}\"", dashboardHtml);
        string libraryHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning"));
        Assert.Contains($"href=\"/Learning/Continue/{data.Course.Id}\"", libraryHtml);
    }

    [Fact]
    public async Task DashboardAndPracticeHub_KeepOlderFeedbackWhenItsLessonIsHidden()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newerPractice = new Practice
        {
            ModuleId = data.Module.Id, OwnerTeacherUserId = data.Teacher.Id,
            Title = "Module practice after lesson practice", Status = PracticeStatus.Published,
            Questions = Enumerable.Range(0, 2).Select(i => new PracticeQuestion
                { OrderIndex = i, Question = Question(data.Teacher.Id, EnglishSkill.Reading, i) }).ToList()
        };
        db.Practices.Add(newerPractice);
        await db.SaveChangesAsync();
        var practices = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Equal(PracticeSubmitStatus.Saved,
            (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data))).Status);
        var answers = newerPractice.Questions.Select(q =>
            new PracticeResponse(q.Id, q.Question.Options.Single(o => o.IsCorrect).Id)).ToArray();
        Assert.Equal(PracticeSubmitStatus.Saved,
            (await practices.SubmitAsync(data.Student.Id, newerPractice.Id, answers)).Status);
        Assert.Equal(LearningAccessResult.Allowed, (await scope.ServiceProvider
            .GetRequiredService<ICourseAuthoringService>().SaveContentAsync(data.Teacher.Id, data.Course.Id,
                ContentKind.Lesson, data.Module.Id, data.First.Id,
                new ContentInput { Title = data.First.Title, Status = LessonStatus.Hidden })).Access);

        var hub = (await scope.ServiceProvider.GetRequiredService<IStudentLearningService>()
            .PracticeHubAsync(data.Student.Id)).Value!;
        Assert.Equal(newerPractice.Id, Assert.Single(hub.Practices).Id);
        Assert.Equal(2, hub.Results.Count);
        Assert.Contains(hub.Results, r => r.PracticeId == data.Practice.Id);
        Assert.Equal(LearningAccessResult.Allowed,
            (await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Access);
        using var client = await ClientAsync(data.Student);
        foreach (var path in new[] { "/Learning/Dashboard", "/Practice" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains($"href=\"/Practice/Result/{data.Practice.Id}\"", html);
            Assert.Contains($"href=\"/Practice/Result/{newerPractice.Id}\"", html);
            Assert.DoesNotContain($"href=\"/Practice/Take/{data.Practice.Id}\"", html);
        }
    }

    [Fact]
    public async Task Dashboard_ResumesNewLessonsAfterHistoricalCourseCompletion()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<IStudentLearningCourseService>();
        Assert.Equal(LearningAccessResult.Allowed,
            (await courses.SetLessonCompletedAsync(data.Student.Id, data.First.Id, true)).Access);
        Assert.Equal(LearningAccessResult.Allowed,
            (await courses.SetLessonCompletedAsync(data.Student.Id, data.Second.Id, true)).Access);
        var completedAt = (await courses.OpenCourseAsync(data.Student.Id, data.Course.Id, false)).Value!.Progress!.CompletedAtUtc;
        Assert.NotNull(completedAt);

        var authoring = scope.ServiceProvider.GetRequiredService<ICourseAuthoringService>();
        var lesson = await authoring.SaveContentAsync(data.Teacher.Id, data.Course.Id,
            ContentKind.Lesson, data.Module.Id, null,
            new ContentInput { Title = "New lesson after completion", Status = LessonStatus.Draft });
        Assert.Equal(LearningAccessResult.Allowed, lesson.Access);
        Assert.Equal(LearningAccessResult.Allowed, (await authoring.SaveContentAsync(data.Teacher.Id,
            data.Course.Id, ContentKind.Resource, lesson.Id, null,
            new ContentInput { ResourceType = LessonResourceType.Text, ContentText = "New lesson content" })).Access);
        Assert.Equal(LearningAccessResult.Allowed, (await authoring.SaveContentAsync(data.Teacher.Id,
            data.Course.Id, ContentKind.Lesson, data.Module.Id, lesson.Id,
            new ContentInput { Title = "New lesson after completion", Status = LessonStatus.Published })).Access);

        var dashboard = (await scope.ServiceProvider.GetRequiredService<IStudentLearningService>()
            .DashboardAsync(data.Student.Id)).Value!;
        var progress = Assert.Single(dashboard.Courses).Progress!;
        Assert.Equal(2, progress.CompletedLessons);
        Assert.Equal(3, progress.TotalLessons);
        Assert.Equal(completedAt, progress.CompletedAtUtc);

        using var client = await ClientAsync(data.Student);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        var heroStart = html.IndexOf("hero-promo-banner", StringComparison.Ordinal);
        var heroEnd = html.IndexOf("</section>", heroStart, StringComparison.Ordinal);
        var hero = html[heroStart..heroEnd];
        Assert.Contains($"/Learning/Continue/{data.Course.Id}", hero);
        Assert.Contains(data.Course.Title, hero);
        var libraryHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning"));
        Assert.Contains($"href=\"/Learning/Continue/{data.Course.Id}\"", libraryHtml);

        // Once current lessons are complete, retain the historical date and stop offering resume.
        Assert.Equal(LearningAccessResult.Allowed,
            (await courses.SetLessonCompletedAsync(data.Student.Id, lesson.Id, true)).Access);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        Assert.DoesNotContain($"/Learning/Continue/{data.Course.Id}", html);
        libraryHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning"));
        Assert.DoesNotContain($"href=\"/Learning/Continue/{data.Course.Id}\"", libraryHtml);
        Assert.Equal(completedAt,
            (await courses.OpenCourseAsync(data.Student.Id, data.Course.Id, false)).Value!.Progress!.CompletedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_CalendarKeepsHistoryWithoutLinkToHiddenLessonOrModule(bool hideModule)
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var courses = scope.ServiceProvider.GetRequiredService<IStudentLearningCourseService>();
        Assert.Equal(LearningAccessResult.Allowed,
            (await courses.SetLessonCompletedAsync(data.Student.Id, data.First.Id, true)).Access);
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var activity = Assert.Single((await learning.DashboardAsync(data.Student.Id)).Value!.WeeklyActivities);
        Assert.Equal(data.First.Id, activity.TargetId);
        Assert.True(activity.CanOpenTarget);

        var authoring = scope.ServiceProvider.GetRequiredService<ICourseAuthoringService>();
        var kind = hideModule ? ContentKind.Module : ContentKind.Lesson;
        var parentId = hideModule ? data.Course.Id : data.Module.Id;
        var contentId = hideModule ? data.Module.Id : data.First.Id;
        var input = hideModule
            ? new ContentInput { Title = data.Module.Title, Visibility = ModuleVisibility.Hidden }
            : new ContentInput { Title = data.First.Title, Status = LessonStatus.Hidden };
        Assert.Equal(LearningAccessResult.Allowed,
            (await authoring.SaveContentAsync(data.Teacher.Id, data.Course.Id, kind, parentId, contentId, input)).Access);

        activity = Assert.Single((await learning.DashboardAsync(data.Student.Id)).Value!.WeeklyActivities);
        Assert.Equal(data.First.Title, activity.Title);
        Assert.False(activity.CanOpenTarget);
        using var client = await ClientAsync(data.Student);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        Assert.Contains(data.First.Title, html);
        Assert.Contains("Bài học hiện không khả dụng", html);
        Assert.DoesNotContain($"href=\"/Learning/Lesson/{data.First.Id}\"", html);
        using var unavailable = await client.GetAsync($"/Learning/Lesson/{data.First.Id}");
        Assert.Contains("/Account/AccessDenied", unavailable.Headers.Location!.ToString());

        input.Visibility = ModuleVisibility.Visible;
        input.Status = LessonStatus.Published;
        Assert.Equal(LearningAccessResult.Allowed,
            (await authoring.SaveContentAsync(data.Teacher.Id, data.Course.Id, kind, parentId, contentId, input)).Access);
        activity = Assert.Single((await learning.DashboardAsync(data.Student.Id)).Value!.WeeklyActivities);
        Assert.True(activity.CanOpenTarget);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        Assert.Contains($"href=\"/Learning/Lesson/{data.First.Id}\"", html);
    }

    [Fact]
    public async Task Dashboard_ZeroLessonCourseShowsNoLessonProgressInsteadOfZeroPercent()
    {
        var data = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Enrollments.Add(new Enrollment
            {
                StudentUserId = data.Student.Id,
                Course = new Course
                {
                    Title = "Empty course " + Guid.NewGuid().ToString("N"),
                    OwnerTeacherUserId = data.Teacher.Id,
                    Status = CourseStatus.Published
                }
            });
            await db.SaveChangesAsync();
        }

        using var client = await ClientAsync(data.Student);
        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/Learning/Dashboard"));
        int noLessonCards = System.Text.RegularExpressions.Regex.Matches(html, "Chưa có bài học").Count;
        Assert.Equal(1, noLessonCards);
        Assert.Contains("<strong>—</strong>", html);
        Assert.DoesNotContain("0/0 bài", html);
    }

    [Fact]
    public async Task Activities_ListsIndependentAndLinkedAssessments_AndResumesWithdrawalWithoutNewPractice()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var independent = new Assessment { Title = "Independent", OwnerTeacherUserId = data.Teacher.Id, DurationMinutes = 10, MaxAttempts = 1,
            Status = AssessmentStatus.Published, AssessmentType = AssessmentType.PlacementTest,
            Questions = Enumerable.Range(0, 5).Select(i => new AssessmentQuestion { OrderIndex = i, Points = 1,
                Question = Question(data.Teacher.Id, EnglishSkill.Reading, i) }).ToList() };
        db.Assessments.Add(independent);
        var attempt = new Attempt { StudentUserId = data.Student.Id, AssessmentId = data.Assessment.Id,
            Status = AttemptStatus.InProgress, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        var otherList = (await learning.AssessmentsAsync(other.Id)).Value!;
        Assert.Contains(otherList, a => a.Id == independent.Id);
        Assert.DoesNotContain(otherList, a => a.Id == data.Assessment.Id);
        var activities = (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!;
        Assert.Equal(data.Practice.Id, Assert.Single(activities.Practices).Id);
        Assert.Equal(data.Assessment.Id, Assert.Single(activities.Assessments).Id);
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CourseStatus.Archived));
        await db.Assessments.Where(a => a.Id == data.Assessment.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AssessmentStatus.Unpublished));
        activities = (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!;
        Assert.Empty(activities.Practices);
        Assert.Equal(attempt.Id, Assert.Single(activities.Assessments).ActiveAttemptId);
        await db.Attempts.Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.DeadlineUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Empty((await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Value!.Assessments);
    }

    [Fact]
    public async Task InvalidPaidEnrollmentCannotAccessDashboardCourseActivitiesOrPractice()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = Receipt(data, PaymentStatus.Failed, 100);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.PaymentId, payment.Id));
        var learning = scope.ServiceProvider.GetRequiredService<IStudentLearningService>();
        Assert.Empty((await learning.DashboardAsync(data.Student.Id)).Value!.Courses);
        Assert.Equal(LearningAccessResult.Forbidden, (await learning.ActivitiesAsync(data.Student.Id, data.Course.Id)).Access);
        Assert.Null((await scope.ServiceProvider.GetRequiredService<IPracticeService>().GetSheetAsync(data.Student.Id, data.Practice.Id)).Value);
    }

    [Fact]
    public async Task Practice_RejectsForeignOptionsAndDuplicates_StoresLatestResultAndKeepsHistoricalFeedback()
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var practices = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        var sheet = (await practices.GetSheetAsync(data.Student.Id, data.Practice.Id)).Value!;
        Assert.DoesNotContain("IsCorrect", JsonSerializer.Serialize(sheet));
        Assert.DoesNotContain("PRIVATE_PRACTICE_EXPLANATION", JsonSerializer.Serialize(sheet));
        Assert.False(sheet.HasResult);
        var valid = Responses(data);
        var bad = valid.ToArray();
        bad[0] = bad[0] with { SelectedOptionId = valid[1].SelectedOptionId };
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, bad)).Status);
        Assert.Equal(PracticeSubmitStatus.InvalidAnswers, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, [valid[0], valid[0]])).Status);
        Assert.Equal(PracticeSubmitStatus.Unavailable, (await practices.SubmitAsync(other.Id, data.Practice.Id, valid)).Status);
        Assert.Equal(PracticeSubmitStatus.Saved, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, valid)).Status);
        var first = (await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!;
        Assert.Equal(100m, first.Score);
        Assert.True((await practices.GetSheetAsync(data.Student.Id, data.Practice.Id)).Value!.HasResult);
        Assert.All(first.Questions, q => Assert.True(q.IsCorrect));
        Assert.Null((await practices.GetResultAsync(other.Id, data.Practice.Id)).Value);
        Assert.Equal(PracticeSubmitStatus.Saved, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data, false))).Status);
        Assert.Equal(0m, (await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!.Score);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PracticeSubmissions.CountAsync(s => s.PracticeId == data.Practice.Id));
        Assert.Empty(await db.PracticeAnswers.Where(a => a.PracticeSubmission.PracticeId == data.Practice.Id).ToListAsync());
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, CourseStatus.Archived));
        Assert.Equal(PracticeSubmitStatus.Unavailable, (await practices.SubmitAsync(data.Student.Id, data.Practice.Id, valid)).Status);
        Assert.NotNull((await practices.GetResultAsync(data.Student.Id, data.Practice.Id)).Value);
        Assert.Single((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(data.Student.Id)).Value!.PracticeResults);
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IStudentLearningService>().DashboardAsync(other.Id)).Value!.PracticeResults);
    }

    [Theory]
    [InlineData(StimulusType.Passage)]
    [InlineData(StimulusType.Audio)]
    public async Task Practice_ResultKeepsStimulusContextAndPrivacyAfterWithdrawal(StimulusType stimulusType)
    {
        var data = await SeedAsync();
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string passage = "Practice context <script>alert('context')</script> & reading.";
        var audioBytes = StimulusAudioTestData.Wav();
        // A distinct hash prevents another test's content from granting unrelated audio access.
        Guid.NewGuid().ToByteArray().CopyTo(audioBytes, audioBytes.Length - 16);
        var created = await scope.ServiceProvider.GetRequiredService<IStimulusCreationService>().CreateAsync(
            data.Teacher.Id, new CreateStimulusRequest
            {
                StimulusType = stimulusType,
                ContentText = stimulusType == StimulusType.Passage ? passage : null,
                AudioContent = stimulusType == StimulusType.Audio ? audioBytes : null
            });
        Assert.True(created.Success);
        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.Id == created.Id);
        var practice = new Practice
        {
            OwnerTeacherUserId = data.Teacher.Id, Title = "Practice result stimulus context",
            Status = PracticeStatus.Draft,
            Questions = Enumerable.Range(0, 2).Select(i =>
            {
                var question = Question(data.Teacher.Id,
                    stimulusType == StimulusType.Audio ? EnglishSkill.Listening : EnglishSkill.Reading, i);
                question.StimulusId = stimulus.Id;
                return new PracticeQuestion { OrderIndex = i, Question = question };
            }).ToList()
        };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        var authoring = scope.ServiceProvider.GetRequiredService<ITeacherPracticeQuestionService>();
        Assert.True((await authoring.PublishAsync(data.Teacher.Id, practice.Id, data.First.Id, null,
            practice.UpdatedAtUtc)).Success);
        Assert.Equal(LearningAccessResult.Allowed, (await scope.ServiceProvider
            .GetRequiredService<IStudentLearningCourseService>().EnrollFreeAsync(other.Id, data.Course.Id)).Access);
        var answers = practice.Questions.Select(q =>
            new PracticeResponse(q.Id, q.Question.Options.Single(o => o.IsCorrect).Id)).ToArray();
        Assert.Equal(PracticeSubmitStatus.Saved, (await scope.ServiceProvider.GetRequiredService<IPracticeService>()
            .SubmitAsync(data.Student.Id, practice.Id, answers)).Status);

        using var client = await ClientAsync(data.Student);
        foreach (var withdrawn in new[] { false, true })
        {
            if (withdrawn)
            {
                var version = await db.Practices.AsNoTracking().Where(p => p.Id == practice.Id)
                    .Select(p => p.UpdatedAtUtc).SingleAsync();
                Assert.True((await authoring.UnpublishAsync(data.Teacher.Id, practice.Id, version)).Success);
            }
            using var result = await client.GetAsync($"/Practice/Result/{practice.Id}");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var html = await result.Content.ReadAsStringAsync();
            if (stimulusType == StimulusType.Passage)
            {
                Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html,
                    System.Text.RegularExpressions.Regex.Escape(HtmlEncoder.Default.Encode(passage))).Count);
                Assert.DoesNotContain(passage, html);
            }
            else
            {
                Assert.True(StimulusAudioUrl.TryGetHash(stimulus.ResourceUrl, out _));
                Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html,
                    System.Text.RegularExpressions.Regex.Escape($"src=\"{stimulus.ResourceUrl}\"")).Count);
                Assert.DoesNotContain("Audio từ máy chủ bên ngoài", WebUtility.HtmlDecode(html));
            }
        }

        using var outsider = await ClientAsync(other);
        using var deniedResult = await outsider.GetAsync($"/Practice/Result/{practice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedResult.StatusCode);
        if (stimulusType == StimulusType.Audio)
        {
            using var audio = await client.GetAsync(stimulus.ResourceUrl);
            Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
            Assert.Equal(audioBytes, await audio.Content.ReadAsByteArrayAsync());
            using var request = new HttpRequestMessage(HttpMethod.Get, stimulus.ResourceUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
            using var range = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal(audioBytes.Take(16).ToArray(), await range.Content.ReadAsByteArrayAsync());
            using var deniedAudio = await outsider.GetAsync(stimulus.ResourceUrl);
            Assert.Equal(HttpStatusCode.NotFound, deniedAudio.StatusCode);
        }
    }

    [Fact]
    public async Task Practice_MixedCorrectWrongAndBlankAnswers_UseEqualWeightsAndRetakeReplacesLatestResult()
    {
        // Tạo đủ 3 câu ngay trong fixture, không thêm/sửa đề sau khi Published.
        var data = await SeedAsync(practiceQuestionCount: 3);
        var questions = data.Practice.Questions.OrderBy(q => q.OrderIndex).ToArray();
        Guid firstCorrect = questions[0].Question.Options.Single(o => o.IsCorrect).Id;
        Guid secondWrong = questions[1].Question.Options.Single(o => !o.IsCorrect).Id;
        var mixedAnswers = new[]
        {
            new PracticeResponse(questions[0].Id, firstCorrect),
            new PracticeResponse(questions[1].Id, secondWrong),
            new PracticeResponse(questions[2].Id, null)
        };

        Guid submissionId;
        using (var firstScope = factory.Services.CreateScope())
        {
            var service = firstScope.ServiceProvider.GetRequiredService<IPracticeService>();
            var submitted = await service.SubmitAsync(data.Student.Id, data.Practice.Id, mixedAnswers);
            Assert.Equal(PracticeSubmitStatus.Saved, submitted.Status);

            // Tính tay: 1 đúng / 3 câu = 33,33%; sai và bỏ trống vẫn nằm trong mẫu số.
            var result = (await service.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!;
            Assert.Equal(33.33m, result.Score);
            Assert.Equal(3, result.Questions.Count);
            var correct = result.Questions.Single(q => q.Id == questions[0].Id);
            Assert.True(correct.IsCorrect);
            Assert.Equal("Correct 0", correct.SelectedAnswer);
            var wrong = result.Questions.Single(q => q.Id == questions[1].Id);
            Assert.False(wrong.IsCorrect);
            Assert.Equal("Wrong 1", wrong.SelectedAnswer);
            Assert.Equal("Correct 1", wrong.CorrectAnswer);
            var blank = result.Questions.Single(q => q.Id == questions[2].Id);
            Assert.False(blank.IsCorrect);
            Assert.Null(blank.SelectedAnswer);
            Assert.Equal("Correct 2", blank.CorrectAnswer);
            Assert.All(result.Questions, q => Assert.Equal("PRIVATE_PRACTICE_EXPLANATION", q.Explanation));

            var db = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var submission = Assert.Single(await db.PracticeSubmissions.AsNoTracking()
                .Include(s => s.Answers)
                .Where(s => s.StudentUserId == data.Student.Id && s.PracticeId == data.Practice.Id)
                .ToListAsync());
            submissionId = submission.Id;
            Assert.Equal(33.33m, submission.Score);
            Assert.Equal(2, submission.Answers.Count);
            Assert.Contains(submission.Answers, a => a.PracticeQuestionId == questions[0].Id && a.SelectedOptionId == firstCorrect);
            Assert.Contains(submission.Answers, a => a.PracticeQuestionId == questions[1].Id && a.SelectedOptionId == secondWrong);
            Assert.DoesNotContain(submission.Answers, a => a.PracticeQuestionId == questions[2].Id);
        }

        // Scope mới mô phỏng request làm lại; không dựa vào entity đang tracked ở lần đầu.
        using var retakeScope = factory.Services.CreateScope();
        var retakeService = retakeScope.ServiceProvider.GetRequiredService<IPracticeService>();
        var retaken = await retakeService.SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data));
        Assert.Equal(PracticeSubmitStatus.Saved, retaken.Status);
        var latestResult = (await retakeService.GetResultAsync(data.Student.Id, data.Practice.Id)).Value!;
        Assert.Equal(100m, latestResult.Score);
        Assert.All(latestResult.Questions, q => Assert.True(q.IsCorrect));

        var retakeDb = retakeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = Assert.Single(await retakeDb.PracticeSubmissions.AsNoTracking()
            .Include(s => s.Answers)
            .Where(s => s.StudentUserId == data.Student.Id && s.PracticeId == data.Practice.Id)
            .ToListAsync());
        Assert.Equal(submissionId, latest.Id);
        Assert.Equal(100m, latest.Score);
        Assert.Equal(3, latest.Answers.Count);
        Assert.Equal(questions.Select(q => q.Question.Options.Single(o => o.IsCorrect).Id).OrderBy(id => id),
            latest.Answers.Select(a => a.SelectedOptionId).OrderBy(id => id));
    }


    [Fact]
    public async Task Practice_ConcurrentRetakesKeepOneConsistentLatestSubmission()
    {
        var data = await SeedAsync();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var scope = factory.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IPracticeService>().SubmitAsync(data.Student.Id, data.Practice.Id, Responses(data, i % 2 == 0));
            Assert.Equal(PracticeSubmitStatus.Saved, result.Status);
        }));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = Assert.Single(await db.PracticeSubmissions.Include(s => s.Answers).Where(s => s.PracticeId == data.Practice.Id).ToListAsync());
        Assert.Equal(submission.Answers.Count * 100m / data.Practice.Questions.Count, submission.Score);
    }

    [Fact]
    public async Task TeacherReport_ScopesOwnerAndCountsOnlyValidEnrollmentsAndSuccessfulQuotedPayments()
    {
        var data = await SeedAsync();
        var foreign = await SeedAsync();
        var buyer = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var succeeded = Receipt(data, PaymentStatus.Succeeded, 100);
        succeeded.Order.StudentUserId = buyer.Id;
        db.Enrollments.Add(new Enrollment { CourseId = data.Course.Id, StudentUserId = buyer.Id, Payment = succeeded });
        db.Payments.AddRange(Receipt(data, PaymentStatus.Failed, 200), Receipt(data, PaymentStatus.Pending, 300),
            Receipt(data, PaymentStatus.Cancelled, 400), Receipt(foreign, PaymentStatus.Succeeded, 999));
        db.Orders.Add(new Order { CourseId = data.Course.Id, StudentUserId = data.Student.Id, Amount = 500 });
        db.LessonProgressEntries.Add(new LessonProgress { EnrollmentId = data.Enrollment.Id, LessonId = data.First.Id, IsCompleted = true });
        await db.SaveChangesAsync();
        await db.Courses.Where(c => c.Id == data.Course.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Price, 9999).SetProperty(c => c.IsPaid, true));
        var reportService = scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>();
        var report = (await reportService.GetAsync(data.Teacher.Id, data.Course.Id, new())).Value!;
        Assert.Equal(2, report.Commerce.Enrollments);
        Assert.Equal(1, report.Commerce.FreeEnrollments);
        Assert.Equal(1, report.Commerce.PaidEnrollments);
        Assert.Equal(5, report.Commerce.Orders);
        Assert.Equal(1, report.Commerce.SuccessfulOrders);
        Assert.Equal(100m, report.Commerce.SuccessfulPaymentAmount);
        Assert.Equal(50m, report.Students.Single(s => s.StudentId == data.Student.Id).Progress.Percentage);
        Assert.Equal(LearningAccessResult.Forbidden, (await reportService.GetAsync(foreign.Teacher.Id, data.Course.Id, new())).Access);
        Assert.Equal(LearningAccessResult.Forbidden, (await reportService.GetAsync(data.Student.Id, data.Course.Id, new())).Access);
    }

    [Fact]
    public async Task TeacherReport_SearchPaginationAndHistoricalCompletionStayCorrectWhenContentHidden()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.Enrollments.Where(e => e.Id == data.Enrollment.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.CompletedAtUtc, completedAt));
        await db.Modules.Where(m => m.Id == data.Module.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Visibility, ModuleVisibility.Hidden));
        var reports = scope.ServiceProvider.GetRequiredService<ITeacherCourseReportService>();
        var report = (await reports.GetAsync(data.Teacher.Id, data.Course.Id, new() { Search = " Student ", Completed = true, Page = int.MaxValue })).Value!;
        Assert.Equal(1, report.Query.Page);
        Assert.Equal("Student", report.Query.Search);
        Assert.Equal(1, report.Commerce.CompletedEnrollments);
        var student = Assert.Single(report.Students);
        Assert.Null(student.Progress.Percentage);
        Assert.Equal(completedAt, student.Progress.CompletedAtUtc);
        var empty = (await reports.GetAsync(data.Teacher.Id, data.Course.Id, new() { Search = "Missing name" })).Value!;
        Assert.Empty(empty.Students);
        Assert.Equal(1, empty.Commerce.Enrollments);
    }

    [Fact]
    public async Task Http_DashboardActivitiesPracticeAndTeacherReportEnforceRoleOwnerAndCsrf()
    {
        var data = await SeedAsync();
        var foreignTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var other = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var student = await ClientAsync(data.Student);
        using var dashboard = await student.GetAsync($"/Learning/Dashboard?studentId={other.Id}");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("no-store", dashboard.Headers.CacheControl!.ToString());
        Assert.Contains(data.Course.Title, await dashboard.Content.ReadAsStringAsync());
        using var course = await student.GetAsync($"/Learning/Course/{data.Course.Id}");
        var html = await course.Content.ReadAsStringAsync();
        Assert.Contains($"/Practice/Take/{data.Practice.Id}", html);
        Assert.Contains($"/Assessment/Take/{data.Assessment.Id}", html);
        var token = await IdentityTestHelpers.GetTokenAsync(student, $"/Practice/Take/{data.Practice.Id}");
        using var missingCsrf = await student.PostAsync($"/Practice/Submit/{data.Practice.Id}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var answers = Responses(data);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        for (var i = 0; i < answers.Length; i++)
        {
            form[$"Answers[{i}].PracticeQuestionId"] = answers[i].PracticeQuestionId.ToString();
            form[$"Answers[{i}].SelectedOptionId"] = answers[i].SelectedOptionId!.Value.ToString();
        }
        using var submitted = await student.PostAsync($"/Practice/Submit/{data.Practice.Id}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        using var result = await student.GetAsync(submitted.Headers.Location);
        Assert.Contains("PRIVATE_PRACTICE_EXPLANATION", await result.Content.ReadAsStringAsync());
        using var outsider = await ClientAsync(other);
        using var deniedSheet = await outsider.GetAsync($"/Practice/Take/{data.Practice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedSheet.StatusCode);
        using var deniedResult = await outsider.GetAsync($"/Practice/Result/{data.Practice.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedResult.StatusCode);
        using var deniedContinue = await outsider.GetAsync($"/Learning/Continue/{data.Course.Id}");
        Assert.Contains("/Account/AccessDenied", deniedContinue.Headers.Location!.ToString());
        using var teacher = await ClientAsync(data.Teacher);
        using var report = await teacher.GetAsync($"/TeacherCourses/Students/{data.Course.Id}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Contains("no-store", report.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(data.Student.Email!, await report.Content.ReadAsStringAsync());
        using var foreignClient = await ClientAsync(foreignTeacher);
        using var deniedReport = await foreignClient.GetAsync($"/TeacherCourses/Students/{data.Course.Id}?Search=Student");
        Assert.Contains("/Account/AccessDenied", deniedReport.Headers.Location!.ToString());
        using var deniedDashboard = await teacher.GetAsync("/Learning/Dashboard");
        Assert.Contains("/Account/AccessDenied", deniedDashboard.Headers.Location!.ToString());
        using var guest = IdentityTestHelpers.CreateClient(factory);
        using var anonymous = await guest.GetAsync("/Learning/Dashboard");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
    }

    private static Payment Receipt(Data data, PaymentStatus status, decimal amount) => new()
    {
        Order = new Order { CourseId = data.Course.Id, StudentUserId = data.Student.Id, Amount = amount, Currency = "VND" },
        Amount = amount, Status = status, Provider = "Test", ProviderTransactionId = Guid.NewGuid().ToString("N")
    };

    private async Task<HttpClient> ClientAsync(ApplicationUser user)
    {
        var client = IdentityTestHelpers.CreateClient(factory);
        using var login = await IdentityTestHelpers.LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private sealed record Data(ApplicationUser Teacher, ApplicationUser Student, Course Course, Module Module, Lesson First,
        Lesson Second, Enrollment Enrollment, Practice Practice, Assessment Assessment);
}

using System.Text.Json;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// DatabaseFixture tạo container/database riêng, không sử dụng dữ liệu demo.
public sealed class PracticeQuestionValidationTests : IClassFixture<IntegrationTestFactory>
{
    private const string Explanation = "PRIVATE_PRACTICE_VALIDATION_EXPLANATION";
    private const string AudioUrl = "https://example.test/practice/audio.mp3";
    private readonly IntegrationTestFactory _factory;

    public PracticeQuestionValidationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // Mỗi test tạo Course, Practice, Teacher và Student riêng.
    // Published Practice phải có đúng một parent và một câu hỏi MCQ hợp lệ.
    private async Task<SeedData> SeedAsync(
        bool moduleParent = false,
        QuestionStatus questionStatus = QuestionStatus.Published,
        bool listening = false)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Course course = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Practice validation course " + Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Published
        };
        Module module = new Module
        {
            Course = course,
            Title = "Visible module",
            Visibility = ModuleVisibility.Visible
        };
        Lesson lesson = new Lesson
        {
            Module = module,
            Title = "Published lesson",
            Status = LessonStatus.Published,
            Resources = new List<LessonResource>
            {
                new LessonResource
                {
                    ResourceType = LessonResourceType.Text,
                    ContentText = "Lesson content for integration testing."
                }
            }
        };
        QuestionOption correct = new QuestionOption
        {
            Content = "Yesterday", OrderIndex = 0, IsCorrect = true
        };
        QuestionOption wrong = new QuestionOption
        {
            Content = "Tomorrow", OrderIndex = 1, IsCorrect = false
        };
        Question question = new Question
        {
            OwnerTeacherUserId = teacher.Id,
            Content = "Choose the correct answer.",
            PrimarySkill = listening ? EnglishSkill.Listening : EnglishSkill.Reading,
            Status = questionStatus,
            Explanation = Explanation,
            Options = new List<QuestionOption> { correct, wrong }
        };
        if (listening)
        {
            question.Stimulus = new Stimulus
            {
                OwnerTeacherUserId = teacher.Id,
                StimulusType = StimulusType.Audio,
                Title = "Listening audio",
                ResourceUrl = AudioUrl
            };
        }

        PracticeQuestion link = new PracticeQuestion { Question = question, OrderIndex = 0 };
        Practice practice = new Practice
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "Validation practice",
            Status = PracticeStatus.Published,
            Questions = new List<PracticeQuestion> { link }
        };
        if (moduleParent)
        {
            practice.Module = module;
        }
        else
        {
            practice.Lesson = lesson;
        }

        db.Lessons.Add(lesson);
        db.Practices.Add(practice);
        db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();

        return new SeedData(teacher.Id, student.Id, course.Id, module.Id, lesson.Id,
            practice.Id, link.Id, question.Id, correct.Id, wrong.Id);
    }

    private static PracticeResponse[] Responses(SeedData data, bool correct = true)
    {
        return new[]
        {
            new PracticeResponse(data.PracticeQuestionId,
                correct ? data.CorrectOptionId : data.WrongOptionId)
        };
    }

    // Cố ý tạo dữ liệu lỗi trong DB test để kiểm tra lớp bảo vệ runtime.
    // Không cho phép Teacher sửa đề Published và không thay đổi schema/constraint.
    private async Task CorruptQuestionAsync(SeedData data, string fault)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Question question = await db.Questions.Include(q => q.Options)
            .Include(q => q.Stimulus).SingleAsync(q => q.Id == data.QuestionId);
        Stimulus? newStimulus = null;

        switch (fault)
        {
            case "blank_content":
                question.Content = "   ";
                break;
            case "blank_option":
                question.Options.Single(o => o.Id == data.WrongOptionId).Content = "   ";
                break;
            case "one_option":
                // Xóa option chưa từng được chọn, không phá FK của kết quả cũ.
                db.QuestionOptions.Remove(question.Options.Single(o => o.Id == data.WrongOptionId));
                break;
            case "no_options":
                db.QuestionOptions.RemoveRange(question.Options);
                break;
            case "no_correct":
                question.Options.Single(o => o.Id == data.CorrectOptionId).IsCorrect = false;
                break;
            case "foreign_question":
                var otherTeacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
                question.OwnerTeacherUserId = otherTeacher.Id;
                break;
            case "foreign_stimulus":
                var stimulusOwner = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
                newStimulus = new Stimulus
                {
                    OwnerTeacherUserId = stimulusOwner.Id,
                    StimulusType = StimulusType.Passage,
                    ContentText = "Another teacher's passage."
                };
                question.Stimulus = newStimulus;
                break;
            case "listening_without_audio":
                question.PrimarySkill = EnglishSkill.Listening;
                question.Stimulus = null;
                question.StimulusId = null;
                break;
            case "listening_with_passage":
                question.PrimarySkill = EnglishSkill.Listening;
                newStimulus = new Stimulus
                {
                    OwnerTeacherUserId = data.TeacherId,
                    StimulusType = StimulusType.Passage,
                    ContentText = "A passage is not listening audio."
                };
                question.Stimulus = newStimulus;
                break;
            case "invalid_audio_url":
                question.PrimarySkill = EnglishSkill.Listening;
                newStimulus = new Stimulus
                {
                    OwnerTeacherUserId = data.TeacherId,
                    StimulusType = StimulusType.Audio,
                    ResourceUrl = "https://"
                };
                question.Stimulus = newStimulus;
                break;
            case "undefined_stimulus_type":
                newStimulus = new Stimulus
                {
                    OwnerTeacherUserId = data.TeacherId,
                    StimulusType = (StimulusType)999,
                    ContentText = "Invalid stimulus type."
                };
                question.Stimulus = newStimulus;
                break;
            default:
                throw new ArgumentException("Unknown test fault.", nameof(fault));
        }

        // Đánh dấu rõ entity mới là Added trước khi cập nhật FK của Question.
        // Gán navigation có GUID sẵn vào entity đã tracked không thay thế Add.
        if (newStimulus != null)
        {
            db.Stimuli.Add(newStimulus);
        }
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false, QuestionStatus.Published, false)]
    [InlineData(true, QuestionStatus.Published, false)]
    [InlineData(false, QuestionStatus.Archived, false)]
    [InlineData(true, QuestionStatus.Archived, false)]
    [InlineData(false, QuestionStatus.Published, true)]
    [InlineData(true, QuestionStatus.Published, true)]
    [InlineData(false, QuestionStatus.Archived, true)]
    [InlineData(true, QuestionStatus.Archived, true)]
    public async Task ValidExistingPractice_AllowsSheetSubmitAndFeedback(
        bool moduleParent,
        QuestionStatus status,
        bool listening)
    {
        var data = await SeedAsync(moduleParent, status, listening);
        using IServiceScope scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();

        var sheet = await service.GetSheetAsync(data.StudentId, data.PracticeId);

        Assert.Equal(LearningAccessResult.Allowed, sheet.Access);
        Assert.NotNull(sheet.Value);
        Assert.Equal(data.CourseId, sheet.Value.CourseId);
        Assert.False(sheet.Value.HasResult);
        var question = Assert.Single(sheet.Value.Questions);
        Assert.Equal(data.PracticeQuestionId, question.Id);
        Assert.Equal(listening ? EnglishSkill.Listening : EnglishSkill.Reading, question.Skill);
        Assert.Equal(listening ? AudioUrl : null, question.AudioUrl);
        Assert.Equal(2, question.Options.Count);
        // Trước submit chỉ có choices, không có đáp án đúng/giải thích.
        string json = JsonSerializer.Serialize(sheet.Value);
        Assert.DoesNotContain("IsCorrect", json);
        Assert.DoesNotContain(Explanation, json);

        var submitted = await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data));
        Assert.Equal(PracticeSubmitStatus.Saved, submitted.Status);
        var result = await service.GetResultAsync(data.StudentId, data.PracticeId);
        Assert.Equal(LearningAccessResult.Allowed, result.Access);
        Assert.NotNull(result.Value);
        Assert.Equal(100m, result.Value.Score);
        var feedback = Assert.Single(result.Value.Questions);
        Assert.Equal("Yesterday", feedback.CorrectAnswer);
        Assert.Equal("Yesterday", feedback.SelectedAnswer);
        Assert.Equal(Explanation, feedback.Explanation);
        Assert.True(feedback.IsCorrect);
        Assert.True((await service.GetSheetAsync(data.StudentId, data.PracticeId)).Value!.HasResult);

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(status, await db.Questions.Where(q => q.Id == data.QuestionId).Select(q => q.Status).SingleAsync());
        Assert.Single(await db.PracticeSubmissions.Where(s => s.PracticeId == data.PracticeId).ToListAsync());
        Assert.Single(await db.PracticeAnswers.Where(a => a.PracticeSubmission.PracticeId == data.PracticeId).ToListAsync());
        Assert.False(await db.Attempts.AnyAsync(a => a.StudentUserId == data.StudentId));
    }

    [Theory]
    [InlineData("blank_content")]
    [InlineData("blank_option")]
    [InlineData("one_option")]
    [InlineData("no_options")]
    [InlineData("no_correct")]
    [InlineData("foreign_question")]
    [InlineData("foreign_stimulus")]
    [InlineData("listening_without_audio")]
    [InlineData("listening_with_passage")]
    [InlineData("invalid_audio_url")]
    [InlineData("undefined_stimulus_type")]
    public async Task InvalidQuestion_DeniesSheetAndSubmitWithoutCreatingResult(string fault)
    {
        var data = await SeedAsync();
        using IServiceScope scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        // Dữ liệu ban đầu hợp lệ, không phải bị từ chối do setup sai Enrollment/parent.
        Assert.Equal(LearningAccessResult.Allowed,
            (await service.GetSheetAsync(data.StudentId, data.PracticeId)).Access);
        await CorruptQuestionAsync(data, fault);

        var sheet = await service.GetSheetAsync(data.StudentId, data.PracticeId);
        Assert.Equal(LearningAccessResult.NotFound, sheet.Access);
        Assert.Null(sheet.Value);
        Assert.Equal(PracticeSubmitStatus.Unavailable,
            (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == data.PracticeId));
        Assert.False(await db.PracticeAnswers.AnyAsync(a => a.PracticeSubmission.PracticeId == data.PracticeId));
    }

    [Theory]
    [InlineData("blank_content")]
    [InlineData("blank_option")]
    [InlineData("one_option")]
    [InlineData("no_correct")]
    [InlineData("listening_without_audio")]
    [InlineData("undefined_stimulus_type")]
    public async Task InvalidQuestion_OnRetake_PreservesPreviousSubmissionAndAnswers(string fault)
    {
        var data = await SeedAsync();
        using (IServiceScope initialScope = _factory.Services.CreateScope())
        {
            var service = initialScope.ServiceProvider.GetRequiredService<IPracticeService>();
            Assert.Equal(PracticeSubmitStatus.Saved,
                (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        }
        SubmissionSnapshot before = await SnapshotAsync(data);
        await CorruptQuestionAsync(data, fault);

        using (IServiceScope deniedScope = _factory.Services.CreateScope())
        {
            var service = deniedScope.ServiceProvider.GetRequiredService<IPracticeService>();
            Assert.Equal(PracticeSubmitStatus.Unavailable,
                (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data, false))).Status);
            var result = await service.GetResultAsync(data.StudentId, data.PracticeId);
            Assert.Equal(LearningAccessResult.Allowed, result.Access);
            Assert.Equal(100m, result.Value!.Score);
        }

        SubmissionSnapshot after = await SnapshotAsync(data);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RepeatedRetakes_KeepOneLatestResultAndLatestSelectedAnswer()
    {
        var data = await SeedAsync();
        Guid? submissionId = null;
        for (int index = 0; index < 10; index++)
        {
            // Mỗi lần làm lại sử dụng scope mới như một request độc lập.
            using IServiceScope scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
            bool correct = index % 2 == 0;
            Assert.Equal(PracticeSubmitStatus.Saved,
                (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data, correct))).Status);
            var result = await service.GetResultAsync(data.StudentId, data.PracticeId);
            Assert.Equal(correct ? 100m : 0m, result.Value!.Score);
            var feedback = Assert.Single(result.Value.Questions);
            Assert.Equal(correct, feedback.IsCorrect);
            Assert.Equal(correct ? "Yesterday" : "Tomorrow", feedback.SelectedAnswer);
            Assert.Equal(Explanation, feedback.Explanation);

            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var submission = Assert.Single(await db.PracticeSubmissions.AsNoTracking()
                .Where(s => s.PracticeId == data.PracticeId).ToListAsync());
            submissionId ??= submission.Id;
            Assert.Equal(submissionId.Value, submission.Id);
            var answer = Assert.Single(await db.PracticeAnswers.AsNoTracking()
                .Where(a => a.PracticeSubmissionId == submission.Id).ToListAsync());
            Assert.Equal(correct ? data.CorrectOptionId : data.WrongOptionId, answer.SelectedOptionId);
        }
    }

    [Theory]
    [InlineData("no_enrollment", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("course_archived", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("practice_draft", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("practice_unpublished", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("lesson_hidden", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("module_hidden", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("foreign_practice_owner", LearningAccessResult.NotFound, PracticeSubmitStatus.Unavailable)]
    [InlineData("student_disabled", LearningAccessResult.Forbidden, PracticeSubmitStatus.Forbidden)]
    [InlineData("student_locked", LearningAccessResult.Forbidden, PracticeSubmitStatus.Forbidden)]
    public async Task ExistingAccessRules_StillDenyUnavailablePractice(
        string state,
        LearningAccessResult expectedAccess,
        PracticeSubmitStatus expectedSubmit)
    {
        var data = await SeedAsync();
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        switch (state)
        {
            case "no_enrollment":
                await db.Enrollments.Where(e => e.StudentUserId == data.StudentId && e.CourseId == data.CourseId).ExecuteDeleteAsync();
                break;
            case "course_archived":
                await db.Courses.Where(c => c.Id == data.CourseId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Archived));
                break;
            case "practice_draft":
                await db.Practices.Where(p => p.Id == data.PracticeId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PracticeStatus.Draft));
                break;
            case "practice_unpublished":
                await db.Practices.Where(p => p.Id == data.PracticeId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PracticeStatus.Unpublished));
                break;
            case "lesson_hidden":
                await db.Lessons.Where(l => l.Id == data.LessonId).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LessonStatus.Hidden));
                break;
            case "module_hidden":
                await db.Modules.Where(m => m.Id == data.ModuleId).ExecuteUpdateAsync(s => s.SetProperty(m => m.Visibility, ModuleVisibility.Hidden));
                break;
            case "foreign_practice_owner":
                var otherTeacher = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Teacher);
                await db.Practices.Where(p => p.Id == data.PracticeId).ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerTeacherUserId, otherTeacher.Id));
                break;
            case "student_disabled":
                await db.Users.Where(u => u.Id == data.StudentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Disabled));
                break;
            case "student_locked":
                await db.Users.Where(u => u.Id == data.StudentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.AccountStatus, AccountStatus.Locked));
                break;
            default:
                throw new ArgumentException("Unknown access state.", nameof(state));
        }

        var sheet = await service.GetSheetAsync(data.StudentId, data.PracticeId);
        Assert.Equal(expectedAccess, sheet.Access);
        Assert.Null(sheet.Value);
        Assert.Equal(expectedSubmit,
            (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        Assert.False(await db.PracticeSubmissions.AnyAsync(s => s.PracticeId == data.PracticeId));
    }

    [Theory]
    [InlineData(AppRoles.Teacher)]
    [InlineData(AppRoles.Admin)]
    public async Task NonStudent_CannotReadSheetOrSubmit(string role)
    {
        var data = await SeedAsync();
        var actor = await IdentityTestHelpers.CreateUserAsync(_factory, role);
        using IServiceScope scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();

        Assert.Equal(LearningAccessResult.Forbidden,
            (await service.GetSheetAsync(actor.Id, data.PracticeId)).Access);
        Assert.Equal(PracticeSubmitStatus.Forbidden,
            (await service.SubmitAsync(actor.Id, data.PracticeId, Responses(data))).Status);
    }

    [Fact]
    public async Task OtherStudent_CannotReadOrOverwriteOwnersResult()
    {
        var data = await SeedAsync();
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(_factory, AppRoles.Student);
        using IServiceScope scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Equal(PracticeSubmitStatus.Saved,
            (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        SubmissionSnapshot before = await SnapshotAsync(data);

        Assert.Equal(LearningAccessResult.NotFound,
            (await service.GetSheetAsync(otherStudent.Id, data.PracticeId)).Access);
        Assert.Equal(PracticeSubmitStatus.Unavailable,
            (await service.SubmitAsync(otherStudent.Id, data.PracticeId, Responses(data, false))).Status);
        Assert.Equal(LearningAccessResult.NotFound,
            (await service.GetResultAsync(otherStudent.Id, data.PracticeId)).Access);
        Assert.Equal(before, await SnapshotAsync(data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeUsesExistingValidatorWithDatabaseOwnerAndLoadedQuestion(bool submit)
    {
        var data = await SeedAsync(listening: true);
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        RecordingUsageValidator recording = new RecordingUsageValidator();
        // Dùng database thật, chỉ ghi nhận dependency để chứng minh đúng chỗ gọi.
        PracticeService service = new PracticeService(db, TimeProvider.System, recording);
        if (submit)
        {
            Assert.Equal(PracticeSubmitStatus.Saved,
                (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        }
        else
        {
            Assert.Equal(LearningAccessResult.Allowed,
                (await service.GetSheetAsync(data.StudentId, data.PracticeId)).Access);
        }

        Assert.Equal(1, recording.ExistingCalls);
        Assert.Equal(0, recording.NewCalls);
        Assert.Equal(data.TeacherId, recording.LastOwnerId);
        Assert.NotEqual(data.StudentId, recording.LastOwnerId);
        Assert.Null(recording.LastTargetSkill);
        Assert.NotNull(recording.LastQuestion);
        Assert.Equal(data.QuestionId, recording.LastQuestion.Id);
        Assert.Equal(2, recording.LastQuestion.Options.Count);
        Assert.NotNull(recording.LastQuestion.Stimulus);
        Assert.Equal(recording.LastQuestion.Stimulus.Id, recording.LastQuestion.StimulusId);
        Assert.Equal(AudioUrl, recording.LastQuestion.Stimulus.ResourceUrl);
    }

    [Fact]
    public async Task ValidatorRejection_DeniesBothPathsAndPreservesPreviouslySavedResult()
    {
        var data = await SeedAsync();
        using (IServiceScope initialScope = _factory.Services.CreateScope())
        {
            var service = initialScope.ServiceProvider.GetRequiredService<IPracticeService>();
            Assert.Equal(PracticeSubmitStatus.Saved,
                (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        }
        SubmissionSnapshot before = await SnapshotAsync(data);
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        RecordingUsageValidator recording = new RecordingUsageValidator { Reject = true };
        PracticeService serviceWithRejection = new PracticeService(db, TimeProvider.System, recording);

        // Dữ liệu vẫn hợp lệ trong SQL: việc từ chối phải đến từ validator vừa nối.
        Assert.Equal(LearningAccessResult.NotFound,
            (await serviceWithRejection.GetSheetAsync(data.StudentId, data.PracticeId)).Access);
        Assert.Equal(PracticeSubmitStatus.Unavailable,
            (await serviceWithRejection.SubmitAsync(data.StudentId, data.PracticeId, Responses(data, false))).Status);
        Assert.Equal(2, recording.ExistingCalls);
        Assert.Equal(0, recording.NewCalls);
        Assert.Equal(before, await SnapshotAsync(data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Withdrawal_DeniesNewWorkButKeepsHistoricalFeedback(bool archiveCourse)
    {
        var data = await SeedAsync();
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPracticeService>();
        Assert.Equal(PracticeSubmitStatus.Saved,
            (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data))).Status);
        SubmissionSnapshot before = await SnapshotAsync(data);
        if (archiveCourse)
        {
            await db.Courses.Where(c => c.Id == data.CourseId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CourseStatus.Archived));
        }
        else
        {
            await db.Practices.Where(p => p.Id == data.PracticeId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PracticeStatus.Unpublished));
        }

        Assert.Equal(LearningAccessResult.NotFound,
            (await service.GetSheetAsync(data.StudentId, data.PracticeId)).Access);
        Assert.Equal(PracticeSubmitStatus.Unavailable,
            (await service.SubmitAsync(data.StudentId, data.PracticeId, Responses(data, false))).Status);
        var history = await service.GetResultAsync(data.StudentId, data.PracticeId);
        Assert.Equal(LearningAccessResult.Allowed, history.Access);
        Assert.Equal(100m, history.Value!.Score);
        Assert.Equal(Explanation, Assert.Single(history.Value.Questions).Explanation);
        Assert.Equal(before, await SnapshotAsync(data));
    }

    // Snapshot đọc bằng scope mới để so sánh dữ liệu thật sau các request.
    private async Task<SubmissionSnapshot> SnapshotAsync(SeedData data)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = Assert.Single(await db.PracticeSubmissions.AsNoTracking()
            .Where(s => s.StudentUserId == data.StudentId && s.PracticeId == data.PracticeId).ToListAsync());
        var answer = Assert.Single(await db.PracticeAnswers.AsNoTracking()
            .Where(a => a.PracticeSubmissionId == submission.Id).ToListAsync());
        return new SubmissionSnapshot(submission.Id, submission.Score, submission.SubmittedAtUtc,
            submission.CreatedAtUtc, submission.UpdatedAtUtc, answer.Id,
            answer.PracticeQuestionId, answer.SelectedOptionId, answer.CreatedAtUtc, answer.UpdatedAtUtc);
    }

    private sealed record SeedData(Guid TeacherId, Guid StudentId, Guid CourseId, Guid ModuleId,
        Guid LessonId, Guid PracticeId, Guid PracticeQuestionId, Guid QuestionId,
        Guid CorrectOptionId, Guid WrongOptionId);

    private sealed record SubmissionSnapshot(Guid SubmissionId, decimal Score,
        DateTimeOffset SubmittedAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
        Guid AnswerId, Guid PracticeQuestionId, Guid SelectedOptionId,
        DateTimeOffset AnswerCreatedAtUtc, DateTimeOffset? AnswerUpdatedAtUtc);

    // Test double chỉ ghi nhận hoặc trả lỗi; mặc định vẫn dùng validator thật.
    private sealed class RecordingUsageValidator : IQuestionUsageValidator
    {
        private readonly QuestionUsageValidator _inner = new QuestionUsageValidator(new CreateQuestionValidator());
        public bool Reject { get; init; }
        public int ExistingCalls { get; private set; }
        public int NewCalls { get; private set; }
        public Guid LastOwnerId { get; private set; }
        public EnglishSkill? LastTargetSkill { get; private set; }
        public Question? LastQuestion { get; private set; }

        public IReadOnlyList<QuestionError> ValidateForNewUse(
            Guid ownerTeacherUserId, Question? question, EnglishSkill? targetSkill = null)
        {
            NewCalls++;
            throw new InvalidOperationException("Existing Practice must not use new-selection validation.");
        }

        public IReadOnlyList<QuestionError> ValidateForExistingUse(
            Guid ownerTeacherUserId, Question? question, EnglishSkill? targetSkill = null)
        {
            ExistingCalls++;
            LastOwnerId = ownerTeacherUserId;
            LastQuestion = question;
            LastTargetSkill = targetSkill;
            if (Reject)
            {
                return new[] { new QuestionError("QuestionId", "test_rejection", "Rejected by the test validator.") };
            }
            return _inner.ValidateForExistingUse(ownerTeacherUserId, question, targetSkill);
        }
    }
}

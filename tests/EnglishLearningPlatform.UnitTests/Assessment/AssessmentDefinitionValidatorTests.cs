using EnglishLearningPlatform.Application.Stimuli;
using System.Globalization;
using System.Text.Json;
using EnglishLearningPlatform.Application;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AssessmentEntity = EnglishLearningPlatform.Domain.Entities.Assessment;

namespace EnglishLearningPlatform.UnitTests.AssessmentAuthoring;

// Test thuần trong bộ nhớ: dùng validator thật, không cần SQL Server hoặc Docker.
public sealed class AssessmentDefinitionValidatorTests
{
    private readonly IAssessmentDefinitionValidator _validator =
        new AssessmentDefinitionValidator(
            new QuestionUsageValidator(new CreateQuestionValidator()));

    [Theory]
    [InlineData(AssessmentType.PlacementTest)]
    [InlineData(AssessmentType.SkillAssessment)]
    [InlineData(AssessmentType.PracticeExam)]
    public void Validate_ValidDefinitionAndPointLimits_ReturnsNoErrors(AssessmentType type)
    {
        var data = ValidDefinition(type);
        AttachStimulus(data.Questions[0].Question, StimulusType.Passage);
        var audioQuestion = data.Questions[1].Question;
        if (type != AssessmentType.SkillAssessment)
            audioQuestion.PrimarySkill = EnglishSkill.Listening;
        AttachStimulus(audioQuestion, StimulusType.Audio);
        data.Questions[0].Points = 0.01m;
        data.Questions[1].Points = 999999.99m;

        // Hai loại này có thể làm Final; Placement được kiểm tra ở scope độc lập.
        if (type != AssessmentType.PlacementTest) LinkCourse(data, asFinal: true);

        Assert.Empty(Validate(data));
    }

    [Fact]
    public void ExternalAudio_IsRejectedForNewPublication_ButExistingDefinitionIsNotRewritten()
    {
        var data = ValidDefinition();
        var audio = AttachStimulus(data.Questions[0].Question, StimulusType.Audio);
        audio.ResourceUrl = "https://external.test/mutable.mp3";
        Assert.Contains(Validate(data), error => error.Contains("Audio URL ngoài"));
        Assert.Empty(Validate(data, firstPublish: false));
        Assert.Equal("https://external.test/mutable.mp3", audio.ResourceUrl);
    }

    [Fact]
    public void Validate_MinimumContent_CountsDistinctQuestionsNotLinkRows()
    {
        var data = ValidDefinition();
        data.Questions.RemoveAt(4);
        Assert.Contains(Validate(data), error => error.Contains("ít nhất 5", StringComparison.Ordinal));

        data = ValidDefinition();
        data.Questions[4].QuestionId = data.Questions[0].QuestionId;
        data.Questions[4].Question = data.Questions[0].Question;
        var errors = Validate(data);
        Assert.Contains(errors, error => error.Contains("trùng", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ít nhất 5", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("1000000")]
    public void Validate_InvalidPoints_ReturnsErrorAtCorrectQuestion(string points)
    {
        var data = ValidDefinition();
        data.Questions[2].Points = decimal.Parse(points, CultureInfo.InvariantCulture);

        Assert.Contains(Validate(data), error =>
            error.StartsWith("Câu 3: Points", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InvalidQuestions_AggregatesMcqSkillAudioAndStimulusErrors()
    {
        var data = ValidDefinition(AssessmentType.SkillAssessment);
        // Mỗi câu sai một phần để kiểm tra thông báo chỉ ra đúng vị trí trong đề.
        data.Questions[0].Question.Options.First(o => o.IsCorrect).IsCorrect = false;
        data.Questions[1].Question.PrimarySkill = EnglishSkill.Vocabulary;
        data.Questions[2].Question.PrimarySkill = EnglishSkill.Listening;
        AttachStimulus(data.Questions[3].Question, StimulusType.Passage).ContentText = "   ";
        AttachStimulus(data.Questions[4].Question, StimulusType.Audio).OwnerTeacherUserId = Guid.NewGuid();

        var errors = Validate(data);

        Assert.Contains(errors, error => error.StartsWith("Câu 1:", StringComparison.Ordinal)
            && error.Contains("đúng một đáp án", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("Câu 2:", StringComparison.Ordinal)
            && error.Contains("TargetSkill", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("Câu 3:", StringComparison.Ordinal)
            && error.Contains("audio stimulus", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("Câu 4:", StringComparison.Ordinal)
            && error.Contains("Passage", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("Câu 5:", StringComparison.Ordinal)
            && error.Contains("Stimulus không thuộc", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ForeignQuestion_RejectsBeforeInspectingItsContent()
    {
        var data = ValidDefinition();
        var question = data.Questions[0].Question;
        question.OwnerTeacherUserId = Guid.NewGuid();
        question.Content = "";
        question.Options.Clear();
        AttachStimulus(question, StimulusType.Passage).ContentText = "";

        string error = Assert.Single(Validate(data));

        Assert.Equal("Câu 1: Câu hỏi không tồn tại hoặc không cùng owner.", error);
    }

    [Theory]
    [InlineData("PlacementScore", "Placement Test")]
    [InlineData("MissingTarget", "chọn một kỹ năng")]
    public void Validate_TypeSpecificConfiguration_UsesExistingRules(string fault, string message)
    {
        var data = ValidDefinition();
        if (fault == "PlacementScore")
            data.Assessment.AssessmentType = AssessmentType.PlacementTest;
        else
            data.Assessment.AssessmentType = AssessmentType.SkillAssessment;

        Assert.Contains(Validate(data), error =>
            error.StartsWith("Cấu hình:", StringComparison.Ordinal)
            && error.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InvalidMetadata_DelegatesToDraftConfigurationValidation()
    {
        var data = ValidDefinition();
        data.Assessment.Title = "   ";
        data.Assessment.DurationMinutes = 0;
        data.Assessment.MaxAttempts = 0;

        var errors = Validate(data);

        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.StartsWith("Cấu hình:", error));
        Assert.Contains(errors, error => error.Contains("tên bài kiểm tra", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Thời lượng", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Số lượt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WrongOwner", "Course không tồn tại hoặc không cùng owner")]
    [InlineData("WrongCourseId", "Course không tồn tại hoặc không cùng owner")]
    [InlineData("Archived", "Course đã Archived")]
    [InlineData("ForeignFinalCourse", "Final phải thuộc đúng Course")]
    [InlineData("MissingFinalScore", "Final phải có PassingScore")]
    [InlineData("PlacementFinal", "Final chỉ được dùng")]
    public void Validate_InvalidCourseOrFinal_ReturnsClearError(string fault, string message)
    {
        var data = ValidDefinition();
        var course = LinkCourse(data, asFinal: true);
        switch (fault)
        {
            case "WrongOwner": course.OwnerTeacherUserId = Guid.NewGuid(); break;
            case "WrongCourseId": data.Assessment.CourseId = Guid.NewGuid(); break;
            case "Archived": course.Status = CourseStatus.Archived; break;
            case "MissingFinalScore": data.Assessment.PassingScore = null; break;
            case "PlacementFinal":
                data.Assessment.AssessmentType = AssessmentType.PlacementTest;
                data.Assessment.PassingScore = null;
                break;
            case "ForeignFinalCourse":
                course.FinalAssessmentId = null;
                data.FinalCourses.Clear();
                data.FinalCourses.Add(new Course
                {
                    OwnerTeacherUserId = data.Assessment.OwnerTeacherUserId,
                    Title = "Other course", FinalAssessmentId = data.Assessment.Id
                });
                break;
        }

        Assert.Contains(Validate(data), error => error.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_ArchivedQuestion_DistinguishesFirstPublishFromExistingUse(bool firstPublish)
    {
        var data = ValidDefinition();
        data.Assessment.Status = firstPublish ? AssessmentStatus.Draft : AssessmentStatus.Published;
        data.Questions[0].Question.Status = QuestionStatus.Archived;

        var errors = Validate(data, firstPublish);

        if (firstPublish)
            Assert.Contains(errors, error => error.Contains("Archived", StringComparison.Ordinal));
        else
            Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidAndInvalidInputs_DoNotChangeStatusVersionsContentOrLocks()
    {
        var data = ValidDefinition();
        LinkCourse(data, asFinal: true);
        var lockedAt = new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero);
        data.Assessment.UpdatedAtUtc = lockedAt;
        var question = data.Questions[0].Question;
        question.LockContent(lockedAt);
        AttachStimulus(question, StimulusType.Passage).LockContent(lockedAt);
        AttachStimulus(data.Questions[1].Question, StimulusType.Audio);
        string before = Snapshot(data);

        Assert.Empty(Validate(data));
        Assert.Equal(before, Snapshot(data));

        // Cả validation thất bại cũng không được khóa thêm hoặc sửa đề.
        data.Assessment.Title = "   ";
        before = Snapshot(data);
        Assert.NotEmpty(Validate(data));
        Assert.Equal(before, Snapshot(data));
    }

    [Fact]
    public void ApplicationDi_ResolvesRealValidatorWithScopedLifetime()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>();

        Assert.IsType<AssessmentDefinitionValidator>(first);
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<IAssessmentDefinitionValidator>());
        var data = ValidDefinition();
        Assert.Empty(first.Validate(data.Assessment, data.Questions, null, data.FinalCourses, true));
    }

    private IReadOnlyList<string> Validate(TestData data, bool firstPublish = true)
    {
        return _validator.Validate(data.Assessment, data.Questions,
            data.LinkedCourse, data.FinalCourses, firstPublish);
    }

    // Tạo dữ liệu mới cho từng test; gắn FK và navigation giống dữ liệu đã được Include.
    private static TestData ValidDefinition(AssessmentType type = AssessmentType.PracticeExam)
    {
        var assessment = new AssessmentEntity
        {
            OwnerTeacherUserId = Guid.NewGuid(), Title = "Definition test", AssessmentType = type,
            TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Reading : null,
            DurationMinutes = 20, MaxAttempts = 3,
            PassingScore = type == AssessmentType.PlacementTest ? null : 72.25m
        };
        var questions = new List<AssessmentQuestion>();
        for (int index = 0; index < 5; index++)
        {
            var question = new Question
            {
                OwnerTeacherUserId = assessment.OwnerTeacherUserId,
                Content = $"Question {index + 1}", PrimarySkill = EnglishSkill.Reading,
                Options = new List<QuestionOption>
                {
                    new QuestionOption { Content = "First", OrderIndex = 0, IsCorrect = true },
                    new QuestionOption { Content = "Second", OrderIndex = 1 }
                }
            };
            questions.Add(new AssessmentQuestion
            {
                AssessmentId = assessment.Id, Assessment = assessment,
                QuestionId = question.Id, Question = question,
                OrderIndex = index, Points = index + 1m
            });
        }
        assessment.Questions = questions;
        return new TestData(assessment, questions);
    }

    private static Stimulus AttachStimulus(Question question, StimulusType type)
    {
        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = question.OwnerTeacherUserId, StimulusType = type,
            Title = "Validator stimulus",
            ContentText = type == StimulusType.Passage ? "Reading passage." : null,
            ResourceUrl = type == StimulusType.Audio ? EnglishLearningPlatform.Application.Stimuli.StimulusAudioConstants.RoutePrefix + new string('a', 64) : null
        };
        question.StimulusId = stimulus.Id;
        question.Stimulus = stimulus;
        return stimulus;
    }

    private static Course LinkCourse(TestData data, bool asFinal)
    {
        var course = new Course
        {
            OwnerTeacherUserId = data.Assessment.OwnerTeacherUserId, Title = "Definition course",
            FinalAssessmentId = asFinal ? data.Assessment.Id : null
        };
        data.Assessment.CourseId = course.Id;
        data.Assessment.Course = course;
        data.LinkedCourse = course;
        if (asFinal) data.FinalCourses.Add(course);
        return course;
    }

    // Chỉ chọn các giá trị cần so sánh để tránh vòng lặp navigation khi serialize entity.
    private static string Snapshot(TestData data)
    {
        var a = data.Assessment;
        return JsonSerializer.Serialize(new
        {
            a.Id, a.OwnerTeacherUserId, a.Title, a.Description, a.AssessmentType,
            a.TargetSkill, a.DurationMinutes, a.MaxAttempts, a.PassingScore,
            a.Status, a.CourseId, a.CreatedAtUtc, a.UpdatedAtUtc,
            Attempts = a.Attempts.Count,
            Questions = data.Questions.Select(q => new
            {
                q.Id, q.AssessmentId, q.QuestionId, q.OrderIndex, q.Points,
                q.Question.Content, q.Question.PrimarySkill, q.Question.Status,
                q.Question.StimulusId, q.Question.ContentLockedAtUtc, q.Question.UpdatedAtUtc,
                Options = q.Question.Options.Select(o => new { o.Id, o.Content, o.OrderIndex, o.IsCorrect }),
                Stimulus = q.Question.Stimulus == null ? null : new
                {
                    q.Question.Stimulus.Id, q.Question.Stimulus.ContentText,
                    q.Question.Stimulus.ResourceUrl, q.Question.Stimulus.ContentLockedAtUtc,
                    q.Question.Stimulus.UpdatedAtUtc
                }
            }),
            Course = data.LinkedCourse == null ? null : new
            {
                data.LinkedCourse.Id, data.LinkedCourse.OwnerTeacherUserId,
                data.LinkedCourse.Status, data.LinkedCourse.FinalAssessmentId, data.LinkedCourse.UpdatedAtUtc
            }
        });
    }

    private sealed record TestData(AssessmentEntity Assessment, List<AssessmentQuestion> Questions)
    {
        public Course? LinkedCourse { get; set; }
        public List<Course> FinalCourses { get; } = new List<Course>();
    }
}

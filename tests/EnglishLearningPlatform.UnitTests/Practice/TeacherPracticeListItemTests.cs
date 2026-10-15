using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class TeacherPracticeListItemTests
{
    [Fact]
    public void NewItem_HasNonNullTitleAndNoParentInformation()
    {
        var item = new TeacherPracticeListItem();

        Assert.Equal(string.Empty, item.Title);
        Assert.Equal(0, item.QuestionCount);
        Assert.Null(item.CourseId);
        Assert.Null(item.CourseTitle);
        Assert.Null(item.ModuleId);
        Assert.Null(item.ModuleTitle);
        Assert.Null(item.LessonId);
        Assert.Null(item.LessonTitle);
    }

    [Fact]
    public void Item_CanRepresentDraftWithoutParentOrQuestions()
    {
        var practiceId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 10, 15, 8, 30, 0, TimeSpan.Zero);
        var item = new TeacherPracticeListItem
        {
            Id = practiceId,
            Title = "Grammar draft",
            Status = PracticeStatus.Draft,
            QuestionCount = 0,
            CreatedAtUtc = createdAt
        };

        Assert.Equal(practiceId, item.Id);
        Assert.Equal("Grammar draft", item.Title);
        Assert.Equal(PracticeStatus.Draft, item.Status);
        Assert.Equal(0, item.QuestionCount);
        Assert.Equal(createdAt, item.CreatedAtUtc);
        Assert.Null(item.CourseId);
        Assert.Null(item.CourseTitle);
        Assert.Null(item.ModuleId);
        Assert.Null(item.ModuleTitle);
        Assert.Null(item.LessonId);
        Assert.Null(item.LessonTitle);
    }

    [Theory]
    [InlineData(PracticeStatus.Draft)]
    [InlineData(PracticeStatus.Published)]
    [InlineData(PracticeStatus.Unpublished)]
    public void Item_CanRepresentEachPracticeStatus(PracticeStatus status)
    {
        var item = new TeacherPracticeListItem { Status = status };

        Assert.Equal(status, item.Status);
    }

    [Fact]
    public void Item_CanRepresentDirectModuleParentWithoutLesson()
    {
        var courseId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var item = new TeacherPracticeListItem
        {
            CourseId = courseId,
            CourseTitle = "English for beginners",
            ModuleId = moduleId,
            ModuleTitle = "Grammar module",
            QuestionCount = 5
        };

        Assert.Equal(courseId, item.CourseId);
        Assert.Equal("English for beginners", item.CourseTitle);
        Assert.Equal(moduleId, item.ModuleId);
        Assert.Equal("Grammar module", item.ModuleTitle);
        Assert.Equal(5, item.QuestionCount);
        Assert.Null(item.LessonId);
        Assert.Null(item.LessonTitle);
    }

    [Fact]
    public void Item_CanRepresentDirectLessonParentWithoutDirectModuleParent()
    {
        var courseId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var item = new TeacherPracticeListItem
        {
            CourseId = courseId,
            CourseTitle = "English for beginners",
            LessonId = lessonId,
            LessonTitle = "Present simple"
        };

        Assert.Equal(courseId, item.CourseId);
        Assert.Equal("English for beginners", item.CourseTitle);
        Assert.Equal(lessonId, item.LessonId);
        Assert.Equal("Present simple", item.LessonTitle);
        Assert.Null(item.ModuleId);
        Assert.Null(item.ModuleTitle);
    }

    [Fact]
    public void ChangingOneItem_DoesNotChangeAnotherItem()
    {
        var first = new TeacherPracticeListItem();
        var second = new TeacherPracticeListItem();

        first.Title = "Reading practice";
        first.QuestionCount = 10;
        first.CourseId = Guid.NewGuid();
        first.LessonId = Guid.NewGuid();

        Assert.Equal(string.Empty, second.Title);
        Assert.Equal(0, second.QuestionCount);
        Assert.Null(second.CourseId);
        Assert.Null(second.LessonId);
    }
}

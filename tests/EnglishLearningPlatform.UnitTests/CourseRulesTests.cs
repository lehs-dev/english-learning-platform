using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class CourseRulesTests
{
    [Theory]
    [InlineData(AssessmentType.PlacementTest, null, AssessmentStatus.Published, false)]
    [InlineData(AssessmentType.PracticeExam, null, AssessmentStatus.Published, false)]
    [InlineData(AssessmentType.PracticeExam, 101, AssessmentStatus.Published, false)]
    [InlineData(AssessmentType.PracticeExam, 60, AssessmentStatus.Draft, false)]
    [InlineData(AssessmentType.PracticeExam, 60, AssessmentStatus.Published, true)]
    [InlineData(AssessmentType.SkillAssessment, 60, AssessmentStatus.Published, true)]
    public void FinalAssessment_MustMatchOwnerCourseTypePassingScoreAndStatus(AssessmentType type, int? passing, AssessmentStatus status, bool valid)
    {
        var course = Ready();
        course.FinalAssessment = new Assessment { OwnerTeacherUserId = course.OwnerTeacherUserId, CourseId = course.Id,
            AssessmentType = type, Status = status, PassingScore = passing };
        course.FinalAssessmentId = course.FinalAssessment.Id;
        Assert.Equal(valid, CourseRules.PublishErrors(course).Count == 0);
        course.FinalAssessment.OwnerTeacherUserId = Guid.NewGuid();
        Assert.NotEmpty(CourseRules.PublishErrors(course));
        course.FinalAssessment.OwnerTeacherUserId = course.OwnerTeacherUserId;
        course.FinalAssessment.CourseId = Guid.NewGuid();
        Assert.NotEmpty(CourseRules.PublishErrors(course));
    }
    [Theory]
    [InlineData("https://example.test/media.mp4", true)]
    [InlineData("http://example.test/media.mp3", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///private.txt", false)]
    [InlineData("/relative/path", false)]
    public void ExternalResources_OnlyAllowAbsoluteHttpUrls(string url, bool valid)
    {
        foreach (var type in new[] { LessonResourceType.Link, LessonResourceType.Audio, LessonResourceType.Video })
            Assert.Equal(valid, CourseRules.ValidResource(type, null, url));
    }
    private static Course Ready() => new() { OwnerTeacherUserId = Guid.NewGuid(), Title = "Course", Modules = [new Module { Title = "Module",
        Lessons = [new Lesson { Title = "Lesson", Status = LessonStatus.Published, Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Text" }] }] }] };
}

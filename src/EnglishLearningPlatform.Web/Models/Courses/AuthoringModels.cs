using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Learning;
namespace EnglishLearningPlatform.Web.Models.Courses;

public sealed record CourseEditModel(Guid? Id, CourseInput Input);
public sealed record ContentEditModel(Guid CourseId, ContentKind Kind, Guid ParentId, Guid? Id, ContentInput Input,
    string? ExistingMediaUrl = null);
public sealed record AuthoringModel(CoursePage Page, IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>> Resources)
{
    public string Tab { get; init; } = "content";
    public Guid? ModuleId { get; init; }
    public Guid? LessonId { get; init; }
    public ContentEditModel? Editor { get; init; }
    public IReadOnlyList<TeacherAssessmentListItem> Assessments { get; init; } = [];
}

public sealed record CourseWorkspaceNav(Guid CourseId, string Tab);
public sealed record OrderButtonsModel(Guid CourseId, ContentKind Kind, Guid ParentId, Guid[] Ids, int Position,
    Guid? ModuleId = null, Guid? LessonId = null);

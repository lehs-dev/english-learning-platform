using EnglishLearningPlatform.Application.Learning;
namespace EnglishLearningPlatform.Web.Models.Courses;
public sealed record CourseEditModel(Guid? Id, CourseInput Input);
public sealed record ContentEditModel(Guid CourseId, ContentKind Kind, Guid ParentId, Guid? Id, ContentInput Input);
public sealed record AuthoringModel(CoursePage Page, IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>> Resources);
public sealed record OrderButtonsModel(Guid CourseId, ContentKind Kind, Guid ParentId, Guid[] Ids, int Position);

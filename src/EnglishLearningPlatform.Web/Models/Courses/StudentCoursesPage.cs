using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Web.Models.Courses;

public sealed record StudentCoursesPage(IReadOnlyList<CourseSummary> EnrolledCourses, CatalogPage Catalog);

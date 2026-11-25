using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Web.Models.Courses;

public sealed record StudentCoursesPage(
    IReadOnlyList<CourseSummary> EnrolledCourses,
    CatalogPage Catalog,
    string? ActiveStatus = null,
    string? SearchQuery = null);

public sealed record StudentProgressPage(
    IReadOnlyList<CourseSummary> EnrolledCourses,
    IReadOnlyList<RepresentativeAssessmentResult> AssessmentResults,
    IReadOnlyList<AssessmentLink> AvailableAssessments,
    GamificationSummary? Gamification,
    IReadOnlyList<SkillRecommendation> Recommendations,
    string ActiveTab);

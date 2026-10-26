using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed class TeacherAssessmentIndexViewModel
{
    public IReadOnlyList<TeacherAssessmentListItem> Items { get; init; } = [];
    public IReadOnlyDictionary<AssessmentStatus, int> StatusCounts { get; init; }
        = new Dictionary<AssessmentStatus, int>();
    public int TotalCount { get; init; }
    public string? Search { get; init; }
    public AssessmentStatus? Status { get; init; }
    public AssessmentType? Type { get; init; }
    public bool HasFilters => !string.IsNullOrWhiteSpace(Search) || Status.HasValue || Type.HasValue;
}

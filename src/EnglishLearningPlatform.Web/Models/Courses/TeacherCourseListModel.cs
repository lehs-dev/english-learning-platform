using System.Globalization;
using System.Text;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Web.Models.Courses;

public sealed class TeacherCourseListQuery
{
    public string? Search { get; set; }
    public CourseStatus? Status { get; set; }
    public CourseLevel? Level { get; set; }
    public EnglishSkill? Skill { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record TeacherCourseListModel(
    TeacherCourseListQuery Query,
    IReadOnlyList<CourseSummary> Courses,
    int Total,
    int AllCount,
    int LessonCount,
    IReadOnlyDictionary<CourseStatus, int> StatusCounts)
{
    public const int PageSize = 6;
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
    public int First => Total == 0 ? 0 : (Query.Page - 1) * PageSize + 1;
    public int Last => Math.Min(Query.Page * PageSize, Total);
    public bool HasFilters => !string.IsNullOrEmpty(Query.Search) || Query.Status.HasValue || Query.Level.HasValue || Query.Skill.HasValue;
    public IEnumerable<int> PageNumbers => Enumerable.Range(Math.Max(1, Math.Min(Query.Page - 2, Pages - 4)), Math.Min(5, Pages));

    public Dictionary<string, string> Route(int page, CourseStatus? status)
    {
        var route = new Dictionary<string, string> { ["Page"] = page.ToString(CultureInfo.InvariantCulture), ["Sort"] = Query.Sort! };
        if (!string.IsNullOrEmpty(Query.Search)) route["Search"] = Query.Search;
        if (status.HasValue) route["Status"] = ((int)status.Value).ToString(CultureInfo.InvariantCulture);
        if (Query.Level.HasValue) route["Level"] = ((int)Query.Level.Value).ToString(CultureInfo.InvariantCulture);
        if (Query.Skill.HasValue) route["Skill"] = ((int)Query.Skill.Value).ToString(CultureInfo.InvariantCulture);
        return route;
    }

    // MyCoursesAsync already scopes this list to the active teacher and orders newest first.
    public static TeacherCourseListModel Create(IReadOnlyList<CourseSummary> ownCourses, TeacherCourseListQuery query)
    {
        query.Search = query.Search?.Trim();
        if (query.Search?.Length > 200) query.Search = query.Search[..200];
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value)) query.Status = null;
        if (query.Level.HasValue && !Enum.IsDefined(query.Level.Value)) query.Level = null;
        if (query.Skill.HasValue && !Enum.IsDefined(query.Skill.Value)) query.Skill = null;
        if (query.Sort is not ("newest" or "oldest" or "title" or "title-desc")) query.Sort = "newest";

        var culture = CultureInfo.GetCultureInfo("vi-VN");
        IEnumerable<CourseSummary> filtered = ownCourses;
        if (!string.IsNullOrEmpty(query.Search))
        {
            var search = SearchText(query.Search);
            filtered = filtered.Where(c => SearchText(c.Title).Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (query.Status.HasValue) filtered = filtered.Where(c => c.Status == query.Status);
        if (query.Level.HasValue) filtered = filtered.Where(c => c.Level == query.Level);
        if (query.Skill.HasValue) filtered = filtered.Where(c => c.Skills.Contains(query.Skill.Value));
        var total = filtered.Count();
        filtered = query.Sort switch
        {
            "oldest" => filtered.Reverse(),
            "title" => filtered.OrderBy(c => c.Title, StringComparer.Create(culture, true)).ThenBy(c => c.Id),
            "title-desc" => filtered.OrderByDescending(c => c.Title, StringComparer.Create(culture, true)).ThenBy(c => c.Id),
            _ => filtered
        };
        query.Page = Math.Clamp(query.Page, 1, Math.Max(1, (total + PageSize - 1) / PageSize));
        return new(query, filtered.Skip((query.Page - 1) * PageSize).Take(PageSize).ToArray(), total,
            ownCourses.Count, ownCourses.Sum(c => c.LessonCount),
            Enum.GetValues<CourseStatus>().ToDictionary(status => status, status => ownCourses.Count(c => c.Status == status)));
    }

    private static string SearchText(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
        .Select(character => character switch { 'đ' => 'd', 'Đ' => 'D', _ => character }).ToArray());
}

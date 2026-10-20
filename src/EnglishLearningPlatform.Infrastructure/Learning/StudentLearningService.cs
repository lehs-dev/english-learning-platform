using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class StudentLearningService(AppDbContext db, ICourseService courses,
    ILearningAccessService access, TimeProvider time) : IStudentLearningService
{
    public async Task<LearningOutcome<StudentDashboard>> DashboardAsync(Guid studentId, CancellationToken ct = default)
    {
        var mine = await courses.MyCoursesAsync(studentId, false, ct);
        if (mine.Access != LearningAccessResult.Allowed) return new(mine.Access);
        var representativeResults = await GetRepresentativeResultsAsync(studentId, ct);
        if (representativeResults.Access != LearningAccessResult.Allowed) return new(representativeResults.Access);
        var results = representativeResults.Value!;
        var recommendations = await RecommendationsAsync(studentId, mine.Value!, results, ct);
        var practiceResults = await db.PracticeSubmissions.AsNoTracking().Where(s => s.StudentUserId == studentId)
            .OrderByDescending(s => s.SubmittedAtUtc).ThenBy(s => s.Id).Take(20)
            .Select(s => new DashboardPracticeResult(s.PracticeId, s.Practice.Title, s.Score, s.SubmittedAtUtc)).ToListAsync(ct);
        return new(LearningAccessResult.Allowed, new(mine.Value!, results, recommendations) { PracticeResults = practiceResults });
    }

    public async Task<LearningOutcome<IReadOnlyList<RepresentativeAssessmentResult>>> GetRepresentativeResultsAsync(
        Guid studentId, CancellationToken ct = default)
    {
        // Profile không cần tải khóa học/gợi ý, nhưng vẫn phải kiểm tra Student trong database.
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var results = await RepresentativeResultsAsync(studentId, ct);
        return new(LearningAccessResult.Allowed, results);
    }

    public async Task<LearningOutcome<CourseActivities>> ActivitiesAsync(Guid studentId, Guid courseId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        var allowed = await access.CheckAccessAsync(studentId, LearningResourceType.Course, courseId, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        return new(allowed, new(await PracticesAsync(studentId, courseId, ct), await AssessmentLinksAsync(studentId, courseId, ct)));
    }

    public async Task<LearningOutcome<IReadOnlyList<AssessmentLink>>> AssessmentsAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!await db.IsStudentAsync(studentId, ct)) return new(LearningAccessResult.Forbidden);
        return new(LearningAccessResult.Allowed, await AssessmentLinksAsync(studentId, null, ct));
    }

    private async Task<List<PracticeLink>> PracticesAsync(Guid studentId, Guid? courseId, CancellationToken ct)
    {
        var rows = await db.AvailablePractices(studentId)
            .Where(p => !courseId.HasValue || (p.LessonId.HasValue ? p.Lesson!.Module.CourseId : p.Module!.CourseId) == courseId)
            .OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Select(p => new { p.Id, CourseId = p.LessonId.HasValue ? p.Lesson!.Module.CourseId : p.Module!.CourseId,
                p.ModuleId, p.LessonId, p.Title, Count = p.Questions.Count }).ToListAsync(ct);
        var ids = rows.Select(p => p.Id).ToArray();
        if (ids.Length == 0) return [];
        // Keep metadata expansion separate from the authorization query; a correlated
        // collection projection duplicates the complex access predicate in SQL.
        var skills = await db.PracticeQuestions.AsNoTracking().Where(q => ids.Contains(q.PracticeId))
            .Select(q => new { q.PracticeId, q.Question.PrimarySkill }).Distinct().ToListAsync(ct);
        var groups = skills.ToLookup(q => q.PracticeId, q => q.PrimarySkill);
        return rows.Select(p => new PracticeLink(p.Id, p.CourseId, p.ModuleId, p.LessonId, p.Title, p.Count,
            groups[p.Id].Order().ToList())).ToList();
    }

    private async Task<List<AssessmentLink>> AssessmentLinksAsync(Guid studentId, Guid? courseId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var rows = await db.AvailableAssessments(studentId, now).Where(a => !courseId.HasValue || a.CourseId == courseId)
            .OrderBy(a => a.Title).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.CourseId, a.Title, a.AssessmentType, Count = a.Questions.Count,
                a.DurationMinutes, a.Status,
                ActiveId = a.Attempts.Where(t => t.StudentUserId == studentId && t.Status == AttemptStatus.InProgress && t.DeadlineUtc > now)
                    .Select(t => (Guid?)t.Id).FirstOrDefault() }).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToArray();
        if (ids.Length == 0) return [];
        var skills = await db.AssessmentQuestions.AsNoTracking().Where(q => ids.Contains(q.AssessmentId))
            .Select(q => new { q.AssessmentId, q.Question.PrimarySkill }).Distinct().ToListAsync(ct);
        var groups = skills.ToLookup(q => q.AssessmentId, q => q.PrimarySkill);
        return rows.Select(a => new AssessmentLink(a.Id, a.CourseId, a.Title, a.AssessmentType, a.Count,
            a.DurationMinutes, groups[a.Id].Order().ToList(), a.ActiveId) { Status = a.Status }).ToList();
    }

    private async Task<List<RepresentativeAssessmentResult>> RepresentativeResultsAsync(Guid studentId, CancellationToken ct)
    {
        var attempts = await db.Attempts.AsNoTracking().Where(t => t.StudentUserId == studentId &&
            t.Status == AttemptStatus.Graded && t.OverallScore.HasValue && t.FinalizedAtUtc.HasValue &&
            t.FinalizationReason.HasValue)
            .Select(t => new { t.Id, t.AssessmentId, t.Assessment.Title, Score = t.OverallScore!.Value, Finalized = t.FinalizedAtUtc!.Value })
            .ToListAsync(ct);
        // BR11.1: overall and skills come from the same highest-score attempt.
        // Compare GUIDs in memory so the final tie-break is deterministic in the domain.
        var representatives = attempts.GroupBy(t => t.AssessmentId).Select(g => g.OrderByDescending(t => t.Score)
            .ThenByDescending(t => t.Finalized).ThenByDescending(t => t.Id).First()).ToList();
        var ids = representatives.Select(t => t.Id).ToArray();
        if (ids.Length == 0) return [];
        var skillRows = await db.AttemptSkillResults.AsNoTracking()
            .Where(s => s.Attempt.StudentUserId == studentId && ids.Contains(s.AttemptId) &&
                s.QuestionCount >= SkillAnalysisRules.MinimumQuestionCount)
            .Select(s => new
            {
                s.AttemptId, s.Skill, s.Score, s.QuestionCount,
                DistinctCount = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill)
                    .Select(q => q.QuestionId).Distinct().Count(),
                Maximum = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill).Sum(q => q.Points),
                Earned = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill &&
                    s.Attempt.Answers.Any(a => a.AssessmentQuestionId == q.Id &&
                        q.Question.Options.Any(o => o.Id == a.SelectedOptionId && o.IsCorrect))).Sum(q => q.Points)
            }).ToListAsync(ct);
        return representatives.OrderByDescending(t => t.Finalized).ThenBy(t => t.AssessmentId)
            .Select(t => new RepresentativeAssessmentResult(t.AssessmentId, t.Title, t.Id, t.Score, t.Finalized,
                skillRows.Where(s => s.AttemptId == t.Id && s.DistinctCount >= SkillAnalysisRules.MinimumQuestionCount &&
                    s.DistinctCount == s.QuestionCount && s.Maximum > 0 && Math.Round(s.Earned * 100m / s.Maximum, 2) == s.Score)
                    .OrderBy(s => s.Skill).Select(s => new DashboardSkillResult(s.Skill, s.Score, s.QuestionCount,
                        SkillAnalysisRules.IsWeak(s.Earned, s.Maximum, s.DistinctCount))).ToList())).ToList();
    }

    private async Task<List<SkillRecommendation>> RecommendationsAsync(Guid studentId, IReadOnlyList<CourseSummary> mine,
        IReadOnlyList<RepresentativeAssessmentResult> results, CancellationToken ct)
    {
        var weak = results.SelectMany(r => r.Skills.Where(s => s.IsWeak).Select(s => (Result: r, Skill: s))).ToList();
        if (weak.Count == 0) return [];
        var practices = await PracticesAsync(studentId, null, ct);
        var assessments = await AssessmentLinksAsync(studentId, null, ct);
        // Recommendations offer enrolled content. Independent assessments remain discoverable
        // separately, but are not treated as courses the student has joined.
        var courseIds = mine.Where(c => c.Status is CourseStatus.Published or CourseStatus.Unpublished).Select(c => c.Id).ToHashSet();
        return weak.Select(w =>
        {
            var contents = mine.Where(c => courseIds.Contains(c.Id) && c.Progress?.TotalLessons > 0 && c.Skills.Contains(w.Skill.Skill))
                .Select(c => new RecommendedContent(RecommendedContentKind.Course, c.Id, c.Title, c.Id))
                .Concat(practices.Where(p => courseIds.Contains(p.CourseId) && p.Skills.Contains(w.Skill.Skill))
                    .Select(p => new RecommendedContent(RecommendedContentKind.Practice, p.Id, p.Title, p.CourseId)))
                .Concat(assessments.Where(a => a.Status == AssessmentStatus.Published && a.CourseId.HasValue &&
                    courseIds.Contains(a.CourseId.Value) && a.Skills.Contains(w.Skill.Skill))
                    .Select(a => new RecommendedContent(RecommendedContentKind.Assessment, a.Id, a.Title, a.CourseId)))
                .OrderBy(c => c.Kind).ThenBy(c => c.Title).ThenBy(c => c.Id).Take(6).ToList();
            return new SkillRecommendation(w.Result.AssessmentId, w.Result.Title, w.Result.AttemptId,
                w.Skill.Skill, w.Skill.Score, contents);
        }).ToList();
    }
}

using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

// Shared read-only assessment evidence; independent of course/path completion services.
internal static class RepresentativeAssessmentQueries
{
    public static async Task<List<RepresentativeAssessmentResult>> ReadAsync(AppDbContext db, Guid studentId, CancellationToken ct)
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
                s.AttemptId,
                s.Skill,
                s.Score,
                s.QuestionCount,
                DistinctCount = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill)
                    .Select(q => q.QuestionId).Distinct().Count(),
                Maximum = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill).Sum(q => q.Points),
                Earned = s.Attempt.Assessment.Questions.Where(q => q.Question.PrimarySkill == s.Skill &&
                    s.Attempt.Answers.Any(a => a.AssessmentQuestionId == q.Id &&
                        (a.IsCorrect ?? q.Question.Options.Any(o => o.Id == a.SelectedOptionId && o.IsCorrect)))).Sum(q => q.Points)
            }).ToListAsync(ct);
        return representatives.OrderByDescending(t => t.Finalized).ThenBy(t => t.AssessmentId)
            .Select(t => new RepresentativeAssessmentResult(t.AssessmentId, t.Title, t.Id, t.Score, t.Finalized,
                skillRows.Where(s => s.AttemptId == t.Id && s.DistinctCount >= SkillAnalysisRules.MinimumQuestionCount &&
                    s.DistinctCount == s.QuestionCount && s.Maximum > 0 && Math.Round(s.Earned * 100m / s.Maximum, 2) == s.Score)
                    .OrderBy(s => s.Skill).Select(s => new DashboardSkillResult(s.Skill, s.Score, s.QuestionCount,
                        SkillAnalysisRules.IsWeak(s.Earned, s.Maximum, s.DistinctCount))).ToList())).ToList();
    }

}

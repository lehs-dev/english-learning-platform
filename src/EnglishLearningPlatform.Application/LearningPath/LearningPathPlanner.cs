using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.LearningPath;

public sealed record LearningPathWeakSkill(EnglishSkill Skill, decimal Score, Guid AttemptId);
public sealed record LearningPathCandidate(LearningPlanTargetType TargetType, Guid TargetId,
    string Title, EnglishSkill Skill, int EstimatedMinutes = 10);
public sealed record PlannedLearningActivity(int DayIndex, int Position, LearningPathCandidate Content);

public static class LearningPathPlanner
{
    public static IReadOnlyList<LearningPathWeakSkill> SelectWeakSkills(IEnumerable<RepresentativeAssessmentResult> results) =>
        results.SelectMany(result => result.Skills
                .Where(skill => skill.QuestionCount >= SkillAnalysisRules.MinimumQuestionCount)
                .Select(skill => new { Skill = skill, result.AttemptId, result.FinalizedAtUtc }))
            // More recent evidence for a skill supersedes older evidence from another assessment.
            .GroupBy(row => row.Skill.Skill)
            .Select(group => group.OrderByDescending(row => row.FinalizedAtUtc).ThenBy(row => row.AttemptId).First())
            .Where(row => row.Skill.IsWeak && row.Skill.Score < SkillAnalysisRules.WeakSkillThreshold)
            .OrderBy(row => row.Skill.Score).ThenBy(row => row.Skill.Skill)
            .Take(2).Select(row => new LearningPathWeakSkill(row.Skill.Skill, row.Skill.Score, row.AttemptId)).ToArray();

    public static IReadOnlyList<PlannedLearningActivity> Build(IReadOnlyList<EnglishSkill> weakSkills,
        IEnumerable<LearningPathCandidate> candidates, int durationDays = LearningPlan.DefaultDurationDays)
    {
        if (!LearningPlan.IsSupportedDuration(durationDays))
            throw new ArgumentOutOfRangeException(nameof(durationDays));

        var skills = weakSkills.Distinct().Take(2).ToArray();
        if (skills.Length == 0) return [];
        var pool = candidates.Where(c => skills.Contains(c.Skill) && c.EstimatedMinutes is >= 5 and <= 15 &&
                c.TargetType is LearningPlanTargetType.Lesson or LearningPlanTargetType.Practice or LearningPlanTargetType.FlashcardDeck)
            .GroupBy(c => new { c.TargetType, c.TargetId, c.Skill }).Select(group => group.First())
            .OrderBy(c => c.TargetType).ThenBy(c => c.TargetId).ThenBy(c => c.Skill).ToArray();
        var activities = Schedule(skills, pool, durationDays, reserveSingleUseContent: false);

        // A varied early schedule can otherwise spend all one-use lessons and decks
        // before the final days, even though repeating a practice would make the plan fit.
        return activities.Count > 0 ? activities : Schedule(skills, pool, durationDays, reserveSingleUseContent: true);
    }

    private static IReadOnlyList<PlannedLearningActivity> Schedule(EnglishSkill[] skills, LearningPathCandidate[] pool,
        int durationDays, bool reserveSingleUseContent)
    {
        var activities = new List<PlannedLearningActivity>();
        var usedOnce = new HashSet<(LearningPlanTargetType, Guid)>();
        var usage = new Dictionary<(LearningPlanTargetType, Guid), int>();
        for (int day = 1; day <= durationDays; day++)
        {
            var today = new List<LearningPathCandidate>();
            int minutes = 0;
            for (int position = 1; position <= 3; position++)
            {
                var preferred = skills[(day + position - 2) % skills.Length];
                var next = pool.Where(c => !today.Any(t => t.TargetType == c.TargetType && t.TargetId == c.TargetId) &&
                        (c.TargetType == LearningPlanTargetType.Practice || !usedOnce.Contains((c.TargetType, c.TargetId))) &&
                        minutes + c.EstimatedMinutes <= 30)
                    .OrderBy(c => reserveSingleUseContent && c.TargetType != LearningPlanTargetType.Practice)
                    // Combine a longer activity with a shorter one before using a third target.
                    .ThenBy(c => reserveSingleUseContent ? (position == 1 ? -c.EstimatedMinutes : c.EstimatedMinutes) : 0)
                    .ThenBy(c => c.Skill != preferred)
                    .ThenBy(c => usage.GetValueOrDefault((c.TargetType, c.TargetId)))
                    .ThenBy(c => c.TargetType).ThenBy(c => c.TargetId).FirstOrDefault();
                if (next is null) break;
                today.Add(next);
                minutes += next.EstimatedMinutes;
                usage[(next.TargetType, next.TargetId)] = usage.GetValueOrDefault((next.TargetType, next.TargetId)) + 1;
                if (next.TargetType != LearningPlanTargetType.Practice) usedOnce.Add((next.TargetType, next.TargetId));
                if (today.Count >= 2 && minutes >= 15) break;
            }
            if (today.Count < 2 || minutes < 15) return [];
            activities.AddRange(today.Select((content, index) => new PlannedLearningActivity(day, index + 1, content)));
        }
        return activities;
    }
}

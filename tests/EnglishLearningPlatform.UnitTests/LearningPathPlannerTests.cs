using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.LearningPath;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class LearningPathPlannerTests
{
    private static Guid Id(int number) => new(number, 0, 0, new byte[8]);

    [Fact]
    public void Planner_IsDeterministicAndSchedulesSevenDaysWithTwoWeakSkills()
    {
        var contents = new[]
        {
            new LearningPathCandidate(LearningPlanTargetType.Practice, Id(1), "Grammar", EnglishSkill.Grammar),
            new LearningPathCandidate(LearningPlanTargetType.Practice, Id(2), "Reading", EnglishSkill.Reading),
            new LearningPathCandidate(LearningPlanTargetType.Practice, Id(3), "Other skill", EnglishSkill.Listening)
        };
        var plan = LearningPathPlanner.Build([EnglishSkill.Grammar, EnglishSkill.Reading], contents);
        Assert.Equal(plan, LearningPathPlanner.Build([EnglishSkill.Grammar, EnglishSkill.Reading], contents.Reverse()));
        Assert.Equal(14, plan.Count);
        Assert.Equal(Enumerable.Range(1, 7), plan.Select(a => a.DayIndex).Distinct());
        foreach (var day in plan.GroupBy(a => a.DayIndex))
        {
            Assert.InRange(day.Count(), 2, 3);
            Assert.InRange(day.Sum(a => a.Content.EstimatedMinutes), 15, 30);
            Assert.Equal(2, day.Select(a => a.Content.TargetId).Distinct().Count());
            Assert.Equal(2, day.Select(a => a.Content.Skill).Distinct().Count());
        }
        Assert.DoesNotContain(plan, a => a.Content.Skill == EnglishSkill.Listening);
    }

    [Fact]
    public void SelectWeakSkills_UsesMostRecentValidEvidenceAndMaximumTwoSkills()
    {
        var old = new RepresentativeAssessmentResult(Id(1), "Old", Id(10), 30, DateTimeOffset.UnixEpoch,
            [new(EnglishSkill.Grammar, 10, 3, true)]);
        var current = new RepresentativeAssessmentResult(Id(2), "Latest", Id(11), 60, DateTimeOffset.UnixEpoch.AddDays(1),
            [new(EnglishSkill.Grammar, 80, 3, false), new(EnglishSkill.Vocabulary, 40, 2, true),
                new(EnglishSkill.Listening, 59.99m, 3, true), new(EnglishSkill.Reading, 20, 4, true)]);
        var skills = LearningPathPlanner.SelectWeakSkills([old, current]);
        Assert.Equal([EnglishSkill.Reading, EnglishSkill.Listening], skills.Select(s => s.Skill));
        Assert.All(skills, s => Assert.Equal(current.AttemptId, s.AttemptId));
    }

    [Theory]
    [InlineData("60", false)]
    [InlineData("59.99", true)]
    public void WeakSkillThreshold_IsStrict(string score, bool expected)
    {
        var value = decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture);
        var result = new RepresentativeAssessmentResult(Id(1), "Assessment", Id(10), value, DateTimeOffset.UnixEpoch,
            [new(EnglishSkill.Reading, value, 3, expected)]);
        Assert.Equal(expected, LearningPathPlanner.SelectWeakSkills([result]).Count == 1);
    }

    [Fact]
    public void Lessons_AreNotRepeatedAndInsufficientContentIsExplicit()
    {
        var lessons = Enumerable.Range(1, 14).Select(number => new LearningPathCandidate(
            LearningPlanTargetType.Lesson, Id(number), $"Lesson {number}", EnglishSkill.Grammar)).ToArray();
        var plan = LearningPathPlanner.Build([EnglishSkill.Grammar], lessons);
        Assert.Equal(14, plan.Select(a => a.Content.TargetId).Distinct().Count());
        Assert.Empty(LearningPathPlanner.Build([EnglishSkill.Grammar], lessons.Take(2)));
        Assert.Empty(LearningPathPlanner.Build([EnglishSkill.Listening], lessons));
        Assert.Empty(LearningPathPlanner.Build([], lessons));
    }

    [Fact]
    public void FlashcardDays_UseThreeShortActivitiesToReachFifteenMinutes()
    {
        var decks = Enumerable.Range(1, 21).Select(number => new LearningPathCandidate(
            LearningPlanTargetType.FlashcardDeck, Id(number), $"Deck {number}", EnglishSkill.Vocabulary, 5));
        var plan = LearningPathPlanner.Build([EnglishSkill.Vocabulary], decks);
        Assert.Equal(21, plan.Count);
        Assert.Equal(21, plan.Select(a => a.Content.TargetId).Distinct().Count());
        Assert.All(plan.GroupBy(a => a.DayIndex), day => Assert.Equal(15, day.Sum(a => a.Content.EstimatedMinutes)));
    }

    [Fact]
    public void FlashcardDeck_IsNotScheduledEveryDayAgainstSpacedRepetition()
    {
        var decks = Enumerable.Range(1, 3).Select(number => new LearningPathCandidate(
            LearningPlanTargetType.FlashcardDeck, Id(number), $"Deck {number}", EnglishSkill.Vocabulary, 5));
        Assert.Empty(LearningPathPlanner.Build([EnglishSkill.Vocabulary], decks));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(30)]
    public void LongerPlans_CanReserveDecksForEveryDayAlongsideRepeatablePractice(int days)
    {
        var practice = new LearningPathCandidate(LearningPlanTargetType.Practice, Id(1), "Practice", EnglishSkill.Vocabulary);
        var decks = Enumerable.Range(2, days).Select(number => new LearningPathCandidate(
            LearningPlanTargetType.FlashcardDeck, Id(number), $"Deck {number}", EnglishSkill.Vocabulary, 5));
        var contents = decks.Append(practice).ToArray();

        var plan = LearningPathPlanner.Build([EnglishSkill.Vocabulary], contents, days);

        Assert.Equal(plan, LearningPathPlanner.Build([EnglishSkill.Vocabulary], contents.Reverse(), days));
        Assert.Equal(Enumerable.Range(1, days), plan.Select(a => a.DayIndex).Distinct());
        Assert.Equal(days, plan.Where(a => a.Content.TargetType == LearningPlanTargetType.FlashcardDeck).Select(a => a.Content.TargetId).Distinct().Count());
        Assert.All(plan.GroupBy(a => a.DayIndex), day =>
        {
            Assert.Equal(2, day.Count());
            Assert.Equal(15, day.Sum(a => a.Content.EstimatedMinutes));
            Assert.Contains(day, a => a.Content.TargetId == practice.TargetId);
        });
        Assert.Empty(LearningPathPlanner.Build([EnglishSkill.Vocabulary], contents.Skip(1), days));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(29)]
    [InlineData(int.MaxValue)]
    public void Planner_RejectsUnsupportedDuration(int days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LearningPathPlanner.Build([EnglishSkill.Grammar], [], days));
}

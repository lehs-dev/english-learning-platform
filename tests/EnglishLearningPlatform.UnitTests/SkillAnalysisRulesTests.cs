using EnglishLearningPlatform.Application.Learning;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class SkillAnalysisRulesTests
{
    [Theory]
    [InlineData(59.999, 100, 3, true)]
    [InlineData(60, 100, 3, false)]
    [InlineData(1, 100, 2, false)]
    [InlineData(0, 0, 3, false)]
    [InlineData(3, 5, 5, false)]
    public void WeakSkill_RequiresEnoughQuestionsAndComparesBeforeRounding(decimal earned, decimal maximum, int count, bool expected) =>
        Assert.Equal(expected, SkillAnalysisRules.IsWeak(earned, maximum, count));
}

using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.Practices;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Practice;

public sealed class PracticeServiceAuthorizationTests
{
    [Fact]
    public async Task StudentOperations_RejectInactiveStudentThroughSharedValidatorBeforeDatabaseAccess()
    {
        // No provider is configured: an accidental database query fails this unit test.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var validator = new InactiveUserValidator();
        var service = new PracticeService(db, TimeProvider.System, new QuestionUsageValidator(new CreateQuestionValidator()),
            validator, new EqualWeightPracticeScoringStrategy());
        var studentId = Guid.NewGuid();
        var practiceId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        var sheet = await service.GetSheetAsync(studentId, practiceId, cancellation.Token);
        var submission = await service.SubmitAsync(studentId, practiceId, [], cancellation.Token);
        var result = await service.GetResultAsync(studentId, practiceId, cancellation.Token);

        Assert.Equal(LearningAccessResult.Forbidden, sheet.Access);
        Assert.Equal(PracticeSubmitStatus.Forbidden, submission.Status);
        Assert.Equal(LearningAccessResult.Forbidden, result.Access);
        Assert.Equal(3, validator.Calls.Count);
        Assert.All(validator.Calls, call => Assert.Equal((studentId, cancellation.Token), call));
    }

    private sealed class InactiveUserValidator : IActiveUserValidator
    {
        public List<(Guid, CancellationToken)> Calls { get; } = [];

        public Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct = default)
        {
            Calls.Add((userId, ct));
            return Task.FromResult(false);
        }

        public Task<bool> IsActiveRoleAsync(Guid userId, string role, CancellationToken ct = default) =>
            throw new InvalidOperationException("Expected student validation.");

        public Task<bool> IsActiveTeacherAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Expected student validation.");

        public Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Expected student validation.");
    }
}

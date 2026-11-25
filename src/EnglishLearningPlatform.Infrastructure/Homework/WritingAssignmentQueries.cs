using EnglishLearningPlatform.Domain.Entities;

namespace EnglishLearningPlatform.Infrastructure.Homework;

internal static class WritingAssignmentQueries
{
    public static IQueryable<WritingAssignment> ReleasedAt(this IQueryable<WritingAssignment> assignments, DateTimeOffset now) =>
        assignments.Where(assignment => !assignment.IsDraft &&
            (!assignment.PublishAtUtc.HasValue || assignment.PublishAtUtc.Value <= now));
}

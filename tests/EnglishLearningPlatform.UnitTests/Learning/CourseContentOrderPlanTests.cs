using EnglishLearningPlatform.Infrastructure.Learning.Content;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Learning;

public sealed class CourseContentOrderPlanTests
{
    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(-8, -3, 2)]
    public void TwoPassReordering_AvoidsEveryExistingAndFinalIndexThenAppliesTheRequestedPermutation(int first, int second, int third)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var indices = new[] { first, second, third };
        var entries = Enumerable.Range(0, ids.Length).Select(i =>
        {
            var index = i;
            return new CourseContentOrderEntry(ids[i], indices[i], value => indices[index] = value);
        }).ToArray();
        var requested = ids.Reverse().ToArray();

        Assert.True(CourseContentOrderPlan.TryCreate(entries, requested, out var plan, out var error));
        Assert.Null(error);
        plan!.ApplyTemporary();
        Assert.Equal(3, indices.Distinct().Count());
        Assert.All(indices, index => Assert.True(index < Math.Min(0, Math.Min(first, Math.Min(second, third)))));
        plan.ApplyFinal();
        Assert.Equal([2, 1, 0], indices);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public void InvalidPermutation_IsRejectedBeforeAnyMutation_AndBeforeIndexRangeValidation(string scenario)
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        CourseContentOrderEntry[] entries =
        [
            new(first, int.MinValue, _ => throw new InvalidOperationException()),
            new(second, 0, _ => throw new InvalidOperationException())
        ];
        Guid[]? requested = scenario switch
        {
            "null" => null,
            "missing" => [first],
            "duplicate" => [first, first],
            _ => [first, Guid.NewGuid()]
        };

        Assert.False(CourseContentOrderPlan.TryCreate(entries, requested, out var plan, out var error));
        Assert.Null(plan);
        Assert.Equal("Thứ tự phải chứa đúng mỗi ID của cùng parent một lần.", error);
    }

    [Fact]
    public void UnderflowingTemporaryRange_IsRejectedWithoutChangingEntries()
    {
        var id = Guid.NewGuid();
        CourseContentOrderEntry[] entries = [new(id, int.MinValue, _ => throw new InvalidOperationException())];
        Assert.False(CourseContentOrderPlan.TryCreate(entries, [id], out var plan, out var error));
        Assert.Null(plan);
        Assert.Equal("OrderIndex vượt giới hạn; cần chuẩn hóa dữ liệu.", error);
    }

    [Fact]
    public void EmptyParent_AllowsAnEmptyReorder()
    {
        Assert.True(CourseContentOrderPlan.TryCreate([], [], out var plan, out var error));
        Assert.Null(error);
        plan!.ApplyTemporary();
        plan.ApplyFinal();
    }
}

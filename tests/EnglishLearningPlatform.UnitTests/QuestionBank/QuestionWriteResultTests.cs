using EnglishLearningPlatform.Application.QuestionBank;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionWriteResultTests
{
    [Fact]
    public void Success_ReturnsIdAndNoErrors()
    {
        var id = Guid.NewGuid();

        var result = QuestionWriteResult.Success(id);

        Assert.True(result.Succeeded);
        Assert.Equal(id, result.QuestionId);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Success_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() =>
            QuestionWriteResult.Success(Guid.Empty));
    }

    [Fact]
    public void Failure_ReturnsErrorsAndNoId()
    {
        var error = new QuestionError(
            "Content", "required", "Content is required.");

        var result = QuestionWriteResult.Failure([error]);

        Assert.False(result.Succeeded);
        Assert.Null(result.QuestionId);
        Assert.Equal(error, Assert.Single(result.Errors));
    }

    [Fact]
    public void Failure_RejectsNullCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            QuestionWriteResult.Failure(null!));
    }

    [Fact]
    public void Failure_RejectsEmptyCollection()
    {
        Assert.Throws<ArgumentException>(() =>
            QuestionWriteResult.Failure([]));
    }

    [Fact]
    public void Failure_RejectsNullEntry()
    {
        Assert.Throws<ArgumentException>(() =>
            QuestionWriteResult.Failure([null!]));
    }

    [Fact]
    public void Failure_KeepsReadOnlySnapshotOfErrors()
    {
        var error = new QuestionError(
            "Content", "required", "Content is required.");
        var source = new List<QuestionError> { error };

        var result = QuestionWriteResult.Failure(source);
        source.Clear();

        Assert.Equal(error, Assert.Single(result.Errors));

        var collection =
            Assert.IsAssignableFrom<ICollection<QuestionError>>(
                result.Errors);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            collection.Clear());
    }
}
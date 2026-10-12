using EnglishLearningPlatform.Application.QuestionBank;
using Xunit;
namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public class QuestionOptionItemTests
{
    [Fact]
    public void QuestionOptionItem_WithValues_StoresOption()
    {
        //Arrange
        Guid id = Guid.NewGuid();
        string content = "Option Content";
        int orderIndex = 1;
        bool isCorrect = true;
        var questionOptionItem = new QuestionOptionItem
        {
            Id = id,
            Content = content,
            OrderIndex = orderIndex,
            IsCorrect = isCorrect
        };
        //Assert
        Assert.Equal(id, questionOptionItem.Id);
        Assert.Equal(content, questionOptionItem.Content);
        Assert.Equal(orderIndex, questionOptionItem.OrderIndex);
        Assert.Equal(isCorrect, questionOptionItem.IsCorrect);
    }
    [Fact]
    public void QuestionOptionItem_NewInstance_HasEmptyContent()
    {
        var questionOptionItem = new QuestionOptionItem();
        Assert.Equal(string.Empty, questionOptionItem.Content);
    }
}

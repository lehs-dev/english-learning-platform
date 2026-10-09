using EnglishLearningPlatform.Application.QuestionBank;
using Xunit;
namespace EnglishLearningPlatform.UnitTests.QuestionBank;
public sealed class QuestionSearchRequestTests
{
    // Khi chưa chọn bộ lọc, request không được tự giới hạn
    // vào một từ khóa, kỹ năng, level, độ khó hoặc trạng thái cụ thể.
    //khi chua chon so trang va so luong cau hoi, request se co gia tri mac dinh la 1 va 10
    // Khi chưa cấu hình phân trang, request dùng trang 1 và 10 câu/trang
    [Fact]
    public void QuestionSearchRequest_DefaultValues_ShouldBeNull()
    {
        QuestionSearchRequest request = new QuestionSearchRequest();
        Assert.Null(request.Search);
        Assert.Null(request.PrimarySkill);
        Assert.Null(request.Level);
        Assert.Null(request.Difficulty);
        Assert.Null(request.Status);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(10, request.PageSize);
    }
}
using EnglishLearningPlatform.Infrastructure.QuestionBank;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionQueryServiceTests
{
    // Service phải báo lỗi khi thiếu database context.
    [Fact]
    public void Constructor_NullDb_ThrowsArgumentNullException()
    {
        // Act và Assert.
        // null! dùng để cố ý truyền null trong test.
        // Dấu ! chỉ bỏ cảnh báo nullable, không thay đổi giá trị null.
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(() =>
                new QuestionQueryService(null!, null!));

        Assert.Equal("db", exception.ParamName);
    }

    // Service phải báo lỗi khi thiếu UserManager.
    [Fact]
    public void Constructor_NullUserManager_ThrowsArgumentNullException()
    {
        // Arrange: tạo context nhưng không thực hiện truy vấn database.
        DbContextOptions<AppDbContext> options =
            new DbContextOptionsBuilder<AppDbContext>().Options;

        // using bảo đảm context được giải phóng khi kết thúc khối này.
        using (AppDbContext db = new AppDbContext(options))
        {
            // Act và Assert.
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() =>
                    new QuestionQueryService(db, null!));

            Assert.Equal("userManager", exception.ParamName);
        }
    }
}
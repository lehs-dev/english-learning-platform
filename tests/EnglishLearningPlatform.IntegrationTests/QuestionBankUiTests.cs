using System.Net;
using System.Net.Http;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

// Dùng fixture có sẵn để khởi động ứng dụng và database kiểm thử riêng.
// Không sử dụng hoặc thay đổi 35 câu hỏi trong database demo.
public sealed class QuestionBankUiTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public QuestionBankUiTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // Theory chạy cùng một hàm test với từng đường dẫn trong InlineData.
    [Theory]
    [InlineData("/TeacherQuestionBank")]
    [InlineData(
        "/TeacherQuestionBank/Details/11111111-1111-1111-1111-111111111111")]
    public async Task Guest_QuestionBankPage_RedirectsToLogin(
        string path)
    {
        // Helper này đã có trong dự án.
        // Nó tạo client không tự đi theo đường dẫn chuyển hướng.
        using (HttpClient client =
            IdentityTestHelpers.CreateClient(_factory))
        {
            // Không đăng nhập trước khi gửi yêu cầu:
            // client hiện tại đại diện cho Guest.
            using (HttpResponseMessage response =
                await client.GetAsync(path))
            {
                // Guest phải được chuyển hướng, không được nhận trang câu hỏi.
                Assert.Equal(
                    HttpStatusCode.Redirect,
                    response.StatusCode);

                // Location là đường dẫn mà ứng dụng yêu cầu chuyển tới.
                Uri? location = response.Headers.Location;

                Assert.NotNull(location);

                // Phải chuyển về trang đăng nhập.
                Assert.Contains(
                    "/Account/Login",
                    location.ToString());
            }
        }
    }
}
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using EnglishLearningPlatform.Infrastructure.QuestionBank;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class QuestionQueryServiceSearchTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    // Dùng fixture database kiểm thử đang có trong dự án.
    public QuestionQueryServiceSearchTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // Kiểm tra owner, tìm từ khóa, thứ tự và phân trang.
    [Fact]
    public async Task SearchAsync_ReturnsOnlyMatchingOwnQuestionsAndPaginates()
    {
        // Arrange: tạo hai Teacher khác nhau.
        ApplicationUser owner =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        ApplicationUser otherTeacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        // Dùng thời gian cố định để kiểm tra thứ tự ổn định.
        DateTimeOffset createdAt = new DateTimeOffset(
            2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        Guid olderQuestionId = await CreateQuestionAsync(
            owner.Id,
            "bank sample - older",
            createdAt);

        Guid newerQuestionId = await CreateQuestionAsync(
            owner.Id,
            "bank sample - newer",
            createdAt.AddMinutes(1));

        // Câu của owner nhưng không khớp từ khóa phải bị loại.
        await CreateQuestionAsync(
            owner.Id,
            "unrelated question",
            createdAt.AddMinutes(2));

        // Câu khớp từ khóa nhưng thuộc Teacher khác phải bị loại.
        await CreateQuestionAsync(
            otherTeacher.Id,
            "bank sample - other teacher",
            createdAt.AddMinutes(3));

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            // Tạo service trực tiếp vì chưa đăng ký DI cho service mới.
            QuestionQueryService service =
                new QuestionQueryService(db, users);

            QuestionSearchRequest request = new QuestionSearchRequest
            {
                Search = " bank sample ",
                PageNumber = 1,
                PageSize = 1
            };

            // Act: lấy trang đầu tiên.
            QuestionSearchResult? firstPage =
                await service.SearchAsync(owner.Id, request);

            // Assert: chỉ có hai câu của owner khớp từ khóa.
            Assert.NotNull(firstPage);
            Assert.Equal(2, firstPage.TotalCount);
            Assert.Equal(2, firstPage.TotalPages);
            Assert.Equal(1, firstPage.PageNumber);
            Assert.Equal(1, firstPage.PageSize);
            Assert.Single(firstPage.Items);

            // Câu mới hơn phải xuất hiện ở trang đầu.
            Assert.Equal(newerQuestionId, firstPage.Items[0].Id);
            Assert.Equal(2, firstPage.Items[0].OptionCount);

            // Act: chuyển sang trang thứ hai.
            request.PageNumber = 2;

            QuestionSearchResult? secondPage =
                await service.SearchAsync(owner.Id, request);

            // Assert: trang thứ hai chứa câu còn lại.
            Assert.NotNull(secondPage);
            Assert.Equal(2, secondPage.TotalCount);
            Assert.Equal(2, secondPage.PageNumber);
            Assert.Single(secondPage.Items);
            Assert.Equal(olderQuestionId, secondPage.Items[0].Id);
        }
    }

    // Student và Admin đều không được truy vấn Question Bank.
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task SearchAsync_NonTeacher_ReturnsNull(string role)
    {
        // Arrange.
        ApplicationUser user =
            await IdentityTestHelpers.CreateUserAsync(_factory, role);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            QuestionQueryService service =
                new QuestionQueryService(db, users);

            // Act.
            QuestionSearchResult? result = await service.SearchAsync(
                user.Id,
                new QuestionSearchRequest());

            // Assert: từ chối quyền truy cập, không trả danh sách.
            Assert.Null(result);
        }
    }

    // Teacher hợp lệ chưa có câu hỏi phải nhận kết quả rỗng, không phải null.
    [Fact]
    public async Task SearchAsync_TeacherWithoutQuestions_ReturnsEmptyResult()
    {
        // Arrange: Teacher mới chưa có câu hỏi nào.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            QuestionQueryService service =
                new QuestionQueryService(db, users);

            // Act.
            QuestionSearchResult? result = await service.SearchAsync(
                teacher.Id,
                new QuestionSearchRequest());

            // Assert.
            Assert.NotNull(result);
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
            Assert.Equal(0, result.TotalPages);
        }
    }

    // Tạo dữ liệu kiểm thử trực tiếp trong database riêng của fixture.
    // Mỗi câu có hai option và đúng một đáp án đúng.
    // Các tham số tùy chọn giúp test tạo câu hỏi với metadata khác nhau.
    // Những test cũ truyền ba tham số vẫn sử dụng giá trị mặc định.
    private async Task<Guid> CreateQuestionAsync(
        Guid ownerId,
        string content,
        DateTimeOffset createdAt,
        EnglishSkill primarySkill = EnglishSkill.Reading,
        string? level = "A1",
        string? difficulty = "Easy",
        QuestionStatus status = QuestionStatus.Draft)
    {
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            Question question = new Question
            {
                OwnerTeacherUserId = ownerId,
                Content = content,
                PrimarySkill = primarySkill,
                Level = level,
                Difficulty = difficulty,
                Status = status,
                CreatedAtUtc = createdAt
            };

            question.Options.Add(new QuestionOption
            {
                Content = "Option 1",
                OrderIndex = 0,
                IsCorrect = false
            });

            question.Options.Add(new QuestionOption
            {
                Content = "Option 2",
                OrderIndex = 1,
                IsCorrect = true
            });

            db.Questions.Add(question);
            await db.SaveChangesAsync();

            return question.Id;
        }
    }

    // Kiểm tra từng skill và bảo đảm không lấy câu hỏi của Teacher khác.
    [Theory]
    [InlineData(EnglishSkill.Vocabulary)]
    [InlineData(EnglishSkill.Grammar)]
    [InlineData(EnglishSkill.Reading)]
    [InlineData(EnglishSkill.Listening)]
    public async Task SearchAsync_FilterBySkill_ReturnsOnlyMatchingOwnQuestion(
        EnglishSkill selectedSkill)
    {
        // Arrange: tạo Teacher sở hữu dữ liệu và một Teacher khác.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        ApplicationUser otherTeacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        DateTimeOffset createdAt = new DateTimeOffset(
            2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

        EnglishSkill[] skills = new EnglishSkill[]
        {
            EnglishSkill.Vocabulary,
            EnglishSkill.Grammar,
            EnglishSkill.Reading,
            EnglishSkill.Listening
        };

        Guid expectedQuestionId = Guid.Empty;

        // Tạo một câu hỏi cho mỗi skill trong ngân hàng của Teacher.
        foreach (EnglishSkill skill in skills)
        {
            Guid questionId = await CreateQuestionAsync(
                teacher.Id,
                "Question for skill filtering",
                createdAt,
                primarySkill: skill);

            // Ghi nhớ ID của câu hỏi mà bộ lọc phải trả về.
            if (skill == selectedSkill)
            {
                expectedQuestionId = questionId;
            }
        }

        // Câu hỏi này khớp skill nhưng không thuộc Teacher đang tìm.
        await CreateQuestionAsync(
            otherTeacher.Id,
            "Another teacher's matching question",
            createdAt,
            primarySkill: selectedSkill);

        // Kiểm tra dữ liệu chuẩn bị đã có câu hỏi mong đợi.
        Assert.NotEqual(Guid.Empty, expectedQuestionId);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            QuestionQueryService service =
                new QuestionQueryService(db, users);

            QuestionSearchRequest request = new QuestionSearchRequest
            {
                PrimarySkill = selectedSkill
            };

            // Act.
            QuestionSearchResult? result =
                await service.SearchAsync(teacher.Id, request);

            // Assert: phải trả đúng câu hỏi, không chỉ đúng skill.
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalCount);
            Assert.Single(result.Items);
            Assert.Equal(expectedQuestionId, result.Items[0].Id);
            Assert.Equal(selectedSkill, result.Items[0].PrimarySkill);
        }
    }

    // Kiểm tra từng bộ lọc metadata hoạt động độc lập.
    // Level và Difficulty có khoảng trắng ở đầu/cuối để kiểm tra Trim.
    // Mỗi bộ lọc được chạy với hai giá trị để phát hiện code lọc viết cố định.
    [Theory]
    [InlineData(nameof(QuestionSearchRequest.Level), false)]
    [InlineData(nameof(QuestionSearchRequest.Level), true)]
    [InlineData(nameof(QuestionSearchRequest.Difficulty), false)]
    [InlineData(nameof(QuestionSearchRequest.Difficulty), true)]
    [InlineData(nameof(QuestionSearchRequest.Status), false)]
    [InlineData(nameof(QuestionSearchRequest.Status), true)]
    public async Task SearchAsync_FilterByMetadata_ReturnsOnlyMatchingQuestion(
        string filterName,
        bool selectSecondQuestion)
    {
        // Arrange.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        DateTimeOffset createdAt = new DateTimeOffset(
            2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

        // Câu thứ nhất: A1, Easy, Archived.
        Guid firstQuestionId = await CreateQuestionAsync(
            teacher.Id,
            "First metadata question",
            createdAt,
            status: QuestionStatus.Archived);

        // Câu thứ hai: B1, Hard, Published; khác câu thứ nhất ở cả ba metadata.
        Guid secondQuestionId = await CreateQuestionAsync(
            teacher.Id,
            "Second metadata question",
            createdAt.AddMinutes(1),
            level: "B1",
            difficulty: "Hard",
            status: QuestionStatus.Published);

        // false: tìm câu thứ nhất; true: tìm câu thứ hai.
        // Ghi nhớ cả ID và metadata để kiểm tra dữ liệu service thực sự trả về.
        Guid expectedQuestionId = firstQuestionId;
        string expectedLevel = "A1";
        string expectedDifficulty = "Easy";
        QuestionStatus expectedStatus = QuestionStatus.Archived;

        if (selectSecondQuestion)
        {
            expectedQuestionId = secondQuestionId;
            expectedLevel = "B1";
            expectedDifficulty = "Hard";
            expectedStatus = QuestionStatus.Published;
        }

        QuestionSearchRequest request = new QuestionSearchRequest();

        // Mỗi lần chạy chỉ bật một bộ lọc.
        if (filterName == nameof(QuestionSearchRequest.Level))
        {
            request.Level = " " + expectedLevel + " ";
        }
        else if (filterName == nameof(QuestionSearchRequest.Difficulty))
        {
            request.Difficulty = " " + expectedDifficulty + " ";
        }
        else if (filterName == nameof(QuestionSearchRequest.Status))
        {
            request.Status = expectedStatus;
        }
        else
        {
            // Nếu khai báo InlineData sai tên bộ lọc, test phải báo lỗi ngay.
            throw new ArgumentException("Tên bộ lọc không hợp lệ.", nameof(filterName));
        }

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            QuestionQueryService service =
                new QuestionQueryService(db, users);

            // Act.
            QuestionSearchResult? result =
                await service.SearchAsync(teacher.Id, request);

            // Assert.
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalCount);
            Assert.Single(result.Items);
            Assert.Equal(expectedQuestionId, result.Items[0].Id);
            Assert.Equal(expectedLevel, result.Items[0].Level);
            Assert.Equal(expectedDifficulty, result.Items[0].Difficulty);
            Assert.Equal(expectedStatus, result.Items[0].Status);
        }
    }

    // Câu hỏi phải đáp ứng tất cả điều kiện được chọn.
    // Mỗi câu gây nhiễu bên dưới cố ý sai đúng một điều kiện.
    [Fact]
    public async Task SearchAsync_CombinedFilters_ReturnsOnlyQuestionMatchingAllConditions()
    {
        // Arrange.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        ApplicationUser otherTeacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        DateTimeOffset createdAt = new DateTimeOffset(
            2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

        // Câu này khớp mọi điều kiện:
        // đúng owner, từ khóa, Reading, A1, Easy và Draft.
        Guid expectedQuestionId = await CreateQuestionAsync(
            teacher.Id,
            "target filter expected question",
            createdAt);

        // Sai skill.
        await CreateQuestionAsync(
            teacher.Id,
            "target filter wrong skill",
            createdAt.AddMinutes(1),
            primarySkill: EnglishSkill.Grammar);

        // Sai level.
        await CreateQuestionAsync(
            teacher.Id,
            "target filter wrong level",
            createdAt.AddMinutes(2),
            level: "B1");

        // Sai difficulty.
        await CreateQuestionAsync(
            teacher.Id,
            "target filter wrong difficulty",
            createdAt.AddMinutes(3),
            difficulty: "Hard");

        // Sai status.
        await CreateQuestionAsync(
            teacher.Id,
            "target filter wrong status",
            createdAt.AddMinutes(4),
            status: QuestionStatus.Published);

        // Sai từ khóa.
        await CreateQuestionAsync(
            teacher.Id,
            "unrelated question",
            createdAt.AddMinutes(5));

        // Khớp các bộ lọc nhưng sai owner.
        await CreateQuestionAsync(
            otherTeacher.Id,
            "target filter another teacher",
            createdAt.AddMinutes(6));

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            UserManager<ApplicationUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            QuestionQueryService service =
                new QuestionQueryService(db, users);

            QuestionSearchRequest request = new QuestionSearchRequest
            {
                Search = " target filter ",
                PrimarySkill = EnglishSkill.Reading,
                Level = " A1 ",
                Difficulty = " Easy ",
                Status = QuestionStatus.Draft
            };

            // Act.
            QuestionSearchResult? result =
                await service.SearchAsync(teacher.Id, request);

            // Assert: chỉ câu khớp tất cả điều kiện được trả về.
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalCount);
            Assert.Single(result.Items);
            Assert.Equal(expectedQuestionId, result.Items[0].Id);
        }
    }
}

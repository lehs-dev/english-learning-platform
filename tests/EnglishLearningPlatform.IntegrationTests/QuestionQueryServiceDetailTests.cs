using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class QuestionQueryServiceDetailTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    // Dùng lại fixture có sẵn để chạy với database kiểm thử riêng.
    public QuestionQueryServiceDetailTests(
        IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // Chuẩn bị một câu hỏi thuộc Teacher được truyền vào.
    // Hàm này chỉ tạo dữ liệu test, không phải chức năng của ứng dụng.
    private async Task<Guid> CreateQuestionAsync(
    Guid ownerId,
    Stimulus? stimulus = null)
    {
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            Question question = new Question
            {
                OwnerTeacherUserId = ownerId,
                Content = "Choose the correct answer.",
                PrimarySkill = EnglishSkill.Reading,
                Level = "A1",
                Explanation = "Option 2 is correct.",
                Status = QuestionStatus.Draft
            };
            // Gắn passage/audio nếu test có truyền vào.
            // EF sẽ lưu cả Stimulus mới khi thêm Question có tham chiếu tới nó.
            question.Stimulus = stimulus;

            // Dữ liệu test audio dùng câu hỏi Listening cho đúng nghiệp vụ.
            // Không có Stimulus hoặc dùng passage thì giữ Reading như hiện tại.
            if (stimulus != null
                && stimulus.StimulusType == StimulusType.Audio)
            {
                question.PrimarySkill = EnglishSkill.Listening;
            }

            // Cố ý thêm option thứ hai trước.
            // Service phải trả options theo OrderIndex,
            // không dựa vào thứ tự thêm vào collection.
            question.Options.Add(new QuestionOption
            {
                Content = "Option 2",
                OrderIndex = 1,
                IsCorrect = true
            });

            question.Options.Add(new QuestionOption
            {
                Content = "Option 1",
                OrderIndex = 0,
                IsCorrect = false
            });

            db.Questions.Add(question);
            await db.SaveChangesAsync();

            return question.Id;
        }
    }

    // Owner phải nhận được đúng câu hỏi và các options đã sắp xếp.
    [Fact]
    public async Task GetByIdAsync_Owner_ReturnsQuestionAndOrderedOptions()
    {
        // Arrange: tạo Teacher và câu hỏi thuộc Teacher đó.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        Guid questionId = await CreateQuestionAsync(teacher.Id);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            // Lấy qua interface để sử dụng đúng đăng ký DI vừa thêm.
            IQuestionQueryService service = scope.ServiceProvider
                .GetRequiredService<IQuestionQueryService>();

            // Act.
            QuestionDetail? detail = await service.GetByIdAsync(
                teacher.Id,
                questionId);

            // Assert: không chỉ kiểm tra có kết quả,
            // mà còn kiểm tra đúng câu hỏi và đúng dữ liệu.
            Assert.NotNull(detail);
            Assert.Equal(questionId, detail.Id);
            Assert.Equal("Choose the correct answer.", detail.Content);
            Assert.Equal(EnglishSkill.Reading, detail.PrimarySkill);
            Assert.Equal("A1", detail.Level);
            Assert.Equal("Option 2 is correct.", detail.Explanation);
            Assert.Equal(QuestionStatus.Draft, detail.Status);

            Assert.Equal(2, detail.Options.Count);

            // Option đầu tiên phải có OrderIndex = 0.
            Assert.Equal(0, detail.Options[0].OrderIndex);
            Assert.Equal("Option 1", detail.Options[0].Content);
            Assert.False(detail.Options[0].IsCorrect);
            Assert.NotEqual(Guid.Empty, detail.Options[0].Id);

            // Option tiếp theo phải có OrderIndex = 1.
            Assert.Equal(1, detail.Options[1].OrderIndex);
            Assert.Equal("Option 2", detail.Options[1].Content);
            Assert.True(detail.Options[1].IsCorrect);
            Assert.NotEqual(Guid.Empty, detail.Options[1].Id);

            // Dữ liệu chuẩn bị chưa gắn passage/audio.
            Assert.Null(detail.Stimulus);
        }
    }

    // Teacher khác không được xem câu hỏi dù biết đúng ID.
    [Fact]
    public async Task GetByIdAsync_OtherTeacher_ReturnsNull()
    {
        // Arrange: tạo owner và một Teacher khác.
        ApplicationUser owner =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        ApplicationUser otherTeacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        // Câu hỏi thực sự tồn tại, nhưng thuộc owner.
        Guid questionId = await CreateQuestionAsync(owner.Id);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IQuestionQueryService service = scope.ServiceProvider
                .GetRequiredService<IQuestionQueryService>();

            // Act: Teacher khác yêu cầu xem câu hỏi của owner.
            QuestionDetail? detail = await service.GetByIdAsync(
                otherTeacher.Id,
                questionId);

            // Assert: không được trả nội dung, options hoặc đáp án.
            Assert.Null(detail);
        }
    }
    // Kiểm tra service lấy và chuyển đúng dữ liệu passage/audio sang DTO.
    [Theory]
    [InlineData(StimulusType.Passage)]
    [InlineData(StimulusType.Audio)]
    public async Task GetByIdAsync_OwnerWithStimulus_ReturnsStimulusDetails(
        StimulusType stimulusType)
    {
        // Arrange: Teacher sở hữu cả câu hỏi và Stimulus.
        ApplicationUser teacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        Stimulus stimulus = new Stimulus
        {
            OwnerTeacherUserId = teacher.Id,
            StimulusType = stimulusType,
            Title = "Stimulus for detail testing"
        };

        // Passage có nội dung văn bản; audio có URL tài nguyên.
        // URL này chỉ là dữ liệu test, test không tải file audio.
        if (stimulusType == StimulusType.Passage)
        {
            stimulus.ContentText = "This is a short reading passage.";
        }
        else
        {
            stimulus.ResourceUrl = "https://example.test/audio/sample.mp3";
        }

        Guid questionId = await CreateQuestionAsync(
            teacher.Id,
            stimulus);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IQuestionQueryService service = scope.ServiceProvider
                .GetRequiredService<IQuestionQueryService>();

            // Act.
            QuestionDetail? detail = await service.GetByIdAsync(
                teacher.Id,
                questionId);

            // Assert: kiểm tra đúng câu hỏi và đúng Stimulus được gắn.
            Assert.NotNull(detail);
            Assert.Equal(questionId, detail.Id);
            Assert.NotNull(detail.Stimulus);

            Assert.Equal(stimulus.Id, detail.Stimulus.Id);
            Assert.Equal(stimulusType, detail.Stimulus.StimulusType);
            Assert.Equal(stimulus.Title, detail.Stimulus.Title);
            Assert.Equal(stimulus.ContentText, detail.Stimulus.ContentText);
            Assert.Equal(stimulus.ResourceUrl, detail.Stimulus.ResourceUrl);

            // Kiểm tra skill của câu hỏi không bị mất khi chuyển sang DTO.
            if (stimulusType == StimulusType.Audio)
            {
                Assert.Equal(EnglishSkill.Listening, detail.PrimarySkill);
            }
            else
            {
                Assert.Equal(EnglishSkill.Reading, detail.PrimarySkill);
            }
        }
    }
    // Không trả dữ liệu khi câu hỏi gắn nhầm Stimulus của Teacher khác.
    [Fact]
    public async Task GetByIdAsync_StimulusOwnedByOtherTeacher_ReturnsNull()
    {
        // Arrange: tạo hai Teacher.
        ApplicationUser owner =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        ApplicationUser otherTeacher =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        Stimulus foreignStimulus = new Stimulus
        {
            OwnerTeacherUserId = otherTeacher.Id,
            StimulusType = StimulusType.Passage,
            Title = "Another teacher's passage",
            ContentText = "This content must not be returned."
        };

        // Cố ý tạo liên kết sai ownership trong database kiểm thử.
        // FK hiện tại kiểm tra Stimulus tồn tại, không bảo đảm cùng owner.
        // Vì vậy cần kiểm tra service không làm lộ dữ liệu này.
        Guid questionId = await CreateQuestionAsync(
            owner.Id,
            foreignStimulus);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IQuestionQueryService service = scope.ServiceProvider
                .GetRequiredService<IQuestionQueryService>();

            // Act: owner xem câu hỏi của mình nhưng Stimulus thuộc người khác.
            QuestionDetail? detail = await service.GetByIdAsync(
                owner.Id,
                questionId);

            // Assert: từ chối trả toàn bộ chi tiết theo cách service đang xử lý.
            Assert.Null(detail);
        }
    }
    // Student và Admin không được đọc chi tiết Question Bank của Teacher.
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task GetByIdAsync_NonTeacher_ReturnsNull(string role)
    {
        // Arrange: câu hỏi thuộc một Teacher hợp lệ.
        ApplicationUser owner =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                AppRoles.Teacher);

        Guid questionId = await CreateQuestionAsync(owner.Id);

        // Người yêu cầu xem là Student hoặc Admin.
        ApplicationUser actor =
            await IdentityTestHelpers.CreateUserAsync(
                _factory,
                role);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IQuestionQueryService service = scope.ServiceProvider
                .GetRequiredService<IQuestionQueryService>();

            // Act: dùng ID câu hỏi thực sự tồn tại.
            QuestionDetail? detail = await service.GetByIdAsync(
                actor.Id,
                questionId);

            // Assert: không trả nội dung hay đáp án.
            Assert.Null(detail);
        }
    }
}
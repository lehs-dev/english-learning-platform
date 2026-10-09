using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class QuestionCreationServiceTests(
    IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private static CreateQuestionRequest ValidRequest() => new()
    {
        Content = "Choose the correct answer.",
        PrimarySkill = EnglishSkill.Grammar,
        Level = "Beginner",
        Difficulty = "Easy",
        Explanation = "Option 2 is correct.",
        Options =
        [
            new QuestionOptionRequest("Option 1", false),
            new QuestionOptionRequest("Option 2", true)
        ]
    };

    [Fact]
    public async Task CreateAsync_ValidRequest_SavesQuestionAndOptions()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            factory,
            AppRoles.Teacher);

        var request = ValidRequest();

        Guid questionId;

        // Thực hiện tạo câu hỏi.
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider
                .GetRequiredService<IQuestionCreationService>();

            var result = await service.CreateAsync(teacher.Id, request);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.QuestionId);

            questionId = result.QuestionId.Value;
        }

        // Dùng context mới để đọc dữ liệu thực sự đã lưu.
        using var verificationScope = factory.Services.CreateScope();

        var db = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var question = await db.Questions
            .AsNoTracking()
            .Include(question => question.Options)
            .SingleAsync(question => question.Id == questionId);

        Assert.Equal(teacher.Id, question.OwnerTeacherUserId);
        Assert.Equal(request.Content, question.Content);
        Assert.Equal(EnglishSkill.Grammar, question.PrimarySkill);
        Assert.Equal(request.Level, question.Level);
        Assert.Equal(request.Difficulty, question.Difficulty);
        Assert.Equal(request.Explanation, question.Explanation);
        Assert.Null(question.StimulusId);
        Assert.Equal(QuestionStatus.Draft, question.Status);

        var options = question.Options
            .OrderBy(option => option.OrderIndex)
            .ToArray();

        Assert.Collection(
            options,
            first =>
            {
                Assert.Equal(questionId, first.QuestionId);
                Assert.Equal(0, first.OrderIndex);
                Assert.Equal("Option 1", first.Content);
                Assert.False(first.IsCorrect);
            },
            second =>
            {
                Assert.Equal(questionId, second.QuestionId);
                Assert.Equal(1, second.OrderIndex);
                Assert.Equal("Option 2", second.Content);
                Assert.True(second.IsCorrect);
            });
    }
    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.Admin)]
    public async Task CreateAsync_NonTeacher_ReturnsForbiddenAndSavesNothing(
        string role)
    {
        var user = await IdentityTestHelpers.CreateUserAsync(factory, role);

        int questionCountBefore;
        int optionCountBefore;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            questionCountBefore = await db.Questions.CountAsync();
            optionCountBefore = await db.QuestionOptions.CountAsync();

            var service = scope.ServiceProvider
                .GetRequiredService<IQuestionCreationService>();

            var result = await service.CreateAsync(user.Id, ValidRequest());

            Assert.False(result.Succeeded);
            Assert.Null(result.QuestionId);

            Assert.Contains(result.Errors, error =>
                error.Field == string.Empty
                && error.Code == "forbidden");
        }

        using var verificationScope = factory.Services.CreateScope();

        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.Equal(
            questionCountBefore,
            await verificationDb.Questions.CountAsync());

        Assert.Equal(
            optionCountBefore,
            await verificationDb.QuestionOptions.CountAsync());
    }
    [Fact]
    public async Task CreateAsync_InvalidContent_ReturnsErrorsAndSavesNothing()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            factory,
            AppRoles.Teacher);

        var request = ValidRequest() with
        {
            Content = "   "
        };

        int questionCountBefore;
        int optionCountBefore;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            questionCountBefore = await db.Questions.CountAsync();
            optionCountBefore = await db.QuestionOptions.CountAsync();

            var service = scope.ServiceProvider
                .GetRequiredService<IQuestionCreationService>();

            var result = await service.CreateAsync(teacher.Id, request);

            Assert.False(result.Succeeded);
            Assert.Null(result.QuestionId);

            Assert.Contains(result.Errors, error =>
                error.Field == nameof(CreateQuestionRequest.Content)
                && error.Code == "required");
        }

        using var verificationScope = factory.Services.CreateScope();

        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.Equal(
            questionCountBefore,
            await verificationDb.Questions.CountAsync());

        Assert.Equal(
            optionCountBefore,
            await verificationDb.QuestionOptions.CountAsync());
    }
    private async Task AssertForbiddenAndSavesNothingAsync(Guid userId)
    {
        int questionCountBefore;
        int optionCountBefore;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            questionCountBefore = await db.Questions.CountAsync();
            optionCountBefore = await db.QuestionOptions.CountAsync();

            var service = scope.ServiceProvider
                .GetRequiredService<IQuestionCreationService>();

            var result = await service.CreateAsync(userId, ValidRequest());

            Assert.False(result.Succeeded);
            Assert.Null(result.QuestionId);

            Assert.Contains(result.Errors, error =>
                error.Field == string.Empty
                && error.Code == "forbidden");
        }

        using var verificationScope = factory.Services.CreateScope();

        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.Equal(
            questionCountBefore,
            await verificationDb.Questions.CountAsync());

        Assert.Equal(
            optionCountBefore,
            await verificationDb.QuestionOptions.CountAsync());
    }
    [Theory]
    [InlineData(AccountStatus.Locked)]
    [InlineData(AccountStatus.Disabled)]
    public async Task CreateAsync_InactiveTeacher_ReturnsForbiddenAndSavesNothing(
        AccountStatus status)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(
            factory,
            AppRoles.Teacher);

        // Lưu trạng thái tài khoản vào database trước khi gọi service.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var storedTeacher = await users.FindByIdAsync(
                teacher.Id.ToString());

            Assert.NotNull(storedTeacher);

            storedTeacher.AccountStatus = status;

            var updateResult = await users.UpdateAsync(storedTeacher);

            Assert.True(updateResult.Succeeded);
        }

        await AssertForbiddenAndSavesNothingAsync(teacher.Id);
    }

    [Fact]
    public async Task CreateAsync_MissingStimulus_ReturnsErrorAndSavesNothing()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var stimulusId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Stimuli.AnyAsync(stimulus => stimulus.Id == stimulusId));
        }

        await AssertInvalidStimulusAndSavesNothingAsync(teacher.Id, stimulusId);
    }

    [Theory]
    [InlineData(StimulusType.Passage)]
    [InlineData(StimulusType.Audio)]
    public async Task CreateAsync_OtherTeachersStimulus_ReturnsErrorAndSavesNothing(
        StimulusType stimulusType)
    {
        var owner = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var stimulusId = await CreateStimulusAsync(owner.Id, stimulusType);

        await AssertInvalidStimulusAndSavesNothingAsync(otherTeacher.Id, stimulusId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stimulus = await db.Stimuli.AsNoTracking().SingleAsync(s => s.Id == stimulusId);

        Assert.Equal(owner.Id, stimulus.OwnerTeacherUserId);
        Assert.Equal(stimulusType, stimulus.StimulusType);
    }

    [Theory]
    [InlineData(StimulusType.Passage, EnglishSkill.Reading)]
    [InlineData(StimulusType.Audio, EnglishSkill.Listening)]
    public async Task CreateAsync_OwnStimulus_SavesQuestionWithStimulus(
        StimulusType stimulusType,
        EnglishSkill skill)
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var stimulusId = await CreateStimulusAsync(teacher.Id, stimulusType);
        var request = ValidRequest() with { StimulusId = stimulusId, PrimarySkill = skill };
        Guid questionId;

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IQuestionCreationService>();
            var result = await service.CreateAsync(teacher.Id, request);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.QuestionId);
            questionId = result.QuestionId.Value;
        }

        using var verificationScope = factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var question = await db.Questions.AsNoTracking()
            .Include(q => q.Stimulus)
            .Include(q => q.Options)
            .SingleAsync(q => q.Id == questionId);

        Assert.Equal(teacher.Id, question.OwnerTeacherUserId);
        Assert.Equal(stimulusId, question.StimulusId);
        Assert.Equal(skill, question.PrimarySkill);
        Assert.Equal(QuestionStatus.Draft, question.Status);
        Assert.NotNull(question.Stimulus);
        Assert.Equal(teacher.Id, question.Stimulus.OwnerTeacherUserId);
        Assert.Equal(stimulusType, question.Stimulus.StimulusType);
        Assert.Equal(2, question.Options.Count);
        Assert.Single(question.Options, option => option.IsCorrect);
    }

    private async Task<Guid> CreateStimulusAsync(Guid ownerId, StimulusType stimulusType)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = ownerId,
            StimulusType = stimulusType,
            Title = "Question creation test stimulus",
            ContentText = stimulusType == StimulusType.Passage ? "A short reading passage." : null,
            ResourceUrl = stimulusType == StimulusType.Audio ? "https://example.test/audio.mp3" : null
        };

        db.Stimuli.Add(stimulus);
        await db.SaveChangesAsync();
        return stimulus.Id;
    }

    private async Task AssertInvalidStimulusAndSavesNothingAsync(Guid teacherId, Guid stimulusId)
    {
        int questionCountBefore;
        int optionCountBefore;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            questionCountBefore = await db.Questions.CountAsync();
            optionCountBefore = await db.QuestionOptions.CountAsync();

            var service = scope.ServiceProvider.GetRequiredService<IQuestionCreationService>();
            var request = ValidRequest() with { StimulusId = stimulusId };
            var result = await service.CreateAsync(teacherId, request);

            Assert.False(result.Succeeded);
            Assert.Null(result.QuestionId);
            var error = Assert.Single(result.Errors);
            Assert.Equal(nameof(CreateQuestionRequest.StimulusId), error.Field);
            Assert.Equal("invalid", error.Code);
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(questionCountBefore, await verificationDb.Questions.CountAsync());
        Assert.Equal(optionCountBefore, await verificationDb.QuestionOptions.CountAsync());
    }
}

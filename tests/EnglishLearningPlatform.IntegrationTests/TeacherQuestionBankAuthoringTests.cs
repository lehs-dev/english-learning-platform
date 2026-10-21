using System.Net;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class TeacherQuestionBankAuthoringTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        Dictionary<string, string> fields,
        string token)
    {
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Teacher_CreateQuestion_ValidData_PersistsAndRedirects()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);

        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");
        var content = "What is the past tense of 'go'? " + Guid.NewGuid().ToString("N");

        var form = new Dictionary<string, string>
        {
            ["Content"] = content,
            ["PrimarySkill"] = ((int)EnglishSkill.Grammar).ToString(),
            ["Level"] = "A2",
            ["Difficulty"] = "Easy",
            ["Explanation"] = "Went is the irregular past tense of go.",
            ["Options[0].Content"] = "Go",
            ["Options[1].Content"] = "Went",
            ["CorrectOptionIndex"] = "1"
        };

        using var response = await PostFormAsync(client, "/TeacherQuestionBank/Create", form, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/TeacherQuestionBank", response.Headers.Location!.ToString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var question = await db.Questions
            .Include(q => q.Options)
            .SingleAsync(q => q.Content == content);

        Assert.Equal(teacher.Id, question.OwnerTeacherUserId);
        Assert.Equal(QuestionStatus.Draft, question.Status);
        Assert.Equal(EnglishSkill.Grammar, question.PrimarySkill);
        Assert.Equal("A2", question.Level);
        Assert.Equal("Easy", question.Difficulty);
        Assert.Equal("Went is the irregular past tense of go.", question.Explanation);
        Assert.Equal(2, question.Options.Count);

        var options = question.Options.OrderBy(o => o.OrderIndex).ToList();
        Assert.Equal("Go", options[0].Content);
        Assert.False(options[0].IsCorrect);
        Assert.Equal("Went", options[1].Content);
        Assert.True(options[1].IsCorrect);
    }

    [Fact]
    public async Task Teacher_CreateQuestion_ValidationFailure_RendersFormWithErrorsAndKeepsInput()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);

        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");
        var content = "Incomplete question " + Guid.NewGuid().ToString("N");

        var form = new Dictionary<string, string>
        {
            ["Content"] = content,
            ["PrimarySkill"] = ((int)EnglishSkill.Grammar).ToString(),
            ["Options[0].Content"] = "Only option 1",
            ["CorrectOptionIndex"] = "0"
        };

        using var response = await PostFormAsync(client, "/TeacherQuestionBank/Create", form, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(content, html);
        Assert.Contains("validation", html);
    }

    [Fact]
    public async Task Teacher_CreateQuestion_WithForeignStimulus_FailsValidation()
    {
        var ownerTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var attackerTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);

        Guid foreignStimulusId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stimulus = new Stimulus
            {
                OwnerTeacherUserId = ownerTeacher.Id,
                StimulusType = StimulusType.Passage,
                Title = "Teacher 1 Reading Passage",
                ContentText = "Passage text..."
            };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync();
            foreignStimulusId = stimulus.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, attackerTeacher);

        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");
        var form = new Dictionary<string, string>
        {
            ["Content"] = "Question referencing foreign stimulus " + Guid.NewGuid().ToString("N"),
            ["PrimarySkill"] = ((int)EnglishSkill.Reading).ToString(),
            ["StimulusId"] = foreignStimulusId.ToString(),
            ["Options[0].Content"] = "Option A",
            ["Options[1].Content"] = "Option B",
            ["CorrectOptionIndex"] = "0"
        };

        using var response = await PostFormAsync(client, "/TeacherQuestionBank/Create", form, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("Stimulus không tồn tại hoặc không thuộc quyền sở hữu của bạn.", decodedHtml);
    }

    [Fact]
    public async Task Teacher_CannotEditQuestion_OwnedByAnotherTeacher()
    {
        var ownerTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);

        Guid questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var q = new Question
            {
                OwnerTeacherUserId = ownerTeacher.Id,
                Content = "Owner's private question",
                PrimarySkill = EnglishSkill.Listening,
                Status = QuestionStatus.Draft,
                Options =
                [
                    new QuestionOption { Content = "A", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "B", IsCorrect = false, OrderIndex = 1 }
                ]
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            questionId = q.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, otherTeacher);

        using var getResponse = await client.GetAsync($"/TeacherQuestionBank/Edit/{questionId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var token = await IdentityTestHelpers.GetTokenAsync(client, "/TeacherQuestionBank/Create");
        var form = new Dictionary<string, string>
        {
            ["Content"] = "Hijacked question",
            ["PrimarySkill"] = ((int)EnglishSkill.Listening).ToString(),
            ["Options[0].Content"] = "Hijacked A",
            ["Options[1].Content"] = "Hijacked B",
            ["CorrectOptionIndex"] = "0"
        };

        using var postResponse = await PostFormAsync(client, $"/TeacherQuestionBank/Edit/{questionId}", form, token);
        Assert.Equal(HttpStatusCode.NotFound, postResponse.StatusCode);
    }

    [Fact]
    public async Task Teacher_CanEditDraftQuestion_WhenNotLocked_UpdatesScoringContentAndMetadata()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);

        Guid questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var q = new Question
            {
                OwnerTeacherUserId = teacher.Id,
                Content = "Original Content " + Guid.NewGuid().ToString("N"),
                PrimarySkill = EnglishSkill.Grammar,
                Level = "A1",
                Difficulty = "Easy",
                Status = QuestionStatus.Draft,
                Options =
                [
                    new QuestionOption { Content = "Opt 1", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "Opt 2", IsCorrect = false, OrderIndex = 1 }
                ]
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            questionId = q.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);

        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/TeacherQuestionBank/Edit/{questionId}");
        var updatedContent = "Updated Content " + Guid.NewGuid().ToString("N");

        var form = new Dictionary<string, string>
        {
            ["Content"] = updatedContent,
            ["PrimarySkill"] = ((int)EnglishSkill.Vocabulary).ToString(),
            ["Level"] = "B2",
            ["Difficulty"] = "Hard",
            ["Explanation"] = "New detailed explanation.",
            ["Options[0].Content"] = "Updated Opt A",
            ["Options[1].Content"] = "Updated Opt B",
            ["CorrectOptionIndex"] = "1"
        };

        using var response = await PostFormAsync(client, $"/TeacherQuestionBank/Edit/{questionId}", form, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/TeacherQuestionBank/Details/{questionId}", response.Headers.Location!.ToString());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedQuestion = await verifyDb.Questions
            .Include(q => q.Options)
            .SingleAsync(q => q.Id == questionId);

        Assert.Equal(updatedContent, updatedQuestion.Content);
        Assert.Equal(EnglishSkill.Vocabulary, updatedQuestion.PrimarySkill);
        Assert.Equal("B2", updatedQuestion.Level);
        Assert.Equal("Hard", updatedQuestion.Difficulty);
        Assert.Equal("New detailed explanation.", updatedQuestion.Explanation);

        var options = updatedQuestion.Options.OrderBy(o => o.OrderIndex).ToList();
        Assert.Equal("Updated Opt A", options[0].Content);
        Assert.False(options[0].IsCorrect);
        Assert.Equal("Updated Opt B", options[1].Content);
        Assert.True(options[1].IsCorrect);
    }

    [Fact]
    public async Task Teacher_EditQuestion_InUnpublishedAssessment_WithoutAttempts_LocksScoringContent_Regression()
    {
        // TBR05: Câu hỏi nằm trong Assessment đã từng Published rồi Unpublished (Status = Unpublished),
        // và chưa có Attempt nào, vẫn phải KHÓA nội dung chấm (Content, PrimarySkill, Options, Stimulus).
        // Level/Difficulty vẫn được sửa; Explanation được giữ nguyên theo T5 để bảo toàn phản hồi cũ.
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);

        Guid questionId;
        var originalContent = "Original Locked Content " + Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id,
                Content = originalContent,
                PrimarySkill = EnglishSkill.Grammar,
                Level = "B1",
                Difficulty = "Medium",
                Explanation = "Original explanation",
                Status = QuestionStatus.Draft,
                Options =
                [
                    new QuestionOption { Content = "Original A", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "Original B", IsCorrect = false, OrderIndex = 1 }
                ]
            };
            db.Questions.Add(question);

            var assessment = new Assessment
            {
                OwnerTeacherUserId = teacher.Id,
                Title = "Assessment Once Published Now Unpublished",
                Status = AssessmentStatus.Unpublished, // Đã từng Publish rồi Unpublish
                AssessmentType = AssessmentType.PlacementTest,
                DurationMinutes = 30,
                MaxAttempts = 3,
                Questions =
                [
                    new AssessmentQuestion
                    {
                        Question = question,
                        OrderIndex = 0,
                        Points = 1m
                    }
                ]
            };
            db.Assessments.Add(assessment);
            await db.SaveChangesAsync();
            questionId = question.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);

        // 1. Kiểm tra GET hiển thị thông báo khóa TBR05
        using var getResponse = await client.GetAsync($"/TeacherQuestionBank/Edit/{questionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("Nội dung chấm đã bị khóa (TBR05)", getHtml);

        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/TeacherQuestionBank/Edit/{questionId}");

        // 2. Thử POST sửa nội dung chấm bị khóa -> Server phải TỪ CHỐI
        var tamperedForm = new Dictionary<string, string>
        {
            ["Content"] = "Tampered Content " + Guid.NewGuid().ToString("N"),
            ["PrimarySkill"] = ((int)EnglishSkill.Vocabulary).ToString(),
            ["Level"] = "C1",
            ["Options[0].Content"] = "Tampered A",
            ["Options[1].Content"] = "Tampered B",
            ["CorrectOptionIndex"] = "0"
        };

        using var tamperedResponse = await PostFormAsync(
            client,
            $"/TeacherQuestionBank/Edit/{questionId}",
            tamperedForm,
            token);

        Assert.Equal(HttpStatusCode.OK, tamperedResponse.StatusCode);
        var tamperedHtml = await tamperedResponse.Content.ReadAsStringAsync();
        Assert.Contains("bị khóa", tamperedHtml);

        // Đảm bảo dữ liệu trong DB không bị thay đổi bởi request trên
        using (var checkScope = factory.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.Questions.SingleAsync(q => q.Id == questionId);
            Assert.Equal(originalContent, unchanged.Content);
        }

        // 3. POST giữ nguyên nội dung chấm và CHỈ cập nhật metadata -> Phải LƯU THÀNH CÔNG
        var metadataForm = new Dictionary<string, string>
        {
            ["Content"] = originalContent,
            ["PrimarySkill"] = ((int)EnglishSkill.Grammar).ToString(),
            ["Level"] = "C1",
            ["Difficulty"] = "Hard",
            ["Explanation"] = "Original explanation",
            ["Options[0].Content"] = "Original A",
            ["Options[1].Content"] = "Original B",
            ["CorrectOptionIndex"] = "0"
        };

        using var successResponse = await PostFormAsync(
            client,
            $"/TeacherQuestionBank/Edit/{questionId}",
            metadataForm,
            token);

        Assert.Equal(HttpStatusCode.Redirect, successResponse.StatusCode);
        Assert.Equal($"/TeacherQuestionBank/Details/{questionId}", successResponse.Headers.Location!.ToString());

        // Kiểm tra DB: Metadata đã cập nhật, nội dung chấm vẫn giữ nguyên
        using (var verifyScope = factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedQuestion = await db.Questions
                .Include(q => q.Options)
                .SingleAsync(q => q.Id == questionId);

            Assert.Equal(originalContent, savedQuestion.Content);
            Assert.Equal(EnglishSkill.Grammar, savedQuestion.PrimarySkill);
            Assert.Equal("C1", savedQuestion.Level);
            Assert.Equal("Hard", savedQuestion.Difficulty);
            Assert.Equal("Original explanation", savedQuestion.Explanation);

            var options = savedQuestion.Options.OrderBy(o => o.OrderIndex).ToList();
            Assert.Equal("Original A", options[0].Content);
            Assert.True(options[0].IsCorrect);
            Assert.Equal("Original B", options[1].Content);
            Assert.False(options[1].IsCorrect);
        }
    }

    [Fact]
    public async Task Teacher_EditQuestion_InDraftAssessment_DoesNotLockScoringContent()
    {
        // Khi câu hỏi chỉ nằm trong Assessment ở trạng thái Draft và chưa có attempt nào,
        // KHÔNG mặc định bị khóa nội dung chấm.
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);

        Guid questionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var question = new Question
            {
                OwnerTeacherUserId = teacher.Id,
                Content = "Question in Draft Assessment",
                PrimarySkill = EnglishSkill.Reading,
                Status = QuestionStatus.Draft,
                Options =
                [
                    new QuestionOption { Content = "D1", IsCorrect = true, OrderIndex = 0 },
                    new QuestionOption { Content = "D2", IsCorrect = false, OrderIndex = 1 }
                ]
            };
            db.Questions.Add(question);

            var assessment = new Assessment
            {
                OwnerTeacherUserId = teacher.Id,
                Title = "Draft Assessment",
                Status = AssessmentStatus.Draft,
                AssessmentType = AssessmentType.PlacementTest,
                DurationMinutes = 15,
                MaxAttempts = 1,
                Questions =
                [
                    new AssessmentQuestion
                    {
                        Question = question,
                        OrderIndex = 0,
                        Points = 1m
                    }
                ]
            };
            db.Assessments.Add(assessment);
            await db.SaveChangesAsync();
            questionId = question.Id;
        }

        using var client = IdentityTestHelpers.CreateClient(factory);
        await IdentityTestHelpers.LoginAsync(client, teacher);

        using var getResponse = await client.GetAsync($"/TeacherQuestionBank/Edit/{questionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Nội dung chấm đã bị khóa", getHtml);

        var token = await IdentityTestHelpers.GetTokenAsync(client, $"/TeacherQuestionBank/Edit/{questionId}");
        var updatedContent = "Updated content in draft assessment " + Guid.NewGuid().ToString("N");

        var form = new Dictionary<string, string>
        {
            ["Content"] = updatedContent,
            ["PrimarySkill"] = ((int)EnglishSkill.Reading).ToString(),
            ["Options[0].Content"] = "D1 updated",
            ["Options[1].Content"] = "D2 updated",
            ["CorrectOptionIndex"] = "1"
        };

        using var postResponse = await PostFormAsync(client, $"/TeacherQuestionBank/Edit/{questionId}", form, token);
        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var dbVerify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await dbVerify.Questions.SingleAsync(q => q.Id == questionId);
        Assert.Equal(updatedContent, updated.Content);
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

// Không dùng namespace .Assessment vì sẽ che tên entity Assessment ở các test cũ.
namespace EnglishLearningPlatform.UnitTests.AssessmentAuthoring;

public sealed class AssessmentDraftRequestTests
{
    [Theory]
    [InlineData(AssessmentType.PlacementTest, null)]
    [InlineData(AssessmentType.SkillAssessment, null)]
    [InlineData(AssessmentType.SkillAssessment, "0")]
    [InlineData(AssessmentType.SkillAssessment, "100")]
    [InlineData(AssessmentType.PracticeExam, null)]
    [InlineData(AssessmentType.PracticeExam, "72.25")]
    public void Validate_ValidConfiguration_ReturnsNoErrors(AssessmentType type, string? passingScore)
    {
        var request = ValidRequest();
        request.AssessmentType = type;
        request.TargetSkill = type == AssessmentType.SkillAssessment ? EnglishSkill.Reading : null;
        request.PassingScore = passingScore == null
            ? null : decimal.Parse(passingScore, CultureInfo.InvariantCulture);

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_TextAtDatabaseLimits_ReturnsNoErrors()
    {
        var request = ValidRequest();
        request.Title = new string('a', 200);
        request.Description = new string('b', 4000);

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("BlankTitle", nameof(AssessmentDraftRequest.Title))]
    [InlineData("LongTitle", nameof(AssessmentDraftRequest.Title))]
    [InlineData("LongDescription", nameof(AssessmentDraftRequest.Description))]
    [InlineData("MissingType", nameof(AssessmentDraftRequest.AssessmentType))]
    [InlineData("UnknownType", nameof(AssessmentDraftRequest.AssessmentType))]
    [InlineData("UnknownSkill", nameof(AssessmentDraftRequest.TargetSkill))]
    [InlineData("DurationZero", nameof(AssessmentDraftRequest.DurationMinutes))]
    [InlineData("AttemptsZero", nameof(AssessmentDraftRequest.MaxAttempts))]
    [InlineData("NegativeScore", nameof(AssessmentDraftRequest.PassingScore))]
    [InlineData("ScoreOver100", nameof(AssessmentDraftRequest.PassingScore))]
    [InlineData("ScorePrecision", nameof(AssessmentDraftRequest.PassingScore))]
    [InlineData("PlacementScore", nameof(AssessmentDraftRequest.PassingScore))]
    [InlineData("MissingTarget", nameof(AssessmentDraftRequest.TargetSkill))]
    [InlineData("UnneededTarget", nameof(AssessmentDraftRequest.TargetSkill))]
    public void Validate_InvalidConfiguration_ReturnsErrorForCorrectField(string fault, string field)
    {
        var request = ValidRequest();

        switch (fault)
        {
            case "BlankTitle": request.Title = "   "; break;
            case "LongTitle": request.Title = new string('a', 201); break;
            case "LongDescription": request.Description = new string('b', 4001); break;
            case "MissingType": request.AssessmentType = null; break;
            case "UnknownType": request.AssessmentType = (AssessmentType)999; break;
            case "UnknownSkill":
                request.AssessmentType = AssessmentType.SkillAssessment;
                request.TargetSkill = (EnglishSkill)999;
                break;
            case "DurationZero": request.DurationMinutes = 0; break;
            case "AttemptsZero": request.MaxAttempts = 0; break;
            case "NegativeScore": request.PassingScore = -0.01m; break;
            case "ScoreOver100": request.PassingScore = 100.01m; break;
            case "ScorePrecision": request.PassingScore = 72.251m; break;
            case "PlacementScore":
                request.AssessmentType = AssessmentType.PlacementTest;
                request.PassingScore = 50m;
                break;
            case "MissingTarget": request.AssessmentType = AssessmentType.SkillAssessment; break;
            case "UnneededTarget": request.TargetSkill = EnglishSkill.Grammar; break;
            default: throw new ArgumentException("Unknown test case.", nameof(fault));
        }

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(field));
    }

    // Gọi đúng DataAnnotations như MVC và service, không tự viết lại validator.
    private static List<ValidationResult> Validate(AssessmentDraftRequest request)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
        return errors;
    }

    private static AssessmentDraftRequest ValidRequest()
    {
        return new AssessmentDraftRequest
        {
            Title = "Grammar assessment",
            AssessmentType = AssessmentType.PracticeExam,
            DurationMinutes = 15,
            MaxAttempts = 1
        };
    }
}

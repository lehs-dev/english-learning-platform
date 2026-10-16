using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.Stimuli;

public sealed class CreateStimulusValidatorTests
{
    private readonly CreateStimulusValidator _validator = new();

    // Hai loại hợp lệ, bao gồm tiêu đề ở đúng giới hạn cho phép.
    [Theory]
    [InlineData(StimulusType.Passage, "Reading passage", null)]
    [InlineData(StimulusType.Audio, null, "https://example.test/audio.mp3")]
    public void Validate_ValidTypeAndContent_HasNoErrors(StimulusType type, string? text, string? url)
    {
        var request = new CreateStimulusRequest { StimulusType = type,
            Title = new string('a', 200), ContentText = text, ResourceUrl = url };

        Assert.Empty(_validator.Validate(request));
    }

    // Không nhận loại bị bỏ trống hoặc số không có trong enum.
    [Theory]
    [InlineData(null)]
    [InlineData((StimulusType)0)]
    [InlineData((StimulusType)999)]
    public void Validate_MissingOrUnknownType_ReturnsTypeError(StimulusType? type)
    {
        var errors = _validator.Validate(new CreateStimulusRequest { StimulusType = type });

        Assert.Contains(nameof(CreateStimulusRequest.StimulusType), errors.Keys);
    }

    // Passage không được chỉ chứa khoảng trắng.
    [Fact]
    public void Validate_BlankPassage_ReturnsContentError()
    {
        var errors = _validator.Validate(new CreateStimulusRequest {
            StimulusType = StimulusType.Passage, ContentText = "   " });

        Assert.Contains(nameof(CreateStimulusRequest.ContentText), errors.Keys);
    }

    // Không nhận URL rỗng, script hoặc đường dẫn tương đối thay cho HTTP/HTTPS.
    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/audio.mp3")]
    public void Validate_InvalidAudioUrl_ReturnsUrlError(string url)
    {
        var errors = _validator.Validate(new CreateStimulusRequest {
            StimulusType = StimulusType.Audio, ResourceUrl = url });

        Assert.Contains(nameof(CreateStimulusRequest.ResourceUrl), errors.Keys);
    }

    // Ngăn lưu dữ liệu vượt giới hạn cột thay vì đợi SQL báo lỗi.
    [Fact]
    public void Validate_OverDatabaseLengths_ReturnsFieldErrors()
    {
        var errors = _validator.Validate(new CreateStimulusRequest {
            StimulusType = StimulusType.Audio, Title = new string('a', 201),
            ResourceUrl = "https://example.test/" + new string('a', 2048) });

        Assert.Contains(nameof(CreateStimulusRequest.Title), errors.Keys);
        Assert.Contains(nameof(CreateStimulusRequest.ResourceUrl), errors.Keys);
    }

    // Không bỏ âm thầm dữ liệu ở trường không thuộc loại đã chọn.
    [Theory]
    [InlineData(StimulusType.Passage, nameof(CreateStimulusRequest.ResourceUrl))]
    [InlineData(StimulusType.Audio, nameof(CreateStimulusRequest.ContentText))]
    public void Validate_DataForBothTypes_RejectsUnusedField(StimulusType type, string field)
    {
        var errors = _validator.Validate(new CreateStimulusRequest {
            StimulusType = type, ContentText = "Passage", ResourceUrl = "https://example.test/audio.mp3" });

        Assert.Single(errors);
        Assert.Contains(field, errors.Keys);
    }
}

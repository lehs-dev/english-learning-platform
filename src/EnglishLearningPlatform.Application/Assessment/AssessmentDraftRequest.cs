using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Domain.Enums;

// Đặt tên khác để tránh trùng tên với property AssessmentType.
using AssessmentKind = EnglishLearningPlatform.Domain.Enums.AssessmentType;

namespace EnglishLearningPlatform.Application.Assessment;

/// <summary>
/// Dữ liệu Teacher được nhập khi tạo hoặc sửa Assessment Draft.
/// Không phải entity EF và không tạo bảng mới.
/// </summary>
public sealed class AssessmentDraftRequest : IValidatableObject
{
    [Required(ErrorMessage = "Bạn phải nhập tên bài kiểm tra.")]
    [StringLength(
        200,
        ErrorMessage = "Tên bài kiểm tra không được vượt quá 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(
        4000,
        ErrorMessage = "Mô tả không được vượt quá 4000 ký tự.")]
    public string? Description { get; set; }

    // Nullable để phân biệt chưa chọn với một loại bài hợp lệ.
    [Required(ErrorMessage = "Bạn phải chọn loại bài kiểm tra.")]
    [EnumDataType(
        typeof(AssessmentKind),
        ErrorMessage = "Loại bài kiểm tra không hợp lệ.")]
    public AssessmentKind? AssessmentType { get; set; }

    [EnumDataType(
        typeof(EnglishSkill),
        ErrorMessage = "Kỹ năng không hợp lệ.")]
    public EnglishSkill? TargetSkill { get; set; }

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Thời lượng phải là số phút nguyên lớn hơn 0.")]
    public int DurationMinutes { get; set; } = 15;

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "Số lượt làm tối đa phải từ 1 trở lên.")]
    public int MaxAttempts { get; set; } = 1;

    [Range(
        typeof(decimal),
        "0",
        "100",
        ErrorMessage = "Điểm đạt phải nằm trong khoảng 0 đến 100.")]
    public decimal? PassingScore { get; set; }

    // Phiên bản dữ liệu khi mở form sửa.
    // Dùng để chặn tab cũ ghi đè thay đổi đã lưu ở tab khác.
    public DateTimeOffset? ExpectedUpdatedAtUtc { get; set; }

    /// <summary>
    /// Kiểm tra các quy tắc liên quan đến nhiều trường.
    /// MVC và service đều sử dụng được các quy tắc này.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (AssessmentType == AssessmentKind.PlacementTest
            && PassingScore.HasValue)
        {
            yield return new ValidationResult(
                "Placement Test không sử dụng điểm đạt.",
                new string[] { nameof(PassingScore) });
        }

        if (AssessmentType == AssessmentKind.SkillAssessment
            && !TargetSkill.HasValue)
        {
            yield return new ValidationResult(
                "Skill Assessment phải chọn một kỹ năng.",
                new string[] { nameof(TargetSkill) });
        }

        // TargetSkill chỉ có ý nghĩa đối với Skill Assessment.
        // Hai loại còn lại được phép kết hợp nhiều kỹ năng.
        if (AssessmentType.HasValue
            && AssessmentType != AssessmentKind.SkillAssessment
            && TargetSkill.HasValue)
        {
            yield return new ValidationResult(
                "Chỉ Skill Assessment sử dụng kỹ năng mục tiêu.",
                new string[] { nameof(TargetSkill) });
        }

        // Database đang lưu PassingScore bằng decimal(5, 2).
        // Từ chối thay vì để database tự làm tròn dữ liệu Teacher nhập.
        if (PassingScore.HasValue
            && decimal.Round(PassingScore.Value, 2)
                != PassingScore.Value)
        {
            yield return new ValidationResult(
                "Điểm đạt chỉ được có tối đa hai chữ số thập phân.",
                new string[] { nameof(PassingScore) });
        }
    }
}
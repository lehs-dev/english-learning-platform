using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.Homework;

namespace EnglishLearningPlatform.Web.Models;

public sealed class WritingAssignmentInput
{
    [Required(ErrorMessage = "Nhập tên bài viết.")]
    [StringLength(WritingHomeworkLimits.TitleLength, ErrorMessage = "Tên bài viết tối đa {1} ký tự.")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Nhập đề bài.")]
    [StringLength(WritingHomeworkLimits.PromptLength, ErrorMessage = "Đề bài tối đa {1} ký tự.")]
    public string? Prompt { get; set; }

    public DateTime? DueAt { get; set; }
}

public sealed class WritingReviewInput
{
    [Required(ErrorMessage = "Nhập nhận xét.")]
    [StringLength(WritingHomeworkLimits.FeedbackLength, ErrorMessage = "Nhận xét tối đa {1} ký tự.")]
    public string? Feedback { get; set; }

    [Range(0, 100, ErrorMessage = "Điểm từ 0 đến 100.")]
    public int? Score { get; set; }
}

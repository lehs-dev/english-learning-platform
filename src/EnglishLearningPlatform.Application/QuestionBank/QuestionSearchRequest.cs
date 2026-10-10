using EnglishLearningPlatform.Domain.Enums;

namespace EnglishLearningPlatform.Application.QuestionBank;
public class QuestionSearchRequest
{
    public string? Search {get; set;}
    public EnglishSkill? PrimarySkill {get; set;}
    public string? Level {get; set;}
    public string? Difficulty {get; set;}
    public QuestionStatus? Status {get; set;}
    // Trang Muon Xem
    public int PageNumber {get; set;} = 1;
    // So Luong Cac Cau Hoi Muon Xem
    public int PageSize{get; set;} = 10;

}
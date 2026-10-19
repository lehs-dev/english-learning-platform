using EnglishLearningPlatform.Application.Assessment;
using EnglishLearningPlatform.Application.QuestionBank;

namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed class TeacherAssessmentQuestionsViewModel
{
    public TeacherAssessmentQuestionEditor Editor { get; set; }
        = new TeacherAssessmentQuestionEditor();

    public QuestionSearchRequest Query { get; set; }
        = new QuestionSearchRequest();

    public QuestionSearchResult Result { get; set; }
        = new QuestionSearchResult(
            new List<QuestionListItem>(), 0, 1, 10);
}
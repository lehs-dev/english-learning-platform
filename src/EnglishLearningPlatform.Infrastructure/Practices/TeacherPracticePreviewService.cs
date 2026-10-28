using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Practices;

// Tách khỏi luồng Student: Preview không có dependency vào service làm/chấm bài.
public sealed class TeacherPracticePreviewService(
    AppDbContext db, IQuestionUsageValidator questionValidator) : ITeacherPracticePreviewService
{
    public async Task<LearningOutcome<TeacherPracticePreview>> GetAsync(
        Guid actorUserId, Guid practiceId, CancellationToken cancellationToken = default)
    {
        // Kiểm tra tài khoản hiện tại trong DB, không chỉ tin role trong cookie.
        bool teacher = await db.HasActiveRoleAsync(actorUserId, AppRoles.Teacher, cancellationToken);
        if (!teacher) return new(LearningAccessResult.Forbidden);

        // Chỉ tải đề của owner, không Include Submissions/Answers hoặc gọi SaveChanges.
        var practice = await db.Practices.AsNoTracking()
            .Where(item => item.Id == practiceId && item.OwnerTeacherUserId == actorUserId)
            .Include(item => item.Lesson).ThenInclude(item => item!.Module).ThenInclude(item => item.Course)
            .Include(item => item.Module).ThenInclude(item => item!.Course)
            .Include(item => item.Questions).ThenInclude(item => item.Question).ThenInclude(item => item.Options)
            .Include(item => item.Questions).ThenInclude(item => item.Question).ThenInclude(item => item.Stimulus)
            .AsSingleQuery().SingleOrDefaultAsync(cancellationToken);
        if (practice == null) return new(LearningAccessResult.NotFound);
        if (practice.Status != PracticeStatus.Draft && practice.Status != PracticeStatus.Published)
            return new(LearningAccessResult.Forbidden);
        // Preview chỉ xem phiên bản đang công bố, không trộn các đề lịch sử.
        var currentQuestions = practice.Questions.Where(q => q.Version == practice.Version).ToList();

        // Liên kết legacy sai owner cũng không được làm lộ Course/Question/Stimulus người khác.
        if ((practice.Lesson != null && practice.Lesson.Module.Course.OwnerTeacherUserId != actorUserId) ||
            (practice.Module != null && practice.Module.Course.OwnerTeacherUserId != actorUserId) ||
            currentQuestions.Any(item => item.Question.OwnerTeacherUserId != actorUserId ||
                (item.Question.Stimulus != null && item.Question.Stimulus.OwnerTeacherUserId != actorUserId)))
            return new(LearningAccessResult.NotFound);

        var warnings = new List<string>();
        if (practice.LessonId.HasValue == practice.ModuleId.HasValue)
            warnings.Add("Cần chọn đúng một Lesson hoặc một Module trước khi Publish.");
        if (currentQuestions.Count == 0)
            warnings.Add("Practice chưa có câu hỏi. Hãy lưu danh sách câu hỏi trước khi Publish.");
        var questions = new List<PracticeQuestionSheet>();
        foreach (var item in currentQuestions.OrderBy(item => item.OrderIndex).ThenBy(item => item.Id))
        {
            var question = item.Question;
            var options = new List<PracticeOption>();
            foreach (var option in question.Options.OrderBy(option => option.OrderIndex).ThenBy(option => option.Id))
                options.Add(new PracticeOption(option.Id, option.Content));

            string? audioUrl = null;
            var stimulus = question.Stimulus;
            // Không đưa file:/javascript: hoặc URL tùy ý vào player.
            if (stimulus != null && stimulus.StimulusType == StimulusType.Audio &&
                (StimulusAudioUrl.TryGetHash(stimulus.ResourceUrl, out _) ||
                 CourseRules.ValidResource(LessonResourceType.Audio, null, stimulus.ResourceUrl)))
                audioUrl = stimulus.ResourceUrl;
            questions.Add(new PracticeQuestionSheet(item.Id, question.Content, question.PrimarySkill,
                stimulus?.StimulusType == StimulusType.Passage ? stimulus.ContentText : null, audioUrl, options));

            // Draft thiếu nội dung vẫn Preview được; cảnh báo không thay thế validator Publish.
            foreach (var error in questionValidator.ValidateForExistingUse(actorUserId, question))
                warnings.Add($"Câu {questions.Count}: {error.Message}");
        }

        string parent = "Chưa chọn Lesson hoặc Module";
        if (practice.Lesson != null)
            parent = practice.Lesson.Module.Course.Title + " / " + practice.Lesson.Module.Title + " / " + practice.Lesson.Title;
        else if (practice.Module != null)
            parent = practice.Module.Course.Title + " / " + practice.Module.Title;

        return new(LearningAccessResult.Allowed, new TeacherPracticePreview
        {
            PracticeId = practice.Id, Title = practice.Title, Description = practice.Description,
            Status = practice.Status, ParentTitle = parent, Questions = questions, Warnings = warnings
        });
    }
}

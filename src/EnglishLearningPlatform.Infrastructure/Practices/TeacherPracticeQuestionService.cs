using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Infrastructure.Stimuli;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.Practices;

/// <summary>
/// Soạn câu hỏi và xuất bản Practice của Teacher.
/// Không sửa nội dung Question hoặc kết quả làm bài.
/// </summary>
public sealed class TeacherPracticeQuestionService
    : ITeacherPracticeQuestionService
{
    private readonly AppDbContext _db;
    private readonly IQuestionUsageValidator _questionUsageValidator;
    private readonly ILogger<TeacherPracticeQuestionService> _logger;
    private readonly IStimulusAudioStore _audioStore;

    public TeacherPracticeQuestionService(
        AppDbContext db,
        IQuestionUsageValidator questionUsageValidator,
        ILogger<TeacherPracticeQuestionService> logger, IStimulusAudioStore audioStore)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (questionUsageValidator == null)
        {
            throw new ArgumentNullException(
                nameof(questionUsageValidator));
        }

        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        _db = db;
        _questionUsageValidator = questionUsageValidator;
        _logger = logger;
        _audioStore = audioStore;
    }

    /// <summary>
    /// Đọc dữ liệu cho màn hình soạn câu hỏi của Teacher.
    /// Chỉ đọc database, không thay đổi Practice hoặc Question.
    /// </summary>
    public async Task<LearningOutcome<TeacherPracticeQuestionEditor>>
        GetEditorAsync(
            Guid actorUserId,
            Guid practiceId,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Dùng lại helper đã có trong service.
        // Kiểm tra tài khoản hiện tại từ database, không chỉ dựa vào cookie.
        bool isTeacher = await IsActiveTeacherAsync(
            actorUserId,
            cancellationToken);

        if (!isTeacher)
        {
            return new LearningOutcome<TeacherPracticeQuestionEditor>(
                LearningAccessResult.Forbidden);
        }

        if (practiceId == Guid.Empty)
        {
            return new LearningOutcome<TeacherPracticeQuestionEditor>(
                LearningAccessResult.NotFound);
        }

        // Chỉ tìm Practice thuộc Teacher hiện tại.
        // AsNoTracking phù hợp vì đây là thao tác chỉ đọc.
        var query = _db.Practices
            .AsNoTracking()
            .Where(practice =>
                practice.Id == practiceId
                && practice.OwnerTeacherUserId == actorUserId);

        // Giữ cùng nguyên tắc với trang danh sách Practice:
        // Draft chưa có parent vẫn được mở.
        // Nếu có parent, Course chứa parent phải thuộc cùng Teacher.
        query = query.Where(practice =>
            (!practice.LessonId.HasValue
                && !practice.ModuleId.HasValue)
            || (practice.Lesson != null
                && practice.Lesson.Module.Course.OwnerTeacherUserId
                    == actorUserId)
            || (practice.Module != null
                && practice.Module.Course.OwnerTeacherUserId
                    == actorUserId));

        // Phòng trường hợp dữ liệu đã có liên kết sai owner.
        // Không trả một Practice chứa nội dung Question của Teacher khác.
        query = query.Where(practice =>
            !practice.Questions.Any(item =>
                item.Question.OwnerTeacherUserId != actorUserId));

        // Select chỉ lấy các trường cần cho màn hình soạn.
        // Không trả entity EF trực tiếp cho Controller.
        var editor = await query
            .Select(practice => new TeacherPracticeQuestionEditor
            {
                PracticeId = practice.Id,
                Title = practice.Title,
                Status = practice.Status,
                Version = practice.Version,
                UpdatedAtUtc = practice.UpdatedAtUtc,
                LessonId = practice.LessonId,
                ModuleId = practice.ModuleId,

                ParentTitle = practice.Lesson != null
                    ? practice.Lesson.Module.Course.Title
                        + " / " + practice.Lesson.Module.Title
                        + " / " + practice.Lesson.Title
                    : practice.Module != null
                        ? practice.Module.Course.Title
                            + " / " + practice.Module.Title
                        : "Chưa chọn Lesson hoặc Module",

                // Sửa bài đã xuất bản tạo phiên bản mới, không ghi đè đề cũ.
                CanEditQuestions =
                    (practice.Status == PracticeStatus.Draft || practice.Status == PracticeStatus.Published
                        || practice.Status == PracticeStatus.Unpublished)
                    && (practice.Status != PracticeStatus.Draft || !practice.Submissions.Any())
                    && (practice.Lesson == null || practice.Lesson.Module.Course.Status != CourseStatus.Archived)
                    && (practice.Module == null || practice.Module.Course.Status != CourseStatus.Archived),

                Questions = practice.Questions
                    .Where(item => item.Version == practice.Version)
                    // Lọc owner cả ở danh sách con trước khi lấy nội dung.
                    .Where(item =>
                        item.Question.OwnerTeacherUserId == actorUserId)
                    .OrderBy(item => item.OrderIndex)
                    .ThenBy(item => item.Id)
                    .Select(item => new QuestionListItem
                    {
                        // Cần QuestionId để gửi lại đúng request lưu.
                        // Không dùng item.Id vì đó là PracticeQuestionId.
                        Id = item.QuestionId,

                        Content = item.Question.Content,
                        PrimarySkill = item.Question.PrimarySkill,
                        Level = item.Question.Level,
                        Status = item.Question.Status,
                        OptionCount = item.Question.Options.Count,
                        CreatedAtUtc = item.Question.CreatedAtUtc
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (editor == null)
        {
            // Không tiết lộ Practice không tồn tại hay thuộc người khác.
            return new LearningOutcome<TeacherPracticeQuestionEditor>(
                LearningAccessResult.NotFound);
        }

        // Chỉ cần tải lựa chọn parent khi Practice còn được chỉnh sửa.
        if (editor.CanEditQuestions)
        {
            editor.ModuleOptions = await _db.Modules
                .AsNoTracking()
                .Where(module =>
                    module.Course.OwnerTeacherUserId == actorUserId
                    && module.Course.Status != CourseStatus.Archived)
                .OrderBy(module => module.Course.Title)
                .ThenBy(module => module.OrderIndex)
                .ThenBy(module => module.Id)
                .Select(module => new PracticeParentOption
                {
                    Id = module.Id,
                    Title = module.Course.Title + " / " + module.Title
                })
                .ToListAsync(cancellationToken);

            editor.LessonOptions = await _db.Lessons
                .AsNoTracking()
                .Where(lesson =>
                    lesson.Module.Course.OwnerTeacherUserId == actorUserId
                    && lesson.Module.Course.Status != CourseStatus.Archived)
                .OrderBy(lesson => lesson.Module.Course.Title)
                .ThenBy(lesson => lesson.Module.OrderIndex)
                .ThenBy(lesson => lesson.OrderIndex)
                .ThenBy(lesson => lesson.Id)
                .Select(lesson => new PracticeParentOption
                {
                    Id = lesson.Id,
                    Title = lesson.Module.Course.Title
                        + " / " + lesson.Module.Title
                        + " / " + lesson.Title
                })
                .ToListAsync(cancellationToken);
        }

        return new LearningOutcome<TeacherPracticeQuestionEditor>(
            LearningAccessResult.Allowed,
            editor);
    }

    /// <summary>
    /// Kiểm tra quyền và dữ liệu trước khi thay danh sách câu hỏi.
    /// Mọi thay đổi database nằm trong cùng một transaction.
    /// </summary>
    public async Task<WriteOutcome> UpdateQuestionsAsync(
        Guid actorUserId,
        Guid practiceId,
        UpdatePracticeQuestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Kiểm tra tài khoản trước khi đọc Practice.
            if (!await IsActiveTeacherAsync(
                actorUserId,
                cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            if (practiceId == Guid.Empty)
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            var snapshot = await _db.Practices.AsNoTracking()
                .Where(p => p.Id == practiceId && p.OwnerTeacherUserId == actorUserId)
                .Select(p => new { p.LessonId, p.ModuleId }).SingleOrDefaultAsync(cancellationToken);
            if (snapshot == null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
            Guid? courseId = await FindParentCourseIdAsync(actorUserId, snapshot.LessonId, snapshot.ModuleId, cancellationToken);
            if ((snapshot.LessonId.HasValue || snapshot.ModuleId.HasValue) && !courseId.HasValue)
                return Invalid(practiceId, "Vị trí bài luyện tập không hợp lệ hoặc không thuộc giảng viên hiện tại.");

            // Nếu chưa Commit mà thoát hàm hoặc xảy ra lỗi,
            // transaction sẽ rollback các thay đổi.
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            // Cùng thứ tự khóa với Student mở/nộp bài, tránh sửa chen giữa một lần chấm.
            if (courseId.HasValue)
            {
                var course = await _db.CoursesForUpdate(courseId.Value).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (course == null || course.OwnerTeacherUserId != actorUserId || course.Status == CourseStatus.Archived)
                    return Invalid(practiceId, "Không thể sửa bài luyện tập trong khóa học đã lưu trữ hoặc không thuộc bạn.");
            }

            // Kiểm tra lại quyền trong transaction.
            // Không chỉ dựa vào kết quả đọc trước transaction.
            if (!await IsActiveTeacherAsync(
                actorUserId,
                cancellationToken))
            {
                return WriteOutcome.Denied(
                    LearningAccessResult.Forbidden);
            }

            // Serialize updates to this Practice until the transaction completes.
            // The persistence query passes both IDs as SQL parameters.
            var practice = await _db.PracticesForUpdate(practiceId, actorUserId)
                .SingleOrDefaultAsync(cancellationToken);

            if (practice == null)
            {
                // Không phân biệt không tồn tại hay thuộc Teacher khác.
                return WriteOutcome.Denied(
                    LearningAccessResult.NotFound);
            }

            // Đọc lại trạng thái từ database nếu entity đã được
            // DbContext theo dõi từ một thao tác trước trong request.
            await _db.Entry(practice).ReloadAsync(cancellationToken);

            // Kiểm tra trong transaction, sau khi đã giữ khóa Practice.
            if (practice.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            {
                return Invalid(
                    practiceId,
                    "Practice đã được cập nhật ở nơi khác. "
                    + "Hãy tải lại trang trước khi chỉnh sửa.");
            }

            if (practice.LessonId != snapshot.LessonId || practice.ModuleId != snapshot.ModuleId)
                return Invalid(practiceId, "Vị trí bài luyện tập đã thay đổi. Hãy tải lại trang.");

            if (!Enum.IsDefined(practice.Status))
            {
                return Invalid(
                    practiceId,
                    "Trạng thái bài luyện tập không hợp lệ.");
            }

            // Bảo vệ dữ liệu lịch sử nếu Practice đã có kết quả.
            bool hasSubmission = await _db.PracticeSubmissions
                .AnyAsync(
                    submission => submission.PracticeId == practiceId,
                    cancellationToken);

            if (practice.Status == PracticeStatus.Draft && hasSubmission)
            {
                return Invalid(
                    practiceId,
                    "Practice đã có kết quả làm bài nên không được đổi câu hỏi.");
            }

            // Không coi null là danh sách rỗng để tránh vô tình
            // xóa hết lựa chọn khi client gửi dữ liệu không hợp lệ.
            if (request.QuestionIds == null)
            {
                return Invalid(
                    practiceId,
                    "Danh sách câu hỏi không hợp lệ.");
            }

            // Chụp lại danh sách đầu vào để dùng nhất quán
            // trong toàn bộ thao tác đang thực hiện.
            Guid[] questionIds = request.QuestionIds.ToArray();
            if (practice.Status != PracticeStatus.Draft && questionIds.Length == 0)
                return Invalid(practiceId, "Bài đã xuất bản phải giữ ít nhất một câu hỏi hợp lệ.");

            // HashSet lưu các ID đã gặp.
            // Add trả false nếu một ID đã xuất hiện trước đó.
            var seenIds = new HashSet<Guid>();

            foreach (Guid questionId in questionIds)
            {
                if (questionId == Guid.Empty)
                {
                    return Invalid(
                        practiceId,
                        "Danh sách có ID câu hỏi rỗng.");
                }

                if (!seenIds.Add(questionId))
                {
                    return Invalid(
                        practiceId,
                        "Không được chọn trùng một câu hỏi.");
                }
            }

            // Chỉ đọc câu hỏi của Teacher hiện tại.
            // Tải cả Options và Stimulus theo yêu cầu của validator.
            // Theo dõi entity để khóa nội dung câu hỏi mới khi lưu vào bài đã xuất bản.
            var currentQuestionIds = await _db.PracticeQuestions
                .Where(item => item.PracticeId == practiceId && item.Version == practice.Version)
                .Select(item => item.QuestionId).ToListAsync(cancellationToken);
            var questions = await _db.Questions
                .Where(question =>
                    question.OwnerTeacherUserId == actorUserId
                    && questionIds.Contains(question.Id))
                .Include(question => question.Options)
                .Include(question => question.Stimulus)
                .ToListAsync(cancellationToken);

            // Database không đảm bảo trả đúng thứ tự của request.
            // Dictionary giúp tìm Question bằng ID khi duyệt request.
            var questionsById = questions.ToDictionary(
                question => question.Id);

            var messages = new List<string>();

            for (int index = 0; index < questionIds.Length; index++)
            {
                // Không tìm thấy hoặc thuộc Teacher khác:
                // question sẽ là null, validator trả lỗi chung.
                questionsById.TryGetValue(
                    questionIds[index],
                    out Question? question);

                // Đây là luồng soạn nội dung mới:
                // dùng ValidateForNewUse để chặn Question Archived.
                // Câu Archived đã có được giữ; không được thêm mới một câu Archived.
                var questionErrors = practice.Status != PracticeStatus.Draft && currentQuestionIds.Contains(questionIds[index])
                    ? _questionUsageValidator.ValidateForExistingUse(actorUserId, question)
                    : _questionUsageValidator.ValidateForNewUse(actorUserId, question);

                foreach (var error in questionErrors)
                {
                    messages.Add(
                        $"Câu {index + 1}: {error.Message}");
                }
            }

            if (messages.Count > 0)
            {
                return Invalid(
                    practiceId,
                    messages.ToArray());
            }

            if (practice.Status != PracticeStatus.Draft)
            {
                foreach (var question in questions)
                {
                    var stimulus = question.Stimulus;
                    if (stimulus?.StimulusType == StimulusType.Passage && string.IsNullOrWhiteSpace(stimulus.ContentText))
                        return Invalid(practiceId, "Bài đọc đính kèm không được để trống.");
                    if (!currentQuestionIds.Contains(question.Id) && stimulus?.StimulusType == StimulusType.Audio
                        && !StimulusAudioUrl.TryGetHash(stimulus.ResourceUrl, out _))
                        return Invalid(practiceId, "Câu hỏi âm thanh mới phải dùng tệp upload nội bộ, không dùng URL ngoài.");
                }
                var audioError = await StimulusAudioIntegrity.GetErrorAsync(questions.Select(q => q.Stimulus), _audioStore, cancellationToken);
                if (audioError != null) return Invalid(practiceId, audioError);
            }

            // Chỉ đến đây khi toàn bộ danh sách mới hợp lệ.
            var oldItems = await _db.PracticeQuestions
                .Where(item => item.PracticeId == practiceId && item.Version == practice.Version)
                .ToListAsync(cancellationToken);

            if (practice.Status == PracticeStatus.Draft && oldItems.Count > 0)
            {
                // Chỉ xóa LIÊN KẾT trong Practice Draft.
                // Không xóa Question hoặc QuestionOption.
                _db.PracticeQuestions.RemoveRange(oldItems);

                // Xóa liên kết cũ trước khi thêm liên kết mới
                // để tránh đụng unique key của QuestionId/OrderIndex.
                // Vẫn nằm trong transaction nên lỗi phía sau sẽ rollback.
                await _db.SaveChangesAsync(cancellationToken);
            }

            if (practice.Status != PracticeStatus.Draft)
            {
                // Giữ nguyên mọi liên kết cũ để phiên đang làm và kết quả cũ vẫn đọc đúng đề.
                practice.Version = checked(practice.Version + 1);
                var lockedAt = DateTimeOffset.UtcNow;
                foreach (var question in questions)
                {
                    question.LockContent(lockedAt);
                    question.Stimulus?.LockContent(lockedAt);
                }
            }

            // Thứ tự trong request quyết định OrderIndex.
            // Draft được phép có danh sách rỗng.
            for (int index = 0; index < questionIds.Length; index++)
            {
                _db.PracticeQuestions.Add(new PracticeQuestion
                {
                    PracticeId = practiceId,
                    Version = practice.Version,
                    QuestionId = questionIds[index],
                    OrderIndex = index
                });
            }

            practice.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(practiceId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Request bị hủy phải được truyền tiếp cho ASP.NET Core.
            throw;
        }
        catch (Exception ex)
        {
            // Chỉ ghi ID phục vụ tra lỗi, không ghi nội dung/đáp án.
            _logger.LogError(
                ex,
                "Updating questions failed for Practice {PracticeId}.",
                practiceId);

            return Invalid(
                practiceId,
                "Chưa thể lưu danh sách câu hỏi. Vui lòng thử lại sau.");
        }
    }

    /// <summary>
    /// Publish và khóa nội dung trong cùng một transaction.
    /// Chỉ dùng danh sách câu hỏi đã lưu trong database.
    /// </summary>
    public async Task<WriteOutcome> PublishAsync(
        Guid actorUserId,
        Guid practiceId,
        Guid? lessonId,
        Guid? moduleId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            bool ownsPractice = await _db.Practices
                .AsNoTracking()
                .AnyAsync(
                    practice =>
                        practice.Id == practiceId
                        && practice.OwnerTeacherUserId == actorUserId,
                    cancellationToken);

            if (!ownsPractice)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            // Hai giá trị cùng có hoặc cùng không có đều không hợp lệ.
            if (lessonId.HasValue == moduleId.HasValue)
            {
                return Invalid(practiceId,
                    "Phải chọn đúng một Lesson hoặc một Module.");
            }

            Guid? courseId = await FindParentCourseIdAsync(
                actorUserId, lessonId, moduleId, cancellationToken);

            if (!courseId.HasValue)
            {
                return Invalid(practiceId,
                    "Lesson hoặc Module không tồn tại "
                    + "hoặc không thuộc quyền sở hữu của bạn.");
            }

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

            // Giữ thứ tự khóa Course -> Practice,
            // giống luồng làm bài của Student đang có.
            var course = await _db.CoursesForUpdate(courseId.Value)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

            if (course == null || course.OwnerTeacherUserId != actorUserId)
            {
                return Invalid(practiceId,
                    "Course không tồn tại hoặc không thuộc quyền sở hữu của bạn.");
            }

            if (course.Status == CourseStatus.Archived)
            {
                return Invalid(practiceId,
                    "Không được Publish Practice trong Course đã Archived.");
            }

            var practice = await _db.PracticesForUpdate(practiceId, actorUserId)
                .SingleOrDefaultAsync(cancellationToken);

            if (practice == null)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            await _db.Entry(practice).ReloadAsync(cancellationToken);

            // Kiểm tra lại tài khoản trước khi ghi dữ liệu.
            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            if (practice.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                return Invalid(practiceId,
                    "Practice đã thay đổi. Hãy tải lại trang trước khi Publish.");
            }

            if (practice.Status != PracticeStatus.Draft
                && practice.Status != PracticeStatus.Published
                && practice.Status != PracticeStatus.Unpublished)
            {
                return Invalid(practiceId, "Trạng thái Practice không hợp lệ.");
            }

            // Sau lần Publish đầu tiên, parent không được thay đổi.
            if (practice.Status != PracticeStatus.Draft
                && (practice.LessonId != lessonId || practice.ModuleId != moduleId))
            {
                return Invalid(practiceId,
                    "Practice đã từng Publish nên không được đổi Lesson/Module.");
            }

            if (practice.Status == PracticeStatus.Draft)
            {
                bool hasSubmission = await _db.PracticeSubmissions
                    .AnyAsync(
                        submission => submission.PracticeId == practiceId,
                        cancellationToken);

                if (hasSubmission)
                {
                    return Invalid(practiceId,
                        "Practice đã có kết quả nên không được xử lý như Draft.");
                }
            }

            // Parent phải vẫn nằm trong Course đang được khóa.
            // Không chỉ tin kết quả tìm trước transaction.
            bool parentStillValid;

            if (lessonId.HasValue)
            {
                parentStillValid = await _db.Lessons
                    .AnyAsync(
                        lesson =>
                            lesson.Id == lessonId.Value
                            && lesson.Module.CourseId == courseId.Value,
                        cancellationToken);
            }
            else
            {
                parentStillValid = await _db.Modules
                    .AnyAsync(
                        module =>
                            module.Id == moduleId!.Value
                            && module.CourseId == courseId.Value,
                        cancellationToken);
            }

            if (!parentStillValid)
            {
                return Invalid(practiceId,
                    "Lesson/Module đã thay đổi. Hãy tải lại trang.");
            }

            var items = await _db.PracticeQuestions
                .Where(item => item.PracticeId == practiceId && item.Version == practice.Version)
                .Include(item => item.Question)
                    .ThenInclude(question => question.Options)
                .Include(item => item.Question)
                    .ThenInclude(question => question.Stimulus)
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

            if (items.Count == 0)
            {
                return Invalid(practiceId,
                    "Practice phải có ít nhất một câu hỏi trước khi Publish.");
            }

            var seenQuestionIds = new HashSet<Guid>();

            // Kiểm tra toàn bộ đề trước khi thay đổi trạng thái hoặc dấu khóa.
            for (int index = 0; index < items.Count; index++)
            {
                var item = items[index];

                if (!seenQuestionIds.Add(item.QuestionId))
                {
                    return Invalid(practiceId,
                        "Practice không được chứa câu hỏi trùng nhau.");
                }

                IReadOnlyList<QuestionError> errors;

                if (practice.Status == PracticeStatus.Draft)
                {
                    // Lần Publish đầu tiên: không chấp nhận Archived.
                    errors = _questionUsageValidator.ValidateForNewUse(
                        actorUserId, item.Question);
                }
                else
                {
                    // Publish lại nội dung cũ:
                    // Archived không tự làm hỏng bài đã từng được công bố.
                    errors = _questionUsageValidator.ValidateForExistingUse(
                        actorUserId, item.Question);
                }

                if (errors.Count > 0)
                {
                    return Invalid(practiceId,
                        $"Câu {index + 1}: {errors[0].Message}");
                }

                var stimulus = item.Question.Stimulus;

                if (practice.Status == PracticeStatus.Draft && stimulus?.StimulusType == StimulusType.Audio
                    && !StimulusAudioUrl.TryGetHash(stimulus.ResourceUrl, out _))
                    return Invalid(practiceId, $"Câu {index + 1}: hãy upload audio bất biến trước khi Publish; URL ngoài có thể bị thay nội dung.");

                if (stimulus != null
                    && stimulus.StimulusType == StimulusType.Passage
                    && string.IsNullOrWhiteSpace(stimulus.ContentText))
                {
                    return Invalid(practiceId,
                        $"Câu {index + 1}: passage không được để trống.");
                }
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            string? audioError = await StimulusAudioIntegrity.GetErrorAsync(
                items.Select(q => q.Question.Stimulus), _audioStore, cancellationToken);
            if (audioError != null) return Invalid(practiceId, audioError);

            // Chỉ khóa sau khi tất cả câu hỏi đều hợp lệ.
            // LockContent giữ nguyên thời điểm nếu nội dung đã bị khóa trước đó.
            foreach (var item in items)
            {
                item.Question.LockContent(now);

                if (item.Question.Stimulus != null)
                {
                    item.Question.Stimulus.LockContent(now);
                }
            }

            practice.LessonId = lessonId;
            practice.ModuleId = moduleId;
            practice.Status = PracticeStatus.Published;
            practice.UpdatedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(practiceId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Publishing Practice failed for {PracticeId}.", practiceId);

            return Invalid(practiceId,
                "Chưa thể Publish Practice. Vui lòng thử lại.");
        }
    }

    /// <summary>
    /// Ngừng cho Student làm bài nhưng giữ nguyên đề và kết quả.
    /// Không chuyển Practice trở lại Draft.
    /// </summary>
    public async Task<WriteOutcome> UnpublishAsync(
        Guid actorUserId,
        Guid practiceId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            var snapshot = await _db.Practices
                .AsNoTracking()
                .Where(practice =>
                    practice.Id == practiceId
                    && practice.OwnerTeacherUserId == actorUserId)
                .Select(practice => new
                {
                    practice.LessonId,
                    practice.ModuleId
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (snapshot == null)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            Guid? courseId = await FindParentCourseIdAsync(
                actorUserId, snapshot.LessonId, snapshot.ModuleId,
                cancellationToken);

            if (!courseId.HasValue)
            {
                return Invalid(practiceId,
                    "Không xác định được parent hợp lệ của Practice.");
            }

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

            // Cho phép Unpublish kể cả Course đã Archived.
            // Đây là thao tác rút bài, không phải mở bài mới cho Student.
            await _db.CoursesForUpdate(courseId.Value)
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            var practice = await _db.PracticesForUpdate(practiceId, actorUserId)
                .SingleOrDefaultAsync(cancellationToken);

            if (practice == null)
            {
                return WriteOutcome.Denied(LearningAccessResult.NotFound);
            }

            await _db.Entry(practice).ReloadAsync(cancellationToken);

            if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
            {
                return WriteOutcome.Denied(LearningAccessResult.Forbidden);
            }

            if (practice.UpdatedAtUtc != expectedUpdatedAtUtc
                || practice.LessonId != snapshot.LessonId
                || practice.ModuleId != snapshot.ModuleId)
            {
                return Invalid(practiceId,
                    "Practice đã thay đổi. Hãy tải lại trang.");
            }

            if (practice.Status == PracticeStatus.Unpublished)
            {
                return WriteOutcome.Ok(practiceId);
            }

            if (practice.Status != PracticeStatus.Published)
            {
                return Invalid(practiceId,
                    "Chỉ được Unpublish Practice đang Published.");
            }

            practice.Status = PracticeStatus.Unpublished;
            practice.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // Không xóa câu hỏi, kết quả hoặc dấu khóa nội dung.
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return WriteOutcome.Ok(practiceId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unpublishing Practice failed for {PracticeId}.", practiceId);

            return Invalid(practiceId,
                "Chưa thể Unpublish Practice. Vui lòng thử lại.");
        }
    }

    /// <summary>
    /// Tìm Course chứa Lesson/Module trong phạm vi Teacher sở hữu.
    /// Không nhận CourseId hoặc owner từ client để quyết định quyền.
    /// </summary>
    private async Task<Guid?> FindParentCourseIdAsync(
        Guid actorUserId,
        Guid? lessonId,
        Guid? moduleId,
        CancellationToken cancellationToken)
    {
        if (lessonId.HasValue)
        {
            return await _db.Lessons
                .AsNoTracking()
                .Where(lesson =>
                    lesson.Id == lessonId.Value
                    && lesson.Module.Course.OwnerTeacherUserId
                        == actorUserId)
                .Select(lesson => (Guid?)lesson.Module.CourseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (moduleId.HasValue)
        {
            return await _db.Modules
                .AsNoTracking()
                .Where(module =>
                    module.Id == moduleId.Value
                    && module.Course.OwnerTeacherUserId == actorUserId)
                .Select(module => (Guid?)module.CourseId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Giữ cùng quy tắc quyền với service tạo Practice hiện có:
    /// tài khoản Active, có đúng một role và role đó là Teacher.
    /// </summary>
    private Task<bool> IsActiveTeacherAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _db.HasActiveRoleAsync(userId, AppRoles.Teacher, cancellationToken);

    /// <summary>
    /// Trả lỗi gắn với danh sách câu hỏi.
    /// params cho phép truyền một hoặc nhiều thông báo vào hàm.
    /// </summary>
    private static WriteOutcome Invalid(
        Guid practiceId,
        params string[] messages)
    {
        var errors = new Dictionary<string, string[]>();

        errors[nameof(UpdatePracticeQuestionsRequest.QuestionIds)] =
            messages;

        return new WriteOutcome(
            LearningAccessResult.Allowed,
            practiceId,
            errors);
    }
}

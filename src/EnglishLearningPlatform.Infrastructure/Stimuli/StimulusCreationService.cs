using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishLearningPlatform.Infrastructure.Stimuli;

public sealed class StimulusCreationService : IStimulusCreationService
{
    private readonly AppDbContext _db;
    private readonly ICreateStimulusValidator _validator;
    private readonly IStimulusAudioStore _audioStore;
    private readonly ILogger<StimulusCreationService> _logger;

    /// <summary>
    /// Nhận dependency từ DI, không tự tạo DbContext hoặc validator trong service.
    /// </summary>
    public StimulusCreationService(AppDbContext db, ICreateStimulusValidator validator, IStimulusAudioStore audioStore,
        ILogger<StimulusCreationService> logger)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }
        if (validator == null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        _db = db;
        _validator = validator;
        _audioStore = audioStore ?? throw new ArgumentNullException(nameof(audioStore));
        _logger = logger;
    }

    /// <summary>
    /// Tạo ngữ liệu mới, không thay đổi Question/Stimulus hoặc dữ liệu lịch sử đã có.
    /// </summary>
    public async Task<WriteOutcome> CreateAsync(
        Guid actorUserId,
        CreateStimulusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (actorUserId == Guid.Empty)
        {
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        }

        // Serializable giữ việc đọc quyền và ghi dữ liệu trong cùng transaction.
        // Thoát trước CommitAsync thì transaction tự rollback.
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        if (!await IsActiveTeacherAsync(actorUserId, cancellationToken))
        {
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        }

        var errors = _validator.Validate(request);
        if (errors.Count > 0)
        {
            return new WriteOutcome(LearningAccessResult.Allowed, Guid.Empty, errors);
        }

        string? title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = null;
        }

        var stimulus = new Stimulus
        {
            // Không lấy owner, ID hoặc dấu khóa từ dữ liệu người dùng gửi lên.
            OwnerTeacherUserId = actorUserId,
            Title = title
        };

        if (request.StimulusType == StimulusType.Passage)
        {
            stimulus.StimulusType = StimulusType.Passage;
            stimulus.ContentText = request.ContentText?.Trim();
        }
        else
        {
            // Validator đã bảo đảm loại hợp lệ và URL không rỗng.
            stimulus.StimulusType = StimulusType.Audio;
            try
            {
                stimulus.ResourceUrl = request.AudioContent == null ? request.ResourceUrl?.Trim()
                    : await _audioStore.StoreAsync(request.AudioContent, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                _logger.LogError(exception, "Storing stimulus audio failed for Teacher {TeacherId}.", actorUserId);
                return new WriteOutcome(LearningAccessResult.Allowed, Guid.Empty,
                    new Dictionary<string, string[]> { [nameof(request.AudioContent)] = new[] { "Chưa lưu được audio. Hãy kiểm tra dung lượng/quyền ghi và tình trạng kho audio của máy chủ." } });
            }
        }

        // Không tải URL ngoài; upload được lưu theo hash và không có API ghi đè/xóa.
        _db.Stimuli.Add(stimulus);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WriteOutcome.Ok(stimulus.Id);
    }

    /// <summary>
    /// Đọc quyền hiện tại trong database, không chỉ tin role trong cookie đăng nhập.
    /// Giữ quy tắc một tài khoản có đúng một role đang áp dụng trong dự án.
    /// </summary>
    private Task<bool> IsActiveTeacherAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _db.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                && user.AccountStatus == AccountStatus.Active
                && _db.UserRoles.Count(userRole => userRole.UserId == userId) == 1
                && _db.UserRoles.Any(userRole => userRole.UserId == userId
                    && _db.Roles.Any(role => role.Id == userRole.RoleId && role.Name == AppRoles.Teacher)),
            cancellationToken);
    }
}

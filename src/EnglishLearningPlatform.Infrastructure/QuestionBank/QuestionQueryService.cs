using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Application.Identity;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.QuestionBank;
public class QuestionQueryService : IQuestionQueryService
{   
    private readonly AppDbContext _dbContext;

    private readonly IActiveUserValidator _activeUsers;
   public QuestionQueryService(AppDbContext db, UserManager<ApplicationUser> userManager, IActiveUserValidator? activeUsers = null)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }
        if (userManager == null)
        {
            throw new ArgumentNullException(nameof(userManager));
        }
        _dbContext = db;
        _activeUsers = activeUsers ?? new ActiveUserValidator(userManager, db);
    }
    public async Task<QuestionSearchResult?> SearchAsync(Guid actorUserId,QuestionSearchRequest request, CancellationToken cancellationToken = default)
    {
        if(request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        //dừng thao tác nếu phía gọi dừng
        cancellationToken.ThrowIfCancellationRequested();
        
        if(!await _activeUsers.IsActiveTeacherAsync(actorUserId, cancellationToken))
        {
           return null;
        }
        int pageNumber = request.PageNumber;
        int pageSize = request.PageSize;
        if(pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.PageNumber), "PageNumber must be greater than 0");
        }
        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PageSize),
                "Số câu mỗi trang phải nằm trong khoảng từ 1 đến 100.");
        }
        if (request.PrimarySkill.HasValue)
        {
            if (!Enum.IsDefined(
                typeof(EnglishSkill),
                request.PrimarySkill.Value))
            {
                throw new ArgumentException(
                    "PrimarySkill không hợp lệ.",
                    nameof(request.PrimarySkill));
            }
        }
        if (request.Status.HasValue)
        {
            if (!Enum.IsDefined(
                typeof(QuestionStatus),
                request.Status.Value))
            {
                throw new ArgumentException(
                    "Status không hợp lệ.",
                    nameof(request.Status));
            }
        }
        // Luôn giới hạn theo owner trước khi thêm các điều kiện tìm kiếm.
        // AsNoTracking dùng cho việc chỉ đọc, không chỉnh sửa entity.
        IQueryable<Question> query = _dbContext.Questions
            .AsNoTracking()
            .Where(question => question.OwnerTeacherUserId == actorUserId);
        // Tìm từ khóa trong nội dung câu hỏi.
        // Bỏ qua nếu không nhập từ khóa hoặc chỉ nhập khoảng trắng.
        if(!string.IsNullOrWhiteSpace(request.Search))
        {
            string search = request.Search.Trim();
            query = query.Where(question => question.Content.Contains(search));
        }
        // Lọc theo đúng một skill được chọn
        if (request.PrimarySkill.HasValue)
        {
            query = query.Where(question => question.PrimarySkill == request.PrimarySkill.Value);
        }
        // Lọc theo đúng một status được chọn
        if (request.Status.HasValue)
        {
            query = query.Where(question => question.Status == request.Status.Value);
        }
        // Lọc theo đúng một level được chọn
        if (!string.IsNullOrWhiteSpace(request.Level))
        {
            string level = request.Level.Trim();
            query = query.Where(question => question.Level == level);
        }
            // Đếm sau khi áp dụng owner và toàn bộ bộ lọc,
        // nhưng trước khi lấy một trang.
        int totalCount = await query.CountAsync(cancellationToken);

        // Dùng long để tránh tràn số khi số trang rất lớn.
        long skipCount = ((long)pageNumber - 1) * pageSize;

        // Trang vượt quá kết quả sẽ nhận danh sách rỗng.
        // Đây không phải trường hợp bị từ chối quyền truy cập.
        if (skipCount >= totalCount)
        {
            return new QuestionSearchResult(new List<QuestionListItem>(), totalCount, pageNumber, pageSize);
        }
        // Lấy câu mới nhất trước.
        // ID giúp thứ tự ổn định khi nhiều câu có cùng thời gian tạo.
        List<QuestionListItem> items = await query
        .OrderByDescending(question => question.CreatedAtUtc)
        .ThenBy(question => question.Id)
        .Skip((int)skipCount)
        .Take(pageSize).Select(question => new QuestionListItem
        {
            Id = question.Id,
            Content = question.Content,
            Level = question.Level,
            PrimarySkill = question.PrimarySkill,
            Status = question.Status,
            OptionCount = question.Options.Count,
            CreatedAtUtc = question.CreatedAtUtc
        }).ToListAsync(cancellationToken);
        return new QuestionSearchResult(
        items,
        totalCount,
        pageNumber,
        pageSize);
    }
    public async Task<QuestionDetail?> GetByIdAsync(Guid actorUserId,Guid questionId, CancellationToken cancellationToken = default)
    {
        // Nếu phía gọi đã hủy yêu cầu thì dừng trước khi truy vấn.
        cancellationToken.ThrowIfCancellationRequested();

        // Giữ cách xử lý đã thống nhất:
        // ID rỗng không tìm được câu hỏi, nên trả null.
        if (questionId == Guid.Empty)
        {
            return null;
        }
        if(!await _activeUsers.IsActiveTeacherAsync(actorUserId, cancellationToken))
        {
            return null;
        }
        Question? question = await _dbContext.Questions
            .AsNoTracking()
            .Include(q => q.Options)
            .Include(q => q.Stimulus)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.OwnerTeacherUserId == actorUserId, cancellationToken);
        if(question == null)
        {
            return null;
        }
        Stimulus? stimulus = question.Stimulus;
        if (stimulus != null
        && stimulus.OwnerTeacherUserId != actorUserId)
        {
          return null;
        }
        // chuyển đối tượng từ entity sang dto
        QuestionDetail detail = new QuestionDetail
        {
            Id = question.Id,
            Content = question.Content,
            Level = question.Level,
            PrimarySkill = question.PrimarySkill,
            Status = question.Status,
            Explanation = question.Explanation,
            CreatedAtUtc = question.CreatedAtUtc,
            UpdatedAtUtc = question.UpdatedAtUtc,
        };
        // sap xếp các option theo OrderIndex tăng dần
        List<QuestionOptionItem> optionItems = new List<QuestionOptionItem>();
        foreach (QuestionOption option in question.Options.OrderBy(item => item.OrderIndex))
        {
            QuestionOptionItem optionItem = new QuestionOptionItem
            {
                Id = option.Id,
                Content = option.Content,
                OrderIndex = option.OrderIndex,
                IsCorrect = option.IsCorrect
            };
            optionItems.Add(optionItem);
        }
        detail.Options = optionItems;
        if(stimulus != null)
        {
            detail.Stimulus = new QuestionStimulusDetail
            {
                Id = stimulus.Id,
                StimulusType = stimulus.StimulusType,
                Title = stimulus.Title,
                ContentText = stimulus.ContentText,
                ResourceUrl = stimulus.ResourceUrl,
            };
        }
        return detail;
    }
    
}

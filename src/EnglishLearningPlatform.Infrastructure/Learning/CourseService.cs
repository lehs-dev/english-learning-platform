using System.ComponentModel.DataAnnotations;
using System.Data;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseService(AppDbContext db, ILearningAccessService access, UserManager<ApplicationUser> users) : ICourseService
{
    private IQueryable<CourseSummary> Summaries(IQueryable<Course> query, bool publicOnly = false) => query.Select(c => new CourseSummary(
        c.Id, c.OwnerTeacherUserId, c.Title, c.Description, c.Objectives,
        db.Users.Where(u => u.Id == c.OwnerTeacherUserId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
        c.Level, c.Status, c.IsPaid, c.Price, c.Skills.Select(s => s.Skill).ToList(),
        c.Topics.Select(t => t.Topic).ToList(), c.Modules.Count(m => !publicOnly || m.Visibility == ModuleVisibility.Visible),
        c.Modules.Where(m => !publicOnly || m.Visibility == ModuleVisibility.Visible).SelectMany(m => m.Lessons).Count(l => !publicOnly || l.Status == LessonStatus.Published)));

    private async Task<bool> ActiveRole(Guid id, string role)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || user.AccountStatus != AccountStatus.Active) return false;
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 && roles[0] == role;
    }
    private Task<LearningAccessResult> Manage(Guid user, Guid course, CancellationToken ct) =>
        access.CheckAccessAsync(user, LearningResourceType.Course, course, LearningOperation.ManageContent, ct);
    private IQueryable<Course> Graph() => db.Courses.Include(c => c.Skills).Include(c => c.Topics)
        .Include(c => c.FinalAssessment).Include(c => c.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources).AsSplitQuery();

    public async Task<CatalogPage> CatalogAsync(CatalogQuery q, CancellationToken ct = default)
    {
        q.Search = q.Search?.Trim(); q.Topic = q.Topic?.Trim();
        if (q.Search?.Length > 200) q.Search = q.Search[..200];
        if (q.Topic?.Length > 120) q.Topic = q.Topic[..120];
        if (q.Level.HasValue && !Enum.IsDefined(q.Level.Value)) q.Level = null;
        if (q.Skill.HasValue && !Enum.IsDefined(q.Skill.Value)) q.Skill = null;
        var published = db.Courses.AsNoTracking().Where(c => c.Status == CourseStatus.Published);
        var query = published;
        if (!string.IsNullOrEmpty(q.Search)) query = query.Where(c => c.Title.Contains(q.Search));
        if (q.Level.HasValue) query = query.Where(c => c.Level == q.Level);
        if (q.Skill.HasValue) query = query.Where(c => c.Skills.Any(s => s.Skill == q.Skill));
        if (!string.IsNullOrEmpty(q.Topic)) query = query.Where(c => c.Topics.Any(t => t.Topic == q.Topic));
        if (q.Teacher.HasValue) query = query.Where(c => c.OwnerTeacherUserId == q.Teacher);
        if (q.Paid.HasValue) query = query.Where(c => c.IsPaid == q.Paid);
        var count = await query.CountAsync(ct);
        q.Page = Math.Clamp(q.Page, 1, Math.Max(1, (count + CatalogPage.PageSize - 1) / CatalogPage.PageSize));
        var items = await Summaries(query.OrderBy(c => c.Title).ThenBy(c => c.Id)
            .Skip((q.Page - 1) * CatalogPage.PageSize).Take(CatalogPage.PageSize), true).ToListAsync(ct);
        var teachers = await db.Users.AsNoTracking().Where(u => published.Any(c => c.OwnerTeacherUserId == u.Id))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).Select(u => new TeacherOption(u.Id, u.FullName)).ToListAsync(ct);
        var topics = await db.CourseTopics.Where(t => t.Course.Status == CourseStatus.Published)
            .Select(t => t.Topic).Distinct().OrderBy(t => t).ToListAsync(ct);
        return new(q, items, count, teachers, topics);
    }

    public async Task<CoursePage?> PublicDetailAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var summary = await Summaries(db.Courses.AsNoTracking().Where(c => c.Id == id && c.Status == CourseStatus.Published), true).SingleOrDefaultAsync(ct);
        if (summary is null) return null;
        var owner = userId.HasValue && await Manage(userId.Value, id, ct) == LearningAccessResult.Allowed;
        var learn = userId.HasValue && await access.CheckAccessAsync(userId.Value, LearningResourceType.Course, id, LearningOperation.ViewContent, ct) == LearningAccessResult.Allowed;
        return new(summary, await Outline(id, false, ct), learn, owner, 0, []);
    }
    private async Task<List<ModuleOutline>> Outline(Guid id, bool owner, CancellationToken ct) =>
        await db.Modules.AsNoTracking().Where(m => m.CourseId == id && (owner || m.Visibility == ModuleVisibility.Visible))
            .OrderBy(m => m.OrderIndex).ThenBy(m => m.Id).Select(m => new ModuleOutline(m.Id, m.Title, m.Description, m.Visibility,
                m.Lessons.Where(l => owner || l.Status == LessonStatus.Published).OrderBy(l => l.OrderIndex).ThenBy(l => l.Id)
                    .Select(l => new LessonOutline(l.Id, l.Title, l.Status)).ToList())).ToListAsync(ct);

    public async Task<LearningOutcome<IReadOnlyList<CourseSummary>>> MyCoursesAsync(Guid userId, bool teacher, CancellationToken ct = default)
    {
        if (!await ActiveRole(userId, teacher ? AppRoles.Teacher : AppRoles.Student)) return new(LearningAccessResult.Forbidden);
        var query = db.Courses.AsNoTracking().Where(c => teacher ? c.OwnerTeacherUserId == userId :
            c.Status != CourseStatus.Draft && c.Enrollments.Any(e => e.StudentUserId == userId &&
                (!e.PaymentId.HasValue || (e.Payment!.Status == PaymentStatus.Succeeded && e.Payment.Order.StudentUserId == userId &&
                    e.Payment.Order.CourseId == c.Id && e.Payment.Amount == e.Payment.Order.Amount && e.Payment.Order.Currency == "VND"))));
        return new(LearningAccessResult.Allowed, await Summaries(query.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id)).ToListAsync(ct));
    }
    public async Task<LearningOutcome<CoursePage>> OpenCourseAsync(Guid userId, Guid id, bool manage, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Course, id,
            manage ? LearningOperation.ManageContent : LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var owner = await Manage(userId, id, ct) == LearningAccessResult.Allowed;
        var summary = await Summaries(db.Courses.AsNoTracking().Where(c => c.Id == id)).SingleAsync(ct);
        var errors = owner ? CourseRules.PublishErrors((await Graph().AsNoTracking().SingleAsync(c => c.Id == id, ct))) : [];
        return new(allowed, new(summary, await Outline(id, owner, ct), true, owner,
            owner ? await db.Enrollments.CountAsync(e => e.CourseId == id, ct) : 0, errors));
    }
    public async Task<LearningOutcome<LessonPage>> OpenLessonAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Lesson, id, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var lesson = await db.Lessons.AsNoTracking().Where(l => l.Id == id).Select(l => new LessonPage(l.Id,
            l.Module.CourseId, l.Module.Course.Title, l.Title, l.Resources.OrderBy(r => r.OrderIndex).ThenBy(r => r.Id)
                .Select(r => new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl)).ToList())).SingleAsync(ct);
        return new(allowed, lesson);
    }
    public async Task<LearningOutcome<ResourceContent>> OpenResourceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Resource, id, LearningOperation.ViewContent, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        return new(allowed, await db.LessonResources.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl)).SingleAsync(ct));
    }
    public async Task<LearningOutcome<Guid>> ModuleCourseAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await access.CheckAccessAsync(userId, LearningResourceType.Module, id, LearningOperation.ViewContent, ct);
        return new(allowed, allowed == LearningAccessResult.Allowed ? await db.Modules.Where(m => m.Id == id).Select(m => m.CourseId).SingleAsync(ct) : Guid.Empty);
    }
    public async Task<LearningOutcome<IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>>>> AuthoringResourcesAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var rows = await db.LessonResources.AsNoTracking().Where(r => r.Lesson.Module.CourseId == courseId)
            .OrderBy(r => r.OrderIndex).ThenBy(r => r.Id).Select(r => new { r.LessonId, Resource = new ResourceContent(r.Id, r.Title, r.ResourceType, r.ContentText, r.ResourceUrl) }).ToListAsync(ct);
        return new(allowed, rows.GroupBy(r => r.LessonId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceContent>)g.Select(r => r.Resource).ToList()));
    }
    public async Task<LearningOutcome<CourseInput>> CourseInputAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, id, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        var c = await db.Courses.AsNoTracking().Include(c => c.Skills).Include(c => c.Topics).SingleAsync(c => c.Id == id, ct);
        return new(allowed, new() { Title = c.Title, Description = c.Description, Objectives = c.Objectives, Level = c.Level,
            IsPaid = c.IsPaid, Price = c.Price, Skills = c.Skills.Select(s => s.Skill).ToArray(), Topics = string.Join(", ", c.Topics.Select(t => t.Topic)) });
    }
    private static Dictionary<string, string[]> Validate(object input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        return results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? "").ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage!).ToArray());
    }
    private static WriteOutcome Invalid(Guid id, Dictionary<string, string[]> errors) => new(LearningAccessResult.Allowed, id, errors);
    public async Task<WriteOutcome> SaveCourseAsync(Guid userId, Guid? id, CourseInput input, CancellationToken ct = default)
    {
        var allowed = id.HasValue ? await Manage(userId, id.Value, ct) : await ActiveRole(userId, AppRoles.Teacher) ? LearningAccessResult.Allowed : LearningAccessResult.Forbidden;
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        input.Title = input.Title?.Trim() ?? "";
        var errors = Validate(input);
        if (!CourseRules.ValidPrice(input.IsPaid, input.Price)) errors[nameof(input.Price)] = ["Free: giá phải bằng 0. Paid: giá phải dương, tối đa 2 chữ số thập phân và trong giới hạn decimal(18,2)."];
        if (input.Skills is null || input.Skills.Any(s => !Enum.IsDefined(s))) errors[nameof(input.Skills)] = ["Kỹ năng không hợp lệ."];
        var topics = (input.Topics ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (topics.Any(t => t.Length > 120)) errors[nameof(input.Topics)] = ["Mỗi topic tối đa 120 ký tự."];
        if (errors.Count > 0) return Invalid(id ?? Guid.Empty, errors);
        var course = id.HasValue ? await db.Courses.Include(c => c.Skills).Include(c => c.Topics).SingleAsync(c => c.Id == id, ct) : new Course { OwnerTeacherUserId = userId };
        if (!id.HasValue) db.Courses.Add(course);
        course.Title = input.Title; course.Description = input.Description?.Trim(); course.Objectives = input.Objectives?.Trim();
        course.Level = input.Level; course.IsPaid = input.IsPaid; course.Price = input.Price; course.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var skillSet = input.Skills!.Distinct().ToHashSet();
        foreach (var old in course.Skills.Where(s => !skillSet.Contains(s.Skill)).ToList()) db.CourseSkills.Remove(old);
        foreach (var skill in skillSet.Where(s => course.Skills.All(old => old.Skill != s))) course.Skills.Add(new CourseSkill { Skill = skill });
        foreach (var old in course.Topics.Where(t => !topics.Contains(t.Topic, StringComparer.OrdinalIgnoreCase)).ToList()) db.CourseTopics.Remove(old);
        foreach (var topic in topics.Where(t => course.Topics.All(old => !old.Topic.Equals(t, StringComparison.OrdinalIgnoreCase)))) course.Topics.Add(new CourseTopic { Topic = topic });
        await db.SaveChangesAsync(ct);
        return WriteOutcome.Ok(course.Id);
    }
    private async Task<bool> ParentMatches(Guid courseId, ContentKind kind, Guid parentId, CancellationToken ct) => kind switch
    {
        ContentKind.Module => parentId == courseId,
        ContentKind.Lesson => await db.Modules.AnyAsync(m => m.Id == parentId && m.CourseId == courseId, ct),
        ContentKind.Resource => await db.Lessons.AnyAsync(l => l.Id == parentId && l.Module.CourseId == courseId, ct),
        _ => false
    };
    public async Task<LearningOutcome<ContentInput>> ContentInputAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return new(allowed);
        if (!await ParentMatches(courseId, kind, parentId, ct)) return new(LearningAccessResult.NotFound);
        if (!id.HasValue) return new(allowed, new());
        ContentInput? input = kind switch
        {
            ContentKind.Module => await db.Modules.Where(m => m.Id == id && m.CourseId == parentId).Select(m => new ContentInput { Title = m.Title, Description = m.Description, Visibility = m.Visibility }).SingleOrDefaultAsync(ct),
            ContentKind.Lesson => await db.Lessons.Where(l => l.Id == id && l.ModuleId == parentId).Select(l => new ContentInput { Title = l.Title, Status = l.Status }).SingleOrDefaultAsync(ct),
            ContentKind.Resource => await db.LessonResources.Where(r => r.Id == id && r.LessonId == parentId).Select(r => new ContentInput { Title = r.Title, ResourceType = r.ResourceType, ContentText = r.ContentText, ResourceUrl = r.ResourceUrl }).SingleOrDefaultAsync(ct),
            _ => null
        };
        return new(input is null ? LearningAccessResult.NotFound : allowed, input);
    }
    public async Task<WriteOutcome> SaveContentAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid? id, ContentInput input, CancellationToken ct = default)
    {
        var check = await ContentInputAsync(userId, courseId, kind, parentId, id, ct);
        if (check.Access != LearningAccessResult.Allowed) return WriteOutcome.Denied(check.Access);
        input.Title = input.Title?.Trim(); input.ContentText = input.ContentText?.Trim(); input.ResourceUrl = input.ResourceUrl?.Trim();
        var errors = Validate(input);
        if (kind != ContentKind.Resource && string.IsNullOrWhiteSpace(input.Title)) errors[nameof(input.Title)] = ["Tên không được để trống."];
        if (kind == ContentKind.Module && !Enum.IsDefined(input.Visibility)) errors[nameof(input.Visibility)] = ["Visibility không hợp lệ."];
        if (kind == ContentKind.Lesson && !Enum.IsDefined(input.Status)) errors[nameof(input.Status)] = ["Status không hợp lệ."];
        if (kind == ContentKind.Resource && !CourseRules.ValidResource(input.ResourceType, input.ContentText, input.ResourceUrl))
            errors[input.ResourceType == LessonResourceType.Text ? nameof(input.ContentText) : nameof(input.ResourceUrl)] = ["Text cần nội dung; Link/Audio/Video cần URL http/https hợp lệ và loại resource hợp lệ."];
        if (kind == ContentKind.Resource && !Enum.IsDefined(input.ResourceType)) errors[nameof(input.ResourceType)] = ["Loại resource không hợp lệ."];
        if (errors.Count > 0) return Invalid(courseId, errors);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        Guid savedId;
        if (kind == ContentKind.Module)
        {
            var m = id.HasValue ? await db.Modules.SingleAsync(m => m.Id == id && m.CourseId == parentId, ct) : new Module { CourseId = parentId, OrderIndex = (await db.Modules.Where(m => m.CourseId == parentId).MaxAsync(m => (int?)m.OrderIndex, ct) ?? -1) + 1 };
            m.Title = input.Title!; m.Description = input.Description?.Trim(); m.Visibility = input.Visibility;
            if (!id.HasValue) db.Modules.Add(m); savedId = m.Id;
        }
        else if (kind == ContentKind.Lesson)
        {
            var l = id.HasValue ? await db.Lessons.Include(l => l.Resources).SingleAsync(l => l.Id == id && l.ModuleId == parentId, ct) : new Lesson { ModuleId = parentId, OrderIndex = (await db.Lessons.Where(l => l.ModuleId == parentId).MaxAsync(l => (int?)l.OrderIndex, ct) ?? -1) + 1 };
            if (input.Status == LessonStatus.Published && (l.Resources.Count == 0 || l.Resources.Any(r => !CourseRules.ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
                return Invalid(courseId, new() { [nameof(input.Status)] = ["Thêm resource hợp lệ trước khi chuyển Lesson sang Published."] });
            l.Title = input.Title!; l.Status = input.Status;
            if (!id.HasValue) db.Lessons.Add(l); savedId = l.Id;
        }
        else
        {
            var r = id.HasValue ? await db.LessonResources.SingleAsync(r => r.Id == id && r.LessonId == parentId, ct) : new LessonResource { LessonId = parentId, OrderIndex = (await db.LessonResources.Where(r => r.LessonId == parentId).MaxAsync(r => (int?)r.OrderIndex, ct) ?? -1) + 1 };
            r.Title = input.Title; r.ResourceType = input.ResourceType;
            r.ContentText = input.ResourceType == LessonResourceType.Text ? input.ContentText : null;
            r.ResourceUrl = input.ResourceType == LessonResourceType.Text ? null : input.ResourceUrl;
            if (!id.HasValue) db.LessonResources.Add(r); savedId = r.Id;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return WriteOutcome.Ok(savedId);
    }
    public async Task<WriteOutcome> DeleteResourceAsync(Guid userId, Guid courseId, Guid lessonId, Guid id, CancellationToken ct = default)
    {
        var check = await ContentInputAsync(userId, courseId, ContentKind.Resource, lessonId, id, ct);
        if (check.Access != LearningAccessResult.Allowed) return WriteOutcome.Denied(check.Access);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var lesson = await db.Lessons.Include(l => l.Resources).SingleAsync(l => l.Id == lessonId, ct);
        if (lesson.Status == LessonStatus.Published && (lesson.Resources.Count <= 1 ||
            lesson.Resources.Where(r => r.Id != id).Any(r => !CourseRules.ValidResource(r.ResourceType, r.ContentText, r.ResourceUrl))))
            return Invalid(courseId, new() { [""] = ["Không thể xóa resource cuối của Lesson Published. Chuyển Lesson về Draft/Hidden trước."] });
        db.LessonResources.Remove(lesson.Resources.Single(r => r.Id == id));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return WriteOutcome.Ok(courseId);
    }
    public async Task<WriteOutcome> ReorderAsync(Guid userId, Guid courseId, ContentKind kind, Guid parentId, Guid[] ids, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, courseId, ct);
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        if (!await ParentMatches(courseId, kind, parentId, ct)) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entries = kind switch
        {
            ContentKind.Module => (await db.Modules.Where(m => m.CourseId == parentId).ToListAsync(ct)).Select(m => new OrderEntry(m.Id, m.OrderIndex, i => m.OrderIndex = i)).ToList(),
            ContentKind.Lesson => (await db.Lessons.Where(l => l.ModuleId == parentId).ToListAsync(ct)).Select(l => new OrderEntry(l.Id, l.OrderIndex, i => l.OrderIndex = i)).ToList(),
            _ => (await db.LessonResources.Where(r => r.LessonId == parentId).ToListAsync(ct)).Select(r => new OrderEntry(r.Id, r.OrderIndex, i => r.OrderIndex = i)).ToList()
        };
        if (ids is null || ids.Length != entries.Count || ids.Distinct().Count() != ids.Length || entries.Any(e => !ids.Contains(e.Id)))
            return Invalid(courseId, new() { [""] = ["Thứ tự phải chứa đúng mỗi ID của cùng parent một lần."] });
        // Temporary values below every existing/final index avoid unique-index collisions in either save.
        var start = Math.Min(-1L, entries.Count == 0 ? -1L : (long)entries.Min(e => e.Index) - entries.Count - 1);
        if (start < int.MinValue) return Invalid(courseId, new() { [""] = ["OrderIndex vượt giới hạn; cần chuẩn hóa dữ liệu."] });
        for (var i = 0; i < entries.Count; i++) entries[i].Set((int)start + i);
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < ids.Length; i++) entries.Single(e => e.Id == ids[i]).Set(i);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return WriteOutcome.Ok(courseId);
    }
    private sealed record OrderEntry(Guid Id, int Index, Action<int> Set);
    public async Task<WriteOutcome> ChangeStatusAsync(Guid userId, Guid id, CourseStatus status, CancellationToken ct = default)
    {
        var allowed = await Manage(userId, id, ct);
        if (allowed != LearningAccessResult.Allowed) return WriteOutcome.Denied(allowed);
        if (status is not (CourseStatus.Published or CourseStatus.Unpublished or CourseStatus.Archived)) return Invalid(id, new() { [""] = ["Trạng thái không hợp lệ."] });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var course = await Graph().SingleAsync(c => c.Id == id, ct);
        if (status == CourseStatus.Published)
        {
            var errors = CourseRules.PublishErrors(course);
            if (errors.Count > 0) return Invalid(id, new() { [""] = errors.ToArray() });
        }
        course.Status = status; course.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return WriteOutcome.Ok(id);
    }
    public async Task<WriteOutcome> EnrollFreeAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        if (!await ActiveRole(userId, AppRoles.Student)) return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var c = await db.Courses.SingleOrDefaultAsync(c => c.Id == id && c.Status == CourseStatus.Published, ct);
        if (c is null) return WriteOutcome.Denied(LearningAccessResult.NotFound);
        if (c.IsPaid || c.Price != 0 || !await db.Users.AnyAsync(u => u.Id == c.OwnerTeacherUserId && u.AccountStatus != AccountStatus.Disabled, ct))
            return WriteOutcome.Denied(LearningAccessResult.Forbidden);
        // Reserve the enrollment key range before checking, so concurrent repeated POSTs
        // wait for the first insert instead of both taking shared locks then deadlocking.
        if (!await db.Enrollments.FromSqlInterpolated($"SELECT * FROM Enrollments WITH (UPDLOCK, HOLDLOCK) WHERE StudentUserId = {userId} AND CourseId = {id}").AnyAsync(ct))
        {
            db.Enrollments.Add(new Enrollment { StudentUserId = userId, CourseId = id });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return WriteOutcome.Ok(id);
    }
}

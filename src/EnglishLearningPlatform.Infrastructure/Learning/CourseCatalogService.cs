using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseCatalogService(
    AppDbContext db, ILearningAccessService access, CourseQueries queries) : ICourseCatalogService
{
    private Task<LearningAccessResult> Manage(Guid user, Guid course, CancellationToken ct) =>
        access.CheckAccessAsync(user, LearningResourceType.Course, course, LearningOperation.ManageContent, ct);

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
        var items = await queries.Summaries(query.OrderBy(c => c.Title).ThenBy(c => c.Id)
            .Skip((q.Page - 1) * CatalogPage.PageSize).Take(CatalogPage.PageSize), true).ToListAsync(ct);
        var teachers = await db.Users.AsNoTracking().Where(u => published.Any(c => c.OwnerTeacherUserId == u.Id))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).Select(u => new TeacherOption(u.Id, u.FullName)).ToListAsync(ct);
        var topics = await db.CourseTopics.Where(t => t.Course.Status == CourseStatus.Published)
            .Select(t => t.Topic).Distinct().OrderBy(t => t).ToListAsync(ct);
        return new(q, items, count, teachers, topics);
    }

    public async Task<CoursePage?> PublicDetailAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var summary = await queries.Summaries(db.Courses.AsNoTracking().Where(c => c.Id == id && c.Status == CourseStatus.Published), true).SingleOrDefaultAsync(ct);
        if (summary is null) return null;
        var owner = userId.HasValue && await Manage(userId.Value, id, ct) == LearningAccessResult.Allowed;
        var learn = userId.HasValue && await access.CheckAccessAsync(userId.Value, LearningResourceType.Course, id, LearningOperation.ViewContent, ct) == LearningAccessResult.Allowed;
        return new(summary, await queries.OutlineAsync(id, false, ct), learn, owner, 0, []);
    }
}

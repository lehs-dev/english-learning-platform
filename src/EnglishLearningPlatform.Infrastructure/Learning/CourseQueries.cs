using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

public sealed class CourseQueries(AppDbContext db)
{
    public IQueryable<CourseSummary> Summaries(IQueryable<Course> query, bool publicOnly = false) => query.Select(c => new CourseSummary(
        c.Id, c.OwnerTeacherUserId, c.Title, c.Description, c.Objectives,
        db.Users.Where(u => u.Id == c.OwnerTeacherUserId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
        c.Level, c.Status, c.IsPaid, c.Price, c.Skills.Select(s => s.Skill).ToList(),
        c.Topics.Select(t => t.Topic).ToList(), c.Modules.Count(m => !publicOnly || m.Visibility == ModuleVisibility.Visible),
        c.Modules.Where(m => !publicOnly || m.Visibility == ModuleVisibility.Visible).SelectMany(m => m.Lessons).Count(l => !publicOnly || l.Status == LessonStatus.Published)));

    public IQueryable<Course> Graph() => db.Courses.Include(c => c.Skills).Include(c => c.Topics)
        .Include(c => c.FinalAssessment).Include(c => c.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources).AsSplitQuery();

    public async Task<List<ModuleOutline>> OutlineAsync(Guid id, bool owner, CancellationToken ct) =>
        await db.Modules.AsNoTracking().Where(m => m.CourseId == id && (owner || m.Visibility == ModuleVisibility.Visible))
            .OrderBy(m => m.OrderIndex).ThenBy(m => m.Id).Select(m => new ModuleOutline(m.Id, m.Title, m.Description, m.Visibility,
                m.Lessons.Where(l => owner || l.Status == LessonStatus.Published).OrderBy(l => l.OrderIndex).ThenBy(l => l.Id)
                    .Select(l => new LessonOutline(l.Id, l.Title, l.Status)).ToList())).ToListAsync(ct);
}

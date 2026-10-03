using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Persistence;
/*
    AppDbContext là "cửa ngõ" duy nhất để code nói chuyện được với Database.
    Mỗi DbSet<Course> Courses tương ứng với 1 bảng.
    Dòng ApplyConfigurationsFromAssembly nghĩa là mọi class cấu hình trong thư mục Configurations/ sẽ tự động được áp dụng.
*/
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSkill> CourseSkills => Set<CourseSkill>();
    public DbSet<CourseTopic> CourseTopics => Set<CourseTopic>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonResource> LessonResources => Set<LessonResource>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<LessonProgress> LessonProgressEntries => Set<LessonProgress>();
    public DbSet<Stimulus> Stimuli => Set<Stimulus>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<Practice> Practices => Set<Practice>();
    public DbSet<PracticeQuestion> PracticeQuestions => Set<PracticeQuestion>();
    public DbSet<PracticeSubmission> PracticeSubmissions => Set<PracticeSubmission>();
    public DbSet<PracticeAnswer> PracticeAnswers => Set<PracticeAnswer>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<AttemptSkillResult> AttemptSkillResults => Set<AttemptSkillResult>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

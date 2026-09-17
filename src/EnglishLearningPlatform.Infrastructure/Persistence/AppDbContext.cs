using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<LearningProgress> LearningProgressEntries => Set<LearningProgress>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Course>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(220).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<Lesson>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.CourseId, x.OrderNo }).IsUnique();
            e.HasOne(x => x.Course).WithMany(x => x.Lessons).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Enrollment>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.StudentUserId }).IsUnique();
            e.HasOne(x => x.Course).WithMany(x => x.Enrollments).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LearningProgress>(e =>
        {
            e.HasIndex(x => new { x.LessonId, x.StudentUserId }).IsUnique();
            e.HasOne(x => x.Lesson).WithMany(x => x.ProgressEntries).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Question>(e =>
        {
            e.Property(x => x.Prompt).HasMaxLength(4000).IsRequired();
            e.ToTable(t =>t.HasCheckConstraint("CK_Question_Difficulty","[Difficulty] BETWEEN 1 AND 5"));
        });

        builder.Entity<AnswerOption>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.Question).WithMany(x => x.AnswerOptions).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Exam>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.PassingScorePercent).HasPrecision(5, 2);
            e.HasOne(x => x.Course).WithMany(x => x.Exams).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExamQuestion>(e =>
        {
            e.Property(x => x.Points).HasPrecision(8, 2);
            e.HasIndex(x => new { x.ExamId, x.QuestionId }).IsUnique();
            e.HasOne(x => x.Exam).WithMany(x => x.ExamQuestions).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Question).WithMany(x => x.ExamQuestions).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExamAttempt>(e =>
        {
            e.Property(x => x.Score).HasPrecision(8, 2);
            e.Property(x => x.MaxScore).HasPrecision(8, 2);
            e.HasOne(x => x.Exam).WithMany(x => x.Attempts).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ExamId, x.StudentUserId, x.StartedAtUtc });
        });

        builder.Entity<StudentAnswer>(e =>
        {
            e.Property(x => x.AwardedPoints).HasPrecision(8, 2);
            e.HasIndex(x => new { x.ExamAttemptId, x.QuestionId }).IsUnique();
            e.HasOne(x => x.ExamAttempt).WithMany(x => x.StudentAnswers).HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SelectedAnswerOption).WithMany().HasForeignKey(x => x.SelectedAnswerOptionId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}

using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

// Called only by an explicit Development startup option. Guest is anonymous; paid access needs verified payment.
public static class LearningDemoSeeder
{
    private const string FreeCourseTitle = "Demo · Everyday English";
    private const string PaidCourseTitle = "Demo · Business English";
    private const string DraftCourseTitle = "Demo · Draft chưa có nội dung";
    private const string FinalAssessmentTitle = "Demo · Bài thi cuối khóa Everyday English";
    private const string LegacyPaidDescription = "Khóa trả phí mẫu; chưa tích hợp thanh toán.";
    private const string PaidDescription = "Khóa trả phí mẫu; thanh toán qua Sandbox để mở quyền học.";

    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> users, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set LearningDemo:Password using local user-secrets/environment before enabling the demo seed.");

        await User("admin1", AppRoles.Admin);
        var teacher = await User("teacher1", AppRoles.Teacher);
        await User("teacher2", AppRoles.Teacher);
        var student = await User("student1", AppRoles.Student);
        await User("student2", AppRoles.Student);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var free = await db.Courses.SingleOrDefaultAsync(c =>
            c.OwnerTeacherUserId == teacher.Id && c.Title == FreeCourseTitle);
        if (free is null)
        {
            free = FreeCourse(teacher.Id);
            db.Courses.Add(free);
        }

        var paid = await db.Courses.SingleOrDefaultAsync(c =>
            c.OwnerTeacherUserId == teacher.Id && c.Title == PaidCourseTitle);
        if (paid is null)
            db.Courses.Add(PaidCourse(teacher.Id));
        else if (paid.Description == LegacyPaidDescription)
            paid.Description = PaidDescription;

        if (!await db.Courses.AnyAsync(c => c.OwnerTeacherUserId == teacher.Id && c.Title == DraftCourseTitle))
            db.Courses.Add(new Course { OwnerTeacherUserId = teacher.Id, Title = DraftCourseTitle });

        // Backfill existing demos without replacing lessons, progress, paid enrollments, or edited course data.
        if (!free.IsPaid && !await db.Enrollments.AnyAsync(e => e.CourseId == free.Id && e.StudentUserId == student.Id))
            db.Enrollments.Add(new Enrollment { Course = free, StudentUserId = student.Id });
        await db.SaveChangesAsync();

        if (free.FinalAssessmentId is null)
        {
            var final = await db.Assessments.SingleOrDefaultAsync(a => a.OwnerTeacherUserId == teacher.Id &&
                a.CourseId == free.Id && a.Title == FinalAssessmentTitle);
            if (final is null)
            {
                final = FinalAssessment(teacher.Id, free.Id);
                db.Assessments.Add(final);
                await db.SaveChangesAsync();
            }

            // Save the course, then its assessment, then the final pointer: both tables reference each other.
            free.FinalAssessmentId = final.Id;
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();

        async Task<ApplicationUser> User(string name, string role)
        {
            var email = name + "@demo.example.test";
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                var roles = await users.GetRolesAsync(existing);
                if (roles.Count != 1 || roles[0] != role)
                    throw new InvalidOperationException("Demo account already exists with an unexpected role.");
                return existing;
            }
            var user = new ApplicationUser { UserName = email, Email = email, FullName = "Demo " + name };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) throw new InvalidOperationException("Demo password does not meet Identity requirements.");
            var assigned = await users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded) throw new InvalidOperationException("Apply Identity role migrations before demo seeding.");
            return user;
        }
    }

    private static Course FreeCourse(Guid teacherId) => new()
    {
        OwnerTeacherUserId = teacherId,
        Title = FreeCourseTitle,
        Description = "Luyện tiếng Anh hằng ngày với bài học và tài nguyên mẫu.",
        Objectives = "Làm quen từ vựng, nghe và đọc tiếng Anh.",
        Status = CourseStatus.Published,
        Skills = [new CourseSkill { Skill = EnglishSkill.Vocabulary }, new CourseSkill { Skill = EnglishSkill.Listening }],
        Topics = [new CourseTopic { Topic = "Everyday English" }],
        Modules =
        [
            new Module
            {
                Title = "Getting started",
                Lessons =
                [
                    new Lesson
                    {
                        Title = "Hello and welcome", Status = LessonStatus.Published,
                        Resources =
                        [
                            new LessonResource { Title = "Welcome", ResourceType = LessonResourceType.Text,
                                ContentText = "Hello! Welcome to your first English lesson.\nPractice: introduce yourself in two sentences." },
                            new LessonResource { Title = "Dictionary", ResourceType = LessonResourceType.Link,
                                ResourceUrl = "https://dictionary.cambridge.org/", OrderIndex = 1 },
                            new LessonResource { Title = "Audio sample", ResourceType = LessonResourceType.Audio,
                                ResourceUrl = "https://www.w3schools.com/html/horse.mp3", OrderIndex = 2 },
                            new LessonResource { Title = "Video sample", ResourceType = LessonResourceType.Video,
                                ResourceUrl = "https://www.w3schools.com/html/mov_bbb.mp4", OrderIndex = 3 }
                        ]
                    },
                    new Lesson { Title = "Draft lesson", OrderIndex = 1 },
                    new Lesson { Title = "Hidden lesson", Status = LessonStatus.Hidden, OrderIndex = 2 }
                ]
            },
            new Module
            {
                Title = "Hidden module", Visibility = ModuleVisibility.Hidden, OrderIndex = 1,
                Lessons = [new Lesson { Title = "Teacher preview only", Status = LessonStatus.Published,
                    Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Hidden sample" }] }]
            }
        ]
    };

    private static Course PaidCourse(Guid teacherId) => new()
    {
        OwnerTeacherUserId = teacherId, Title = PaidCourseTitle, Description = PaidDescription,
        IsPaid = true, Price = 199000, Status = CourseStatus.Published, Level = CourseLevel.Intermediate,
        Topics = [new CourseTopic { Topic = "Business" }], Skills = [new CourseSkill { Skill = EnglishSkill.Reading }],
        Modules = [new Module { Title = "At work", Lessons = [new Lesson { Title = "Business introductions",
            Status = LessonStatus.Published, Resources = [new LessonResource { ResourceType = LessonResourceType.Text,
                ContentText = "Good morning. Let me introduce our team." }] }] }]
    };

    private static Assessment FinalAssessment(Guid teacherId, Guid courseId) => new()
    {
        OwnerTeacherUserId = teacherId, CourseId = courseId, Title = FinalAssessmentTitle,
        Description = "Bài kiểm tra ngữ pháp và từ vựng: đúng ít nhất 4/5 câu để đạt 80%.",
        // Đề kết hợp Grammar và Vocabulary nên dùng Practice Exam.
        // Thay đổi mẫu tạo mới, không cập nhật Assessment đã có trong database.
        AssessmentType = AssessmentType.PracticeExam,
        TargetSkill = null,
        DurationMinutes = 15, MaxAttempts = 3, PassingScore = 80, Status = AssessmentStatus.Published,
        Questions =
        [
            Question(teacherId, 0, EnglishSkill.Grammar, "She ___ to school every day.", "Use 'goes' with she/he/it in the present simple.",
                ("go", false), ("goes", true), ("going", false), ("gone", false)),
            Question(teacherId, 1, EnglishSkill.Grammar, "They ___ watching a film now.", "Use 'are' with 'they' in the present continuous.",
                ("is", false), ("am", false), ("are", true), ("be", false)),
            Question(teacherId, 2, EnglishSkill.Grammar, "Yesterday, I ___ my homework.", "'Yesterday' indicates the past simple: finished.",
                ("finished", true), ("finish", false), ("finishes", false), ("finishing", false)),
            Question(teacherId, 3, EnglishSkill.Vocabulary, "Choose the word that means 'very happy'.", "'Delighted' means very happy.",
                ("tired", false), ("hungry", false), ("quiet", false), ("delighted", true)),
            Question(teacherId, 4, EnglishSkill.Vocabulary, "The opposite of 'early' is ___.", "'Late' is the opposite of 'early'.",
                ("first", false), ("late", true), ("quick", false), ("soon", false))
        ]
    };

    private static AssessmentQuestion Question(Guid teacherId, int order, EnglishSkill skill, string content, string explanation,
        params (string Content, bool IsCorrect)[] options) => new()
    {
        OrderIndex = order, Points = 1,
        Question = new Question
        {
            OwnerTeacherUserId = teacherId, Content = content, PrimarySkill = skill, Level = "Beginner", Difficulty = "Easy",
            Explanation = explanation, Status = QuestionStatus.Published,
            Options = options.Select((option, index) => new QuestionOption
            {
                Content = option.Content, IsCorrect = option.IsCorrect, OrderIndex = index
            }).ToList()
        }
    };
}

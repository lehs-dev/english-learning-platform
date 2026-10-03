using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

// Called only by an explicit Development startup option. Never grants paid enrollments.
public static class LearningDemoSeeder
{
    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> users, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set LearningDemo:Password using local user-secrets/environment before enabling the demo seed.");
        var teacher = await User("teacher1", AppRoles.Teacher);
        await User("teacher2", AppRoles.Teacher);
        var student = await User("student1", AppRoles.Student);
        await User("student2", AppRoles.Student);
        if (await db.Courses.AnyAsync(c => c.OwnerTeacherUserId == teacher.Id && c.Title == "Demo · Everyday English")) return;
        var free = new Course
        {
            OwnerTeacherUserId = teacher.Id, Title = "Demo · Everyday English", Description = "Luyện tiếng Anh hằng ngày với bài học và tài nguyên mẫu.",
            Objectives = "Làm quen từ vựng, nghe và đọc tiếng Anh.", Status = CourseStatus.Published,
            Skills = [new CourseSkill { Skill = EnglishSkill.Vocabulary }, new CourseSkill { Skill = EnglishSkill.Listening }],
            Topics = [new CourseTopic { Topic = "Everyday English" }],
            Modules = [new Module { Title = "Getting started", Lessons = [new Lesson { Title = "Hello and welcome", Status = LessonStatus.Published,
                Resources = [new LessonResource { Title = "Welcome", ResourceType = LessonResourceType.Text, ContentText = "Hello! Welcome to your first English lesson.\nPractice: introduce yourself in two sentences." },
                    new LessonResource { Title = "Dictionary", ResourceType = LessonResourceType.Link, ResourceUrl = "https://dictionary.cambridge.org/", OrderIndex = 1 },
                    new LessonResource { Title = "Audio sample", ResourceType = LessonResourceType.Audio, ResourceUrl = "https://www.w3schools.com/html/horse.mp3", OrderIndex = 2 },
                    new LessonResource { Title = "Video sample", ResourceType = LessonResourceType.Video, ResourceUrl = "https://www.w3schools.com/html/mov_bbb.mp4", OrderIndex = 3 }] },
                new Lesson { Title = "Draft lesson", OrderIndex = 1 }, new Lesson { Title = "Hidden lesson", Status = LessonStatus.Hidden, OrderIndex = 2 }] },
                new Module { Title = "Hidden module", Visibility = ModuleVisibility.Hidden, OrderIndex = 1,
                    Lessons = [new Lesson { Title = "Teacher preview only", Status = LessonStatus.Published, Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Hidden sample" }] }] }]
        };
        var paid = new Course { OwnerTeacherUserId = teacher.Id, Title = "Demo · Business English", Description = "Khóa trả phí mẫu; chưa tích hợp thanh toán.", IsPaid = true, Price = 199000, Status = CourseStatus.Published,
            Level = CourseLevel.Intermediate, Topics = [new CourseTopic { Topic = "Business" }], Skills = [new CourseSkill { Skill = EnglishSkill.Reading }],
            Modules = [new Module { Title = "At work", Lessons = [new Lesson { Title = "Business introductions", Status = LessonStatus.Published,
                Resources = [new LessonResource { ResourceType = LessonResourceType.Text, ContentText = "Good morning. Let me introduce our team." }] }] }] };
        db.Courses.AddRange(free, paid, new Course { OwnerTeacherUserId = teacher.Id, Title = "Demo · Draft chưa có nội dung" });
        db.Enrollments.Add(new Enrollment { Course = free, StudentUserId = student.Id });
        await db.SaveChangesAsync();

        async Task<ApplicationUser> User(string name, string role)
        {
            var email = name + "@demo.example.test";
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                var roles = await users.GetRolesAsync(existing);
                if (roles.Count != 1 || roles[0] != role) throw new InvalidOperationException("Demo account already exists with an unexpected role.");
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
}

namespace EnglishLearningPlatform.Infrastructure.Identity;

public static class AppRoles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";
    public static readonly string[] All = [Student, Teacher, Admin];
}

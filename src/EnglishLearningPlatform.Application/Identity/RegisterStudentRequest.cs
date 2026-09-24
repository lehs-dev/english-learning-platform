namespace EnglishLearningPlatform.Application.Identity;

public sealed record RegisterStudentRequest(
    string FullName,
    string Email,
    string Password
);

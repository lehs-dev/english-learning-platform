
namespace EnglishLearningPlatform.Application.Identity;

public interface IRegistrationService
{
    Task<RegisterStudentResult> RegisterStudentAsync(
        RegisterStudentRequest request
    );
}

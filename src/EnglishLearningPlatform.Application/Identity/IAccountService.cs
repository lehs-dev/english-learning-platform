
namespace EnglishLearningPlatform.Application.Identity;

public interface IAccountService
{
    Task<RegisterStudentResult> RegisterStudentAsync(
        RegisterStudentRequest request
    );
}

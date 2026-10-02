namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class QuestionWriteResult
{
    private QuestionWriteResult(Guid? questionId, IReadOnlyList<QuestionError> errors)
    {
        QuestionId = questionId;
        Errors = errors;
    }
    public bool Succeeded => QuestionId.HasValue;
    public Guid? QuestionId { get; }
    public IReadOnlyList<QuestionError> Errors { get; }
    public IReadOnlyList<QuestionError> GetErrors() => Errors;
    public static QuestionWriteResult Success(Guid questionId)
    {
        if(questionId == Guid.Empty)
        {
            throw new ArgumentException(
                "QuestionId cannot be empty.",
                nameof(questionId));
        }
        return new QuestionWriteResult(
            questionId, 
            Array.Empty<QuestionError>());
    }
    public static QuestionWriteResult Failure(
        IEnumerable<QuestionError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var snapshot = errors.ToArray();

        if (snapshot.Length == 0 ||
            snapshot.Any(error => error is null))
        {
            throw new ArgumentException(
                "Failure must contain at least one non-null error.",
                nameof(errors));
        }

        return new QuestionWriteResult(
            null,
            Array.AsReadOnly(snapshot));
    }
}
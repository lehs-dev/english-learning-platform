using System.ComponentModel.DataAnnotations;
using EnglishLearningPlatform.Application.Flashcards;

namespace EnglishLearningPlatform.Web.Models.Flashcards;

public sealed class FlashcardDeckInput
{
    [Required] public Guid CourseId { get; set; }
    public Guid? LessonId { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Description { get; set; }
    public SaveFlashcardDeckRequest ToRequest() => new(CourseId, LessonId, Title, Description);
}

public sealed class FlashcardCardInput
{
    [Required, StringLength(500)] public string FrontText { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string BackText { get; set; } = string.Empty;
    [StringLength(2000)] public string? Example { get; set; }
    [StringLength(200)] public string? Pronunciation { get; set; }
    [StringLength(2000)] public string? AudioResourceUrl { get; set; }
    public SaveFlashcardRequest ToRequest() => new(FrontText, BackText, Example, Pronunciation, AudioResourceUrl);
}

public sealed record FlashcardDeckForm(FlashcardDeckInput Input, IReadOnlyList<FlashcardCourseOption> Courses);
public sealed record TeacherFlashcardEditModel(TeacherFlashcardDeck Deck, IReadOnlyList<FlashcardCourseOption> Courses);

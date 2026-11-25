using EnglishLearningPlatform.Domain.Common;

namespace EnglishLearningPlatform.Domain.Entities;

public sealed class Flashcard : BaseEntity
{
    public Guid DeckId { get; set; }
    public FlashcardDeck Deck { get; set; } = null!;
    public string FrontText { get; set; } = string.Empty;
    public string BackText { get; set; } = string.Empty;
    public string? Example { get; set; }
    public string? Pronunciation { get; set; }
    public string? AudioResourceUrl { get; set; }
    public int OrderIndex { get; set; }
}

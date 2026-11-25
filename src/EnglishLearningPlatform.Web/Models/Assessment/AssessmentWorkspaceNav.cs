namespace EnglishLearningPlatform.Web.Models.Assessment;

public sealed record AssessmentWorkspaceNav(Guid Id, string Tab, bool CanEdit = true, bool CanPreview = true);

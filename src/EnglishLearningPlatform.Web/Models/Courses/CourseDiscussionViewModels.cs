using EnglishLearningPlatform.Application.Learning;

namespace EnglishLearningPlatform.Web.Models.Courses;

public sealed record CourseDiscussionInboxViewModel(CourseDiscussionInbox Inbox, Guid RequestId, string? Subject, string? Body);

public sealed record CourseDiscussionConversationViewModel(CourseDiscussionConversation Conversation, Guid RequestId, string? Body);

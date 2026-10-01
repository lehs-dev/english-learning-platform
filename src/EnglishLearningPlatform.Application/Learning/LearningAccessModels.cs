namespace EnglishLearningPlatform.Application.Learning;

public enum LearningResourceType { Course, Module, Lesson }

// ViewContent = xem nội dung đã enroll/preview của owner.
// ManageContent = quản lý nội dung của Teacher; RecordProgress = mutation của Student.
public enum LearningOperation { ViewContent, ManageContent, RecordProgress }

public enum LearningAccessResult { Allowed, Forbidden, NotFound }

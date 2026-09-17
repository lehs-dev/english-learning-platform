# ERD — MVP

> Đây là ERD nghiệp vụ. Bảng ASP.NET Core Identity (`AspNetUsers`, `AspNetRoles`, ...) do Identity tạo riêng; các cột `StudentUserId`/`TeacherUserId` tham chiếu logic tới `AspNetUsers.Id`.

```mermaid
erDiagram
    ASP_NET_USERS ||--o{ COURSE : teaches
    ASP_NET_USERS ||--o{ ENROLLMENT : enrolls
    ASP_NET_USERS ||--o{ LEARNING_PROGRESS : tracks
    ASP_NET_USERS ||--o{ QUESTION : authors
    ASP_NET_USERS ||--o{ EXAM_ATTEMPT : takes

    COURSE ||--o{ LESSON : contains
    COURSE ||--o{ ENROLLMENT : has
    COURSE ||--o{ EXAM : has
    LESSON ||--o{ LEARNING_PROGRESS : records

    QUESTION ||--o{ ANSWER_OPTION : has
    EXAM ||--o{ EXAM_QUESTION : contains
    QUESTION ||--o{ EXAM_QUESTION : included_in
    EXAM ||--o{ EXAM_ATTEMPT : receives
    EXAM_ATTEMPT ||--o{ STUDENT_ANSWER : contains
    QUESTION ||--o{ STUDENT_ANSWER : answered
    ANSWER_OPTION o|--o{ STUDENT_ANSWER : selected

    ASP_NET_USERS {
        uniqueidentifier Id PK
        nvarchar Email
        nvarchar DisplayName
        bit IsActive
    }
    COURSE {
        uniqueidentifier Id PK
        uniqueidentifier TeacherUserId FK
        nvarchar Title
        nvarchar Slug UK
        int Level
        bit IsPublished
    }
    LESSON {
        uniqueidentifier Id PK
        uniqueidentifier CourseId FK
        nvarchar Title
        int OrderNo
        nvarchar ContentMarkdown
        nvarchar MediaUrl
        bit IsPublished
    }
    ENROLLMENT {
        uniqueidentifier Id PK
        uniqueidentifier CourseId FK
        uniqueidentifier StudentUserId FK
        int Status
        datetimeoffset EnrolledAtUtc
    }
    LEARNING_PROGRESS {
        uniqueidentifier Id PK
        uniqueidentifier LessonId FK
        uniqueidentifier StudentUserId FK
        int Status
        datetimeoffset LastAccessedAtUtc
        datetimeoffset CompletedAtUtc
    }
    QUESTION {
        uniqueidentifier Id PK
        uniqueidentifier TeacherUserId FK
        nvarchar Prompt
        int Type
        int Skill
        int Difficulty
    }
    ANSWER_OPTION {
        uniqueidentifier Id PK
        uniqueidentifier QuestionId FK
        nvarchar Text
        bit IsCorrect
    }
    EXAM {
        uniqueidentifier Id PK
        uniqueidentifier CourseId FK
        nvarchar Title
        int DurationMinutes
        decimal PassingScorePercent
        int MaxAttempts
        bit IsPublished
    }
    EXAM_QUESTION {
        uniqueidentifier Id PK
        uniqueidentifier ExamId FK
        uniqueidentifier QuestionId FK
        decimal Points
        int OrderNo
    }
    EXAM_ATTEMPT {
        uniqueidentifier Id PK
        uniqueidentifier ExamId FK
        uniqueidentifier StudentUserId FK
        datetimeoffset StartedAtUtc
        datetimeoffset SubmittedAtUtc
        int Status
        decimal Score
        decimal MaxScore
    }
    STUDENT_ANSWER {
        uniqueidentifier Id PK
        uniqueidentifier ExamAttemptId FK
        uniqueidentifier QuestionId FK
        uniqueidentifier SelectedAnswerOptionId FK
        nvarchar TextAnswer
        bit IsCorrect
        decimal AwardedPoints
    }
```

## Ràng buộc quan trọng

- `Course.Slug` unique.
- Một student chỉ có một `Enrollment` cho mỗi course.
- Một student chỉ có một `LearningProgress` cho mỗi lesson.
- Một question chỉ xuất hiện một lần trong một exam.
- Một attempt chỉ có một `StudentAnswer` cho mỗi question.
- Xóa `Question` đã được dùng trong exam/answer nên bị `Restrict` để tránh mất lịch sử.

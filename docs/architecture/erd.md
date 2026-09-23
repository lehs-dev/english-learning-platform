# ERD — MVP

> Đây là ERD nghiệp vụ. Bảng ASP.NET Core Identity (`AspNetUsers`, `AspNetRoles`, ...) do Identity tạo riêng; các cột `StudentUserId`/`TeacherUserId` tham chiếu logic tới `AspNetUsers.Id`.

```mermaid
erDiagram
    USERS {
        bigint id PK
        string email UK
        string full_name
        string role
        string account_status
        datetime created_at
        datetime updated_at
    }

    COURSES {
        bigint id PK
        bigint owner_teacher_id FK
        bigint final_assessment_id FK "nullable"
        string title
        string description
        string objectives
        string level
        string status
        datetime created_at
        datetime updated_at
    }

    COURSE_SKILLS {
        bigint course_id PK, FK
        string skill PK
    }

    COURSE_TOPICS {
        bigint id PK
        bigint course_id FK
        string topic
    }

    MODULES {
        bigint id PK
        bigint course_id FK
        string title
        string description
        int order_index
        string visibility
        datetime created_at
        datetime updated_at
    }

    LESSONS {
        bigint id PK
        bigint module_id FK
        string title
        int order_index
        string status
        datetime created_at
        datetime updated_at
    }

    LESSON_RESOURCES {
        bigint id PK
        bigint lesson_id FK
        string resource_type
        string title "nullable"
        string content_text "nullable"
        string resource_url "nullable"
        int order_index
    }

    ENROLLMENTS {
        bigint id PK
        bigint student_id FK
        bigint course_id FK
        bigint last_accessed_lesson_id FK "nullable"
        datetime enrolled_at
        datetime completed_at "nullable"
    }

    LESSON_PROGRESS {
        bigint id PK
        bigint enrollment_id FK
        bigint lesson_id FK
        boolean is_completed
        datetime updated_at
    }

    STIMULI {
        bigint id PK
        bigint owner_teacher_id FK
        string stimulus_type
        string title "nullable"
        string content_text "nullable"
        string resource_url "nullable"
        datetime created_at
        datetime updated_at
    }

    QUESTIONS {
        bigint id PK
        bigint owner_teacher_id FK
        bigint stimulus_id FK "nullable"
        string content
        string primary_skill
        string level "nullable"
        string difficulty "nullable"
        string explanation "nullable"
        string status
        datetime created_at
        datetime updated_at
    }

    QUESTION_OPTIONS {
        bigint id PK
        bigint question_id FK
        string content
        int order_index
        boolean is_correct
    }

    PRACTICES {
        bigint id PK
        bigint owner_teacher_id FK
        bigint lesson_id FK "nullable"
        bigint module_id FK "nullable"
        string title
        string description "nullable"
        string status
        datetime created_at
        datetime updated_at
    }

    PRACTICE_QUESTIONS {
        bigint id PK
        bigint practice_id FK
        bigint question_id FK
        int order_index
    }

    PRACTICE_SUBMISSIONS {
        bigint id PK
        bigint student_id FK
        bigint practice_id FK
        decimal score
        datetime submitted_at
    }

    PRACTICE_ANSWERS {
        bigint id PK
        bigint practice_submission_id FK
        bigint practice_question_id FK
        bigint selected_option_id FK
    }

    ASSESSMENTS {
        bigint id PK
        bigint owner_teacher_id FK
        bigint course_id FK "nullable"
        string title
        string description "nullable"
        string assessment_type
        string target_skill "nullable"
        int duration_minutes
        int max_attempts
        decimal passing_score "nullable"
        string status
        datetime created_at
        datetime updated_at
    }

    ASSESSMENT_QUESTIONS {
        bigint id PK
        bigint assessment_id FK
        bigint question_id FK
        int order_index
        decimal points
    }

    ATTEMPTS {
        bigint id PK
        bigint student_id FK
        bigint assessment_id FK
        string status
        datetime started_at
        datetime deadline
        datetime finalized_at "nullable"
        string finalization_reason "nullable"
        decimal overall_score "nullable"
        boolean passed "nullable"
    }

    ATTEMPT_ANSWERS {
        bigint id PK
        bigint attempt_id FK
        bigint assessment_question_id FK
        bigint selected_option_id FK
        datetime saved_at
    }

    ATTEMPT_SKILL_RESULTS {
        bigint id PK
        bigint attempt_id FK
        string skill
        decimal score
        int question_count
    }

    AUDIT_LOGS {
        bigint id PK
        bigint actor_user_id FK "nullable"
        bigint target_user_id FK "nullable"
        string actor_snapshot
        string target_snapshot "nullable"
        string action
        string before_data "nullable"
        string after_data "nullable"
        datetime occurred_at
    }

    USERS ||--o{ COURSES : owns
    USERS ||--o{ STIMULI : owns
    USERS ||--o{ QUESTIONS : owns
    USERS ||--o{ PRACTICES : owns
    USERS ||--o{ ASSESSMENTS : owns

    USERS ||--o{ ENROLLMENTS : enrolls
    COURSES ||--o{ ENROLLMENTS : has
    LESSONS o|--o{ ENROLLMENTS : last_accessed

    COURSES ||--o{ COURSE_SKILLS : tagged_with
    COURSES ||--o{ COURSE_TOPICS : tagged_with

    COURSES ||--o{ MODULES : contains
    MODULES ||--o{ LESSONS : contains
    LESSONS ||--o{ LESSON_RESOURCES : contains

    ENROLLMENTS ||--o{ LESSON_PROGRESS : tracks
    LESSONS ||--o{ LESSON_PROGRESS : progress_for

    STIMULI o|--o{ QUESTIONS : supports
    QUESTIONS ||--|{ QUESTION_OPTIONS : has

    LESSONS o|--o{ PRACTICES : lesson_parent
    MODULES o|--o{ PRACTICES : module_parent
    PRACTICES ||--o{ PRACTICE_QUESTIONS : contains
    QUESTIONS ||--o{ PRACTICE_QUESTIONS : reused_in

    USERS ||--o{ PRACTICE_SUBMISSIONS : submits
    PRACTICES ||--o{ PRACTICE_SUBMISSIONS : receives
    PRACTICE_SUBMISSIONS ||--o{ PRACTICE_ANSWERS : contains
    PRACTICE_QUESTIONS ||--o{ PRACTICE_ANSWERS : answer_for
    QUESTION_OPTIONS ||--o{ PRACTICE_ANSWERS : selected_option

    COURSES o|--o{ ASSESSMENTS : course_scope
    COURSES o|--o| ASSESSMENTS : final_assessment
    ASSESSMENTS ||--o{ ASSESSMENT_QUESTIONS : contains
    QUESTIONS ||--o{ ASSESSMENT_QUESTIONS : reused_in

    USERS ||--o{ ATTEMPTS : starts
    ASSESSMENTS ||--o{ ATTEMPTS : receives
    ATTEMPTS ||--o{ ATTEMPT_ANSWERS : contains
    ASSESSMENT_QUESTIONS ||--o{ ATTEMPT_ANSWERS : answer_for
    QUESTION_OPTIONS ||--o{ ATTEMPT_ANSWERS : selected_option
    ATTEMPTS ||--o{ ATTEMPT_SKILL_RESULTS : produces

    USERS o|--o{ AUDIT_LOGS : actor
    USERS o|--o{ AUDIT_LOGS : target
```

## Ràng buộc toàn vẹn đi kèm ERD v1

> ERD trên mô tả quan hệ và thuộc tính chính. Các ràng buộc nhiều bảng, ràng buộc trạng thái và thao tác đồng thời sau đây là một phần của thiết kế dữ liệu/nghiệp vụ; FK đơn lẻ không đủ để bảo đảm chúng.

| Nhóm dữ liệu | Ràng buộc cần thực thi |
| --- | --- |
| `ENROLLMENTS` | Unique (`student_id`, `course_id`). User phải có quyền Student khi tạo Enrollment; role đổi sau này không xóa lịch sử. |
| `LESSON_PROGRESS` | Unique (`enrollment_id`, `lesson_id`); Lesson phải thuộc đúng Course của Enrollment. `last_accessed_lesson_id` cũng phải thuộc Course đó. |
| `PRACTICES` | Không đồng thời có `lesson_id` và `module_id`; `Published` bắt buộc có đúng một parent thuộc Course của Owner, ≥1 Question khác nhau; bản Draft chưa gắn parent thì không khả dụng. `Unpublished` không được Start/Submit. |
| `PRACTICE_QUESTIONS`, `ASSESSMENT_QUESTIONS` | Unique (`practice_id`, `question_id`) và unique (`assessment_id`, `question_id`); Question cùng Owner với Practice/Assessment. Mỗi Question trong Assessment có `points > 0`. |
| `PRACTICE_SUBMISSIONS` | Unique (`student_id`, `practice_id`): chỉ một kết quả gần nhất. Khi làm lại, thay score và bộ `PRACTICE_ANSWERS` tương ứng một cách nhất quán. |
| `ATTEMPT_ANSWERS`, `PRACTICE_ANSWERS` | Unique (`attempt_id`, `assessment_question_id`) và unique (`practice_submission_id`, `practice_question_id`); câu phải thuộc đúng Assessment/Practice của lần làm, Option chọn phải thuộc đúng Question đó. Câu bỏ trống không cần tạo Answer row. |
| `ATTEMPT_SKILL_RESULTS` | Unique (`attempt_id`, `skill`); chỉ sử dụng nhóm đủ ngưỡng theo TBR08 để phân loại điểm yếu/gợi ý. |
| `COURSES.final_assessment_id` | Khi Course chưa Archived, Final phải trỏ tới Assessment `Published` thuộc **chính Course** và cùng Owner, có Type/PassingScore hợp lệ. Với Course Archived, Final lịch sử có thể Archived theo TBR14. Trước khi đổi `ASSESSMENTS.course_id` phải kiểm tra Course nào đang trỏ tới nó làm Final. |
| `QUESTIONS`, `QUESTION_OPTIONS`, `STIMULI` | Một Question phải có ≥2 Option, đúng một Option đúng; Stimulus phải cùng Owner với Question. Khi Question từng được Published hoặc có dữ liệu làm bài, nội dung/Option/Stimulus liên quan bất biến theo TBR05; tài nguyên audio không được ghi đè tại cùng URL. |
| `ATTEMPTS` | Start, kiểm tra `MaxAttempts`, xử lý Attempt quá Deadline, giới hạn một Attempt còn hiệu lực và finalize/chấm một lần phải nhất quán khi nhiều request cùng lúc. `ATTEMPT_ANSWERS` phải thuộc đúng Attempt còn hiệu lực. |
| `USERS`, `AUDIT_LOGS` | Kiểm tra Active Admin cuối cùng khi cập nhật đồng thời. Hard-delete User hợp lệ thì FK actor/target của Audit được để null, giữ `actor_snapshot`/`target_snapshot` và thời điểm; không xóa dây chuyền Audit Log. |

Các điều kiện cùng Course, cùng Question, trạng thái Published và cập nhật đồng thời cần được kiểm tra trong thao tác nghiệp vụ tương ứng; chỉ ghi FK trong ERD không thể hiện đầy đủ các điều kiện này.

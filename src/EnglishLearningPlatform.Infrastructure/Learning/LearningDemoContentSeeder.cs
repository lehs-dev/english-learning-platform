using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

/// <summary>
/// Adds a larger, repeatable catalog for local UI exploration. This is only
/// called by the explicit Development demo seed; existing rows are never reset.
/// </summary>
internal static class LearningDemoContentSeeder
{
    private const string ReadingStimulusTitle = "Demo · Reading passage: Mia's weekend";
    private const string ListeningStimulusTitle = "Demo · Listening: City Library";

    public static async Task SeedAsync(AppDbContext db, Guid teacherId, Guid studentId,
        IStimulusAudioStore audioStore)
    {
        var courses = await EnsureCoursesAsync(db, teacherId);
        await db.SaveChangesAsync();

        var questions = await EnsureQuestionBankAsync(db, teacherId, audioStore);
        await db.SaveChangesAsync();

        // Give the main demo learner useful access to the free catalog. Keep
        // every paid course un-enrolled so checkout and access denial remain testable.
        foreach (var course in courses.Where(course => !course.IsPaid && course.Status == CourseStatus.Published))
        {
            if (!await db.Enrollments.AnyAsync(enrollment => enrollment.CourseId == course.Id
                    && enrollment.StudentUserId == studentId))
            {
                db.Enrollments.Add(new Enrollment { CourseId = course.Id, StudentUserId = studentId });
            }
        }

        foreach (var course in courses)
        {
            foreach (var module in course.Modules.Where(module => module.Visibility == ModuleVisibility.Visible))
            {
                await EnsurePracticeAsync(db, teacherId, module.Id, null,
                    $"Demo · Checkpoint: {module.Title}",
                    PickQuestions(questions, module.OrderIndex * 3 + 1, 5));

                foreach (var lesson in module.Lessons.Where(lesson => lesson.Status == LessonStatus.Published))
                {
                    await EnsurePracticeAsync(db, teacherId, null, lesson.Id,
                        $"Demo · Quick Check: {lesson.Title}",
                        PickQuestions(questions, lesson.OrderIndex + module.OrderIndex * 2, 5));
                }
            }

            await EnsureCourseAssessmentsAsync(db, teacherId, course, questions);
        }

        var starterCourse = courses.SingleOrDefault(course => course.Title == "Demo · English Starter A1");
        var starterModule = starterCourse?.Modules.OrderBy(module => module.OrderIndex).FirstOrDefault();
        if (starterModule is not null)
        {
            await EnsurePracticeAsync(db, teacherId, starterModule.Id, null,
                "Demo · Draft Practice: Teacher preview sandbox", PickQuestions(questions, 5, 5),
                PracticeStatus.Draft);
        }

        await EnsureIndependentAssessmentsAsync(db, teacherId, questions);
        await db.SaveChangesAsync();
    }

    private static async Task<List<Course>> EnsureCoursesAsync(AppDbContext db, Guid teacherId)
    {
        var definitions = DemoCourses();
        var courses = new List<Course>(definitions.Length);
        foreach (var definition in definitions)
        {
            var course = await db.Courses
                .Include(item => item.Modules)
                    .ThenInclude(module => module.Lessons)
                .SingleOrDefaultAsync(item => item.OwnerTeacherUserId == teacherId
                    && item.Title == definition.Title);
            if (course is null)
            {
                course = new Course
                {
                    OwnerTeacherUserId = teacherId,
                    Title = definition.Title,
                    Description = definition.Description,
                    Objectives = definition.Objectives,
                    Level = definition.Level,
                    Status = CourseStatus.Published,
                    IsPaid = definition.IsPaid,
                    Price = definition.IsPaid ? 299000m : 0m,
                    Topics = definition.Topics.Select(topic => new CourseTopic { Topic = topic }).ToList(),
                    Skills = definition.Skills.Select(skill => new CourseSkill { Skill = skill }).ToList(),
                    Modules = definition.Modules.Select((module, moduleIndex) => new Module
                    {
                        Title = module.Title,
                        Description = module.Description,
                        OrderIndex = moduleIndex,
                        Visibility = ModuleVisibility.Visible,
                        Lessons = module.Lessons.Select((lesson, lessonIndex) => new Lesson
                        {
                            Title = lesson.Title,
                            Status = LessonStatus.Published,
                            OrderIndex = lessonIndex,
                            Resources =
                            [
                                new LessonResource
                                {
                                    Title = "Lesson notes",
                                    ResourceType = LessonResourceType.Text,
                                    ContentText = lesson.Content
                                }
                            ]
                        }).ToList()
                    }).ToList()
                };
                db.Courses.Add(course);
            }

            courses.Add(course);
        }

        // Include the original sample course in the practice/assessment catalog.
        var originalCourse = await db.Courses
            .Include(item => item.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleOrDefaultAsync(item => item.OwnerTeacherUserId == teacherId
                && item.Title == "Demo · Everyday English");
        if (originalCourse is not null)
            courses.Insert(0, originalCourse);

        return courses;
    }

    private static async Task<List<Question>> EnsureQuestionBankAsync(AppDbContext db, Guid teacherId,
        IStimulusAudioStore audioStore)
    {
        var audioBytes = ReadEmbeddedListeningAudio();
        var audioUrl = await audioStore.StoreAsync(audioBytes);

        var readingStimulus = await db.Stimuli.SingleOrDefaultAsync(stimulus =>
            stimulus.OwnerTeacherUserId == teacherId && stimulus.Title == ReadingStimulusTitle);
        if (readingStimulus is null)
        {
            readingStimulus = new Stimulus
            {
                OwnerTeacherUserId = teacherId,
                StimulusType = StimulusType.Passage,
                Title = ReadingStimulusTitle,
                ContentText = "Mia lives near the town centre. On weekdays, she walks to work and buys a coffee at the station. " +
                    "Last Saturday, she took a bus to the market, bought apples and bread, and met her friend Leo at noon. " +
                    "They planned to visit the museum the following weekend."
            };
            readingStimulus.LockContent(DateTimeOffset.UtcNow);
            db.Stimuli.Add(readingStimulus);
        }

        var listeningStimulus = await db.Stimuli.SingleOrDefaultAsync(stimulus =>
            stimulus.OwnerTeacherUserId == teacherId && stimulus.Title == ListeningStimulusTitle);
        if (listeningStimulus is null)
        {
            listeningStimulus = new Stimulus
            {
                OwnerTeacherUserId = teacherId,
                StimulusType = StimulusType.Audio,
                Title = ListeningStimulusTitle,
                ResourceUrl = audioUrl
            };
            listeningStimulus.LockContent(DateTimeOffset.UtcNow);
            db.Stimuli.Add(listeningStimulus);
        }
        await db.SaveChangesAsync();

        var definitions = QuestionBank(readingStimulus.Id, listeningStimulus.Id);
        var questions = new List<Question>(definitions.Length);
        foreach (var definition in definitions)
        {
            var question = await db.Questions
                .Include(item => item.Options)
                .Include(item => item.Stimulus)
                .SingleOrDefaultAsync(item => item.OwnerTeacherUserId == teacherId
                    && item.Content == definition.Content);
            if (question is null)
            {
                question = new Question
                {
                    OwnerTeacherUserId = teacherId,
                    Content = definition.Content,
                    PrimarySkill = definition.Skill,
                    Level = definition.Level,
                    Explanation = definition.Explanation,
                    Status = QuestionStatus.Published,
                    StimulusId = definition.StimulusId,
                    Stimulus = definition.StimulusId == readingStimulus.Id ? readingStimulus
                        : definition.StimulusId == listeningStimulus.Id ? listeningStimulus : null,
                    Options = definition.Options.Select((option, index) => new QuestionOption
                    {
                        Content = option,
                        IsCorrect = index == definition.CorrectOption,
                        OrderIndex = index
                    }).ToList()
                };
                question.LockContent(DateTimeOffset.UtcNow);
                db.Questions.Add(question);
            }

            questions.Add(question);
        }

        var draftQuestions = new[]
        {
            new QuestionSeed("Demo draft: Which sentence is a polite request?", EnglishSkill.Grammar, null,
                "Choose the sentence that uses could and please appropriately.",
                ["Could you send me the schedule, please?", "You send schedule now.", "Send schedule could.", "Please you schedule sent."], 0),
            new QuestionSeed("Demo draft: A person who designs buildings is an ___.", EnglishSkill.Vocabulary, null,
                "An architect designs buildings.", ["architect", "accountant", "assistant", "athlete"], 0),
            new QuestionSeed("Demo draft: What are Mia and Leo planning to do next weekend?", EnglishSkill.Reading, readingStimulus.Id,
                "Mia and Leo planned to visit the museum the following weekend.", ["Visit the museum", "Take a train", "Go to the market", "Meet at the station"], 0),
            new QuestionSeed("Demo draft: What should visitors bring to the library?", EnglishSkill.Listening, listeningStimulus.Id,
                "The announcement asks visitors to bring a library card.", ["A notebook", "A photo ID", "A library card", "A laptop"], 2)
        };
        foreach (var definition in draftQuestions)
        {
            if (await db.Questions.AnyAsync(question => question.OwnerTeacherUserId == teacherId
                    && question.Content == definition.Content))
                continue;

            db.Questions.Add(new Question
            {
                OwnerTeacherUserId = teacherId,
                Content = definition.Content,
                PrimarySkill = definition.Skill,
                Level = definition.Level,
                Explanation = definition.Explanation,
                Status = QuestionStatus.Draft,
                StimulusId = definition.StimulusId,
                Options = definition.Options.Select((option, index) => new QuestionOption
                {
                    Content = option,
                    IsCorrect = index == definition.CorrectOption,
                    OrderIndex = index
                }).ToList()
            });
        }

        return questions;
    }

    private static async Task EnsurePracticeAsync(AppDbContext db, Guid teacherId, Guid? moduleId, Guid? lessonId,
        string title, IReadOnlyList<Question> questions, PracticeStatus status = PracticeStatus.Published)
    {
        if (await db.Practices.AnyAsync(practice => practice.OwnerTeacherUserId == teacherId
                && practice.Title == title))
            return;

        var practice = new Practice
        {
            OwnerTeacherUserId = teacherId,
            ModuleId = moduleId,
            LessonId = lessonId,
            Title = title,
            Description = "Bài luyện mẫu để thử làm bài, chấm điểm và xem giải thích.",
            Status = status,
            Questions = questions.Select((question, index) => new PracticeQuestion
            {
                QuestionId = question.Id,
                Question = question,
                OrderIndex = index
            }).ToList()
        };
        db.Practices.Add(practice);
    }

    private static async Task EnsureCourseAssessmentsAsync(AppDbContext db, Guid teacherId, Course course,
        IReadOnlyList<Question> questions)
    {
        var titlePrefix = $"Demo · Assessment · {course.Title}";
        var mixedQuestions = PickQuestions(questions, course.Modules.Count + 2, 12);
        await EnsureAssessmentAsync(db, teacherId, course.Id, titlePrefix + " · Mixed Skills",
            "Bài đánh giá tổng hợp gắn với khóa học; bao gồm Reading, Listening, Vocabulary và Grammar.",
            AssessmentType.PracticeExam, null, 25, 3, 60m, mixedQuestions);
        await EnsureAssessmentAsync(db, teacherId, course.Id, titlePrefix + " · Listening",
            "Bài đánh giá kỹ năng Listening với audio lưu nội bộ.",
            AssessmentType.SkillAssessment, EnglishSkill.Listening, 15, 3, 60m,
            questions.Where(question => question.PrimarySkill == EnglishSkill.Listening).Take(5).ToArray());
    }

    private static async Task EnsureIndependentAssessmentsAsync(AppDbContext db, Guid teacherId,
        IReadOnlyList<Question> questions)
    {
        var mixed = PickQuestions(questions, 1, 15);
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Placement Test A1",
            "Bài xác định điểm xuất phát: câu hỏi cơ bản theo bốn kỹ năng.",
            AssessmentType.PlacementTest, null, 20, 3, null, mixed);
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Placement Test A2–B1",
            "Bài xác định trình độ tổng quát với nội dung từ cơ bản đến trung cấp.",
            AssessmentType.PlacementTest, null, 30, 3, null, PickQuestions(questions, 4, 20));
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Skill Assessment Reading",
            "Đánh giá kỹ năng đọc hiểu.", AssessmentType.SkillAssessment, EnglishSkill.Reading,
            15, 3, 60m, BySkill(questions, EnglishSkill.Reading));
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Skill Assessment Listening",
            "Đánh giá kỹ năng nghe với audio mẫu có thể phát trực tiếp.",
            AssessmentType.SkillAssessment, EnglishSkill.Listening, 15, 3, 60m,
            BySkill(questions, EnglishSkill.Listening));
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Skill Assessment Vocabulary",
            "Đánh giá vốn từ vựng trong ngữ cảnh.", AssessmentType.SkillAssessment,
            EnglishSkill.Vocabulary, 15, 3, 60m, BySkill(questions, EnglishSkill.Vocabulary));
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Skill Assessment Grammar",
            "Đánh giá ngữ pháp tiếng Anh.", AssessmentType.SkillAssessment,
            EnglishSkill.Grammar, 15, 3, 60m, BySkill(questions, EnglishSkill.Grammar));
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Mock Exam B1",
            "Đề luyện tổng hợp có thời gian và trọng số câu hỏi.", AssessmentType.PracticeExam,
            null, 35, 3, 65m, PickQuestions(questions, 7, 20));

        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Draft Assessment",
            "Bản nháp mẫu để thử chỉnh cấu hình và thêm câu hỏi.", AssessmentType.PracticeExam,
            null, 20, 2, 60m, [], AssessmentStatus.Draft);
        await EnsureAssessmentAsync(db, teacherId, null, "Demo · Unpublished Assessment",
            "Đề đã gỡ khỏi danh sách Student nhưng vẫn có thể quản lý ở Teacher.",
            AssessmentType.PracticeExam, null, 20, 2, 60m, PickQuestions(questions, 3, 5),
            AssessmentStatus.Unpublished);
    }

    private static async Task EnsureAssessmentAsync(AppDbContext db, Guid teacherId, Guid? courseId, string title,
        string description, AssessmentType type, EnglishSkill? targetSkill, int durationMinutes, int maxAttempts,
        decimal? passingScore, IReadOnlyList<Question> questions, AssessmentStatus status = AssessmentStatus.Published)
    {
        if (await db.Assessments.AnyAsync(assessment => assessment.OwnerTeacherUserId == teacherId
                && assessment.Title == title))
            return;

        var assessment = new Assessment
        {
            OwnerTeacherUserId = teacherId,
            CourseId = courseId,
            Title = title,
            Description = description,
            AssessmentType = type,
            TargetSkill = targetSkill,
            DurationMinutes = durationMinutes,
            MaxAttempts = maxAttempts,
            PassingScore = passingScore,
            Status = status,
            Questions = questions.Select((question, index) => new AssessmentQuestion
            {
                QuestionId = question.Id,
                Question = question,
                OrderIndex = index,
                Points = index % 3 == 0 ? 2m : 1m
            }).ToList()
        };
        db.Assessments.Add(assessment);
    }

    private static IReadOnlyList<Question> BySkill(IReadOnlyList<Question> questions, EnglishSkill skill) =>
        questions.Where(question => question.PrimarySkill == skill).Take(5).ToArray();

    private static IReadOnlyList<Question> PickQuestions(IReadOnlyList<Question> questions, int offset, int count) =>
        Enumerable.Range(0, Math.Min(count, questions.Count))
            .Select(index => questions[(index + offset) % questions.Count])
            .DistinctBy(question => question.Id)
            .ToArray();

    private static byte[] ReadEmbeddedListeningAudio()
    {
        using var stream = typeof(LearningDemoContentSeeder).Assembly
            .GetManifestResourceStream("EnglishLearningPlatform.Infrastructure.DemoContent.city-library-listening.wav")
            ?? throw new InvalidOperationException("Embedded demo listening audio was not found.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static CourseDefinition[] DemoCourses() =>
    [
        new(
            "Demo · English Starter A1",
            "Khóa nền tảng miễn phí với bài học ngắn, bài luyện theo bài/chương và đánh giá cuối chủ đề.",
            "Giới thiệu bản thân, nói về lịch sinh hoạt và hiểu thông tin quen thuộc.",
            CourseLevel.Beginner, false,
            ["English basics", "Everyday life"], [EnglishSkill.Grammar, EnglishSkill.Vocabulary, EnglishSkill.Listening],
            [
                new("First conversations", "Chào hỏi và trao đổi thông tin cá nhân.",
                    [new("Greetings", "Use hello, good morning and polite introductions in a short conversation."),
                     new("Names and countries", "Ask and answer questions about names, countries and languages."),
                     new("Numbers and contact details", "Practise numbers, phone numbers and spelling an email address.")]),
                new("Daily routines", "Mô tả thói quen và lịch trình hàng ngày.",
                    [new("A weekday routine", "Describe a normal weekday with present simple verbs."),
                     new("Days and times", "Understand days of the week, clock times and simple schedules."),
                     new("At home", "Learn common household words and describe where things are.")]),
                new("Around town", "Từ vựng và hội thoại tại những địa điểm quen thuộc.",
                    [new("Places in town", "Identify common places such as the station, market and library."),
                     new("Asking for directions", "Ask where a place is and follow simple directions."),
                     new("A short weekend plan", "Read a short message about a plan and find its key details.")])
            ]),
        new(
            "Demo · English at Work A2",
            "Khóa miễn phí về giao tiếp cơ bản tại nơi làm việc, có cả bài nghe và đề đánh giá theo kỹ năng.",
            "Viết tin nhắn công việc ngắn, hiểu lịch họp và giao tiếp lịch sự với đồng nghiệp.",
            CourseLevel.Elementary, false,
            ["Work", "Communication"], [EnglishSkill.Reading, EnglishSkill.Listening, EnglishSkill.Vocabulary, EnglishSkill.Grammar],
            [
                new("Workplace introductions", "Giới thiệu bản thân và trao đổi công việc.",
                    [new("Introducing your role", "Explain your role, team and main responsibilities."),
                     new("Polite requests", "Use can, could and please to make a polite request."),
                     new("Making arrangements", "Arrange a time and place for a short meeting.")]),
                new("Meetings and messages", "Đọc lịch họp và hiểu thông báo nội bộ.",
                    [new("Meeting schedules", "Find the date, time and room in a meeting notice."),
                     new("Email essentials", "Recognise the purpose and action in a short work email."),
                     new("Taking a message", "Listen for a caller's name, number and requested action.")]),
                new("Customer conversations", "Giao tiếp lịch sự và giải quyết yêu cầu đơn giản.",
                    [new("Welcoming a visitor", "Greet a visitor and offer basic help."),
                     new("Understanding a request", "Identify what a customer needs and when they need it."),
                     new("Confirming the details", "Repeat key information to make sure an arrangement is correct.")])
            ]),
        new(
            "Demo · Reading & Listening B1",
            "Khóa trả phí mẫu để thử checkout, quyền truy cập sau thanh toán và nội dung trình độ trung cấp.",
            "Đọc thông báo dài hơn, nghe lấy thông tin chính và sử dụng từ vựng trong bối cảnh học tập/công việc.",
            CourseLevel.Intermediate, true,
            ["Study", "Travel", "B1 English"], [EnglishSkill.Reading, EnglishSkill.Listening, EnglishSkill.Vocabulary, EnglishSkill.Grammar],
            [
                new("Travel planning", "Lập kế hoạch chuyến đi và xử lý thay đổi lịch trình.",
                    [new("Comparing transport", "Compare journey time, price and convenience."),
                     new("Hotel information", "Understand check-in details and common hotel policies."),
                     new("A change of plan", "Follow a spoken update when a departure time changes.")]),
                new("Study and research", "Đọc thông tin học tập và tóm tắt ý chính.",
                    [new("Finding reliable information", "Distinguish a main idea from supporting details."),
                     new("Note-taking", "Choose the key details to include in brief notes."),
                     new("A short campus announcement", "Listen for a location, a time and the next action.")]),
                new("English in context", "Ôn tập từ vựng, ngữ pháp và đọc hiểu B1.",
                    [new("Phrasal verbs at work", "Understand common phrasal verbs in work situations."),
                     new("Past experiences", "Use past forms to describe an experience and its result."),
                     new("Reading for detail", "Scan a short passage to locate names, dates and reasons.")])
            ])
    ];

    private static QuestionSeed[] QuestionBank(Guid readingStimulusId, Guid listeningStimulusId) =>
    [
        new("She usually ___ to the office by bus.", EnglishSkill.Grammar, null,
            "Use the present simple with she/he/it.", ["go", "goes", "going", "gone"], 1),
        new("They ___ dinner when I called last night.", EnglishSkill.Grammar, null,
            "Use the past continuous for an action in progress at that time.", ["have", "had", "were having", "are having"], 2),
        new("I have worked here ___ 2022.", EnglishSkill.Grammar, null,
            "Use since with a starting point in time.", ["for", "since", "during", "from"], 1),
        new("If it ___ tomorrow, we will take the train.", EnglishSkill.Grammar, null,
            "In the first conditional, use the present simple after if.", ["rains", "will rain", "rained", "is raining"], 0),
        new("You ___ wear a seat belt in a car.", EnglishSkill.Grammar, null,
            "Must expresses a strong rule or obligation.", ["might", "must", "would", "could"], 1),
        new("The meeting was postponed, so it will happen ___ than planned.", EnglishSkill.Vocabulary, null,
            "Postponed means moved to a later time.", ["earlier", "later", "nearer", "faster"], 1),
        new("A person who designs buildings is an ___.", EnglishSkill.Vocabulary, null,
            "An architect plans and designs buildings.", ["architect", "accountant", "engineer", "assistant"], 0),
        new("Please ___ the form before Friday.", EnglishSkill.Vocabulary, null,
            "Complete a form means fill in all required information.", ["complete", "collect", "connect", "compare"], 0),
        new("The train was crowded, so there were no ___ seats.", EnglishSkill.Vocabulary, null,
            "Available means ready for someone to use.", ["available", "private", "nervous", "ordinary"], 0),
        new("Mia bought fruit at the market because it was fresh and ___.", EnglishSkill.Vocabulary, null,
            "Reasonable describes a fair or acceptable price.", ["reasonable", "silent", "narrow", "careless"], 0),
        new("Where does Mia live?", EnglishSkill.Reading, readingStimulusId,
            "The first sentence says she lives near the town centre.", ["Near the town centre", "Next to the museum", "At the station", "Beside the market"], 0),
        new("How does Mia travel to work on weekdays?", EnglishSkill.Reading, readingStimulusId,
            "The passage says that she walks to work.", ["By bus", "By train", "On foot", "By bicycle"], 2),
        new("What did Mia buy last Saturday?", EnglishSkill.Reading, readingStimulusId,
            "She bought apples and bread at the market.", ["A book and a map", "Apples and bread", "Coffee and a ticket", "Flowers and cheese"], 1),
        new("When did Mia meet Leo?", EnglishSkill.Reading, readingStimulusId,
            "The passage says they met at noon.", ["At 9 a.m.", "At noon", "In the evening", "On Sunday morning"], 1),
        new("Where are Mia and Leo planning to go next weekend?", EnglishSkill.Reading, readingStimulusId,
            "Their plan is to visit the museum.", ["The library", "The market", "The museum", "The station"], 2),
        new("What time does City Library open from Monday to Friday?", EnglishSkill.Listening, listeningStimulusId,
            "The announcement says nine o'clock on weekdays.", ["8:00", "9:00", "9:30", "10:00"], 1),
        new("What time does the library open on Saturday?", EnglishSkill.Listening, listeningStimulusId,
            "On Saturday it opens at ten.", ["9:00", "9:30", "10:00", "11:00"], 2),
        new("Where does the reading club meet?", EnglishSkill.Listening, listeningStimulusId,
            "The club meets in Room Two.", ["Room One", "Room Two", "The main hall", "The cafe"], 1),
        new("What time does the reading club meet?", EnglishSkill.Listening, listeningStimulusId,
            "Half past four is 4:30.", ["4:00", "4:15", "4:30", "5:00"], 2),
        new("What should visitors bring to the library?", EnglishSkill.Listening, listeningStimulusId,
            "The announcement asks visitors to bring a library card.", ["A notebook", "A photo ID", "A library card", "A laptop"], 2)
    ];

    private sealed record QuestionSeed(string Content, EnglishSkill Skill, Guid? StimulusId,
        string Explanation, string[] Options, int CorrectOption, string Level = "Intermediate");

    private sealed record LessonDefinition(string Title, string Content);
    private sealed record ModuleDefinition(string Title, string Description, LessonDefinition[] Lessons);
    private sealed record CourseDefinition(string Title, string Description, string Objectives, CourseLevel Level,
        bool IsPaid, string[] Topics, EnglishSkill[] Skills, ModuleDefinition[] Modules);
}

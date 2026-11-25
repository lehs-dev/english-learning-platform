using System.Data;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Practice;
using EnglishLearningPlatform.Application.Stimuli;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Gamification;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishLearningPlatform.Infrastructure.Learning;

internal static class LearningDemoWorkshopSeeder
{
    private static readonly Guid WorkshopId = Guid.Parse("629c2a6d-fbd1-46a1-ac8e-d2ec0b7d8da8");
    private static readonly Guid OrderMessageAssignmentId = Guid.Parse("ed8f3030-8395-47ef-9ea4-70f73e7db1ce");
    private const string CourseTitle = "Tiếng Anh tại quán cà phê";
    private const string Transcript = "Could I have an iced coffee to go, please?";

    public static async Task SeedAsync(AppDbContext db, Guid teacherId, Guid studentId, IStimulusAudioStore audioStore)
    {
        // The workshop and its histories form one seed. A stable ID keeps renamed or edited demo data intact.
        var audioUrl = await StoreAudioAsync(audioStore);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var now = DateTimeOffset.UtcNow;
        var existingStatus = await db.CoursesForUpdate(WorkshopId)
            .Select(course => (CourseStatus?)course.Status).SingleOrDefaultAsync();
        if (existingStatus.HasValue)
        {
            if (existingStatus is CourseStatus.Published or CourseStatus.Unpublished &&
                !await db.WritingAssignments.AnyAsync(assignment => assignment.Id == OrderMessageAssignmentId))
            {
                db.WritingAssignments.Add(OrderMessageAssignment(now));
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return;
        }

        var today = GamificationPolicy.LocalDate(now);
        DateTimeOffset Morning(int daysAgo) => GamificationPolicy.LocalDayStartUtc(today.AddDays(-daysAgo)).AddHours(10);
        var created = Morning(12);

        var lessons = new[]
        {
            LessonAt(0, "Chào hỏi và gọi món",
                """
                Dùng “Could I have …, please?” khi gọi món lịch sự.

                Barista: Hi! What can I get you?
                Customer: Could I have an iced coffee, please?
                Barista: Of course. What size would you like?
                Customer: A small one, please.

                Thử thay iced coffee bằng tea hoặc hot chocolate.
                """, created),
            LessonAt(1, "Uống tại quán hay mang đi?",
                """
                “For here” nghĩa là dùng tại quán. “To go” nghĩa là mang đi.

                Barista: Is that for here or to go?
                Customer: To go, please.

                Để gọi một món mang đi trong một câu, đặt “to go” sau tên món.
                """, created),
            LessonAt(2, "Chọn sữa và mức đường",
                """
                Dùng “with” để yêu cầu thêm gì đó và “without” để bỏ một thành phần.

                Customer: Could I have it with oat milk and without sugar, please?
                Barista: Sure. Would you like anything else?
                Customer: No, thank you.
                """, created),
            LessonAt(0, "Hỏi giá và thanh toán",
                """
                “How much is it?” dùng để hỏi giá. “Can I pay by card?” là hỏi có thể trả bằng thẻ không.

                Barista: That’s four fifty.
                Customer: Can I pay by card?
                Barista: Yes, just tap here.
                """, created),
            LessonAt(1, "Nhờ sửa món bị nhầm",
                """
                Mở đầu bằng “Excuse me” để được chú ý, rồi nói bạn đã gọi gì.

                Customer: Excuse me, I ordered an iced coffee, not a hot one.
                Barista: I’m sorry. I’ll make a new one right away.
                Customer: Thank you!
                """, created),
            LessonAt(2, "Thực hành một lượt gọi món",
                """
                Giờ hãy tự đóng vai khách. Chào, chọn món và cỡ ly, nói bạn uống tại quán hay mang đi, rồi hỏi cách thanh toán.

                Mẫu bắt đầu: “Hi! Could I have a small …, please?”
                Tự đổi hai chi tiết trong câu để luyện lại.
                """, created)
        };
        var course = new Course
        {
            Id = WorkshopId,
            OwnerTeacherUserId = teacherId,
            Title = CourseTitle,
            Description = "Luyện gọi món, thanh toán và xử lý nhầm món bằng hội thoại ngắn.",
            Objectives = "Gọi đồ uống lịch sự, nói rõ yêu cầu và trao đổi khi cần sửa món.",
            Level = CourseLevel.Beginner,
            Status = CourseStatus.Published,
            CreatedAtUtc = created,
            Skills = [new CourseSkill { Skill = EnglishSkill.Listening }, new CourseSkill { Skill = EnglishSkill.Vocabulary },
                new CourseSkill { Skill = EnglishSkill.Grammar }],
            Topics = [new CourseTopic { Topic = "Giao tiếp hằng ngày" }],
            Modules =
            [
                new Module { Title = "Gọi món", Visibility = ModuleVisibility.Visible, OrderIndex = 0,
                    CreatedAtUtc = created, Lessons = [..lessons.Take(3)] },
                new Module { Title = "Thanh toán và xử lý nhầm món", Visibility = ModuleVisibility.Visible, OrderIndex = 1,
                    CreatedAtUtc = created, Lessons = [..lessons.Skip(3)] }
            ]
        };
        var enrollment = new Enrollment
        {
            Course = course,
            StudentUserId = studentId,
            CreatedAtUtc = Morning(9),
            EnrolledAtUtc = Morning(9),
            LastAccessedLesson = lessons[3],
            LessonProgressEntries = lessons.Take(3).Select((lesson, index) => new LessonProgress
            {
                Lesson = lesson, IsCompleted = true,
                CreatedAtUtc = Morning(5 - index), UpdatedAtUtc = Morning(5 - index)
            }).ToList()
        };
        db.Enrollments.Add(enrollment);

        var practice = BuildPractice(teacherId, lessons[1], audioUrl, created);
        db.Practices.Add(practice);
        var deck = BuildDeck(teacherId, course, lessons[1], created);
        db.FlashcardDecks.Add(deck);
        db.WritingAssignments.AddRange(
            WritingTask(course, studentId, "Viết đoạn gọi món",
                "Viết đoạn hội thoại 4–6 lượt. Gọi một đồ uống, chọn cỡ ly và nói bạn muốn mang đi hay dùng tại quán.",
                """
                Barista: Hello! What can I get you?
                Customer: Could I have a small iced coffee, please?
                Barista: For here or to go?
                Customer: To go, please. Thank you!
                """, Morning(8), Morning(6), Morning(5)),
            WritingTask(course, studentId, "Lịch sự nhờ sửa món",
                "Viết 3–4 câu cho tình huống bạn nhận nhầm cà phê nóng. Dùng “Excuse me” để bắt đầu.",
                "Excuse me, I ordered an iced coffee. Could you make it with oat milk, please? Thank you for your help.",
                Morning(4), Morning(1), null),
            OrderMessageAssignment(now));

        db.CourseDiscussionThreads.AddRange(
            Discussion(enrollment, studentId, teacherId, "Dùng “to go” ở đâu trong câu?",
                "Em có thể nói “Could I have a to go coffee?” không ạ? Em chưa chắc vị trí của “to go”.",
                Morning(6), "Cách tự nhiên hơn là “Could I have a coffee to go, please?”. Đặt “to go” sau tên món.", Morning(5)),
            Discussion(enrollment, studentId, teacherId, "Phân biệt “oat milk” và “oats”",
                "Khi gọi món, em nói “with oats” thay cho “with oat milk” được không ạ?", now, null, null));

        foreach (var progress in enrollment.LessonProgressEntries)
            db.GamificationEvents.Add(Activity(studentId, GamificationEventType.LessonCompleted,
                progress.Lesson.Id, progress.UpdatedAtUtc!.Value));
        SeedReview(db, studentId, deck, deck.Cards.Single(card => card.OrderIndex == 0), Morning(2), FlashcardRating.Good);
        SeedReview(db, studentId, deck, deck.Cards.Single(card => card.OrderIndex == 1), Morning(1), FlashcardRating.Hard);

        if (!await db.StudentAchievements.AnyAsync(badge => badge.StudentUserId == studentId && badge.BadgeCode == "first-lesson"))
            db.StudentAchievements.Add(new StudentAchievement
            {
                StudentUserId = studentId, BadgeCode = "first-lesson", AwardedAtUtc = Morning(5), CreatedAtUtc = Morning(5)
            });

        await db.SaveChangesAsync();
        foreach (int daysAgo in new[] { 5, 4, 3, 2, 1 })
            await StudentActivityDays.RecordAsync(db, studentId, Morning(daysAgo), CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static Lesson LessonAt(int order, string title, string content, DateTimeOffset created) => new()
    {
        Title = title,
        OrderIndex = order,
        Status = LessonStatus.Published,
        CreatedAtUtc = created,
        Resources = [new LessonResource { Title = "Hội thoại mẫu và cách dùng", ResourceType = LessonResourceType.Text,
            ContentText = content, CreatedAtUtc = created }]
    };

    private static WritingAssignment WritingTask(Course course, Guid studentId, string title,
        string prompt, string response, DateTimeOffset assigned, DateTimeOffset submitted, DateTimeOffset? reviewed) => new()
    {
        Course = course,
        Title = title,
        Prompt = prompt,
        CreatedAtUtc = assigned,
        Submissions =
        [
            new WritingSubmission
            {
                StudentUserId = studentId, Response = response, CreatedAtUtc = submitted, ReviewedAtUtc = reviewed,
                UpdatedAtUtc = reviewed,
                Score = reviewed.HasValue ? 92 : null,
                Feedback = reviewed.HasValue
                    ? "Em đã gọi đúng món, cỡ ly và dùng “to go” tự nhiên. Lần sau thử thêm yêu cầu “with oat milk” để hội thoại sát thực tế hơn."
                    : null
            }
        ]
    };

    private static WritingAssignment OrderMessageAssignment(DateTimeOffset now) => new()
    {
        Id = OrderMessageAssignmentId,
        CourseId = WorkshopId,
        Title = "Viết lời nhắn đặt món",
        Prompt = "Viết 2–3 câu nhắn quán để đặt đồ uống mang đi. Nêu rõ cỡ ly, loại sữa và giờ bạn sẽ đến lấy.",
        DueAtUtc = GamificationPolicy.LocalDayStartUtc(GamificationPolicy.LocalDate(now).AddDays(2)).AddHours(21),
        CreatedAtUtc = now
    };

    private static CourseDiscussionThread Discussion(Enrollment enrollment, Guid studentId, Guid teacherId,
        string subject, string question, DateTimeOffset asked, string? reply, DateTimeOffset? replied)
    {
        var thread = new CourseDiscussionThread
        {
            Enrollment = enrollment, Subject = subject, CreatedAtUtc = asked, UpdatedAtUtc = replied,
            Messages = [new CourseDiscussionMessage { AuthorUserId = studentId, Body = question, SequenceNumber = 1, CreatedAtUtc = asked }]
        };
        if (reply is not null && replied is { } answered)
            thread.Messages.Add(new CourseDiscussionMessage
            {
                AuthorUserId = teacherId, Body = reply, WrittenByTeacher = true, SequenceNumber = 2, CreatedAtUtc = answered
            });
        return thread;
    }

    private static FlashcardDeck BuildDeck(Guid teacherId, Course course, Lesson lesson, DateTimeOffset created) => new()
    {
        OwnerTeacherUserId = teacherId,
        Course = course,
        Lesson = lesson,
        Title = "Từ vựng gọi món",
        Description = "Sáu cụm từ để gọi cà phê và thanh toán.",
        Status = FlashcardDeckStatus.Published,
        CreatedAtUtc = created,
        Cards =
        [
            Card(0, "to go", "Mang đi", "/tə ɡoʊ/", "One iced coffee to go, please."),
            Card(1, "for here", "Dùng tại quán", "/fər hɪr/", "Is that for here or to go?"),
            Card(2, "oat milk", "Sữa yến mạch", "/oʊt mɪlk/", "Could I have that with oat milk?"),
            Card(3, "without sugar", "Không thêm đường", "/wɪˈðaʊt ˈʃʊɡər/", "I’d like a tea without sugar, please."),
            Card(4, "the receipt", "Biên lai", "/ðə rɪˈsiːt/", "Could I have the receipt, please?"),
            Card(5, "pay by card", "Thanh toán bằng thẻ", "/peɪ baɪ kɑːrd/", "Can I pay by card?")
        ]
    };

    private static Flashcard Card(int order, string front, string back, string pronunciation, string example) =>
        new() { OrderIndex = order, FrontText = front, BackText = back, Pronunciation = pronunciation, Example = example };

    private static void SeedReview(AppDbContext db, Guid studentId, FlashcardDeck deck, Flashcard card,
        DateTimeOffset reviewed, FlashcardRating rating)
    {
        var schedule = FlashcardSchedule.Calculate(reviewed, 0, rating);
        db.FlashcardReviews.Add(new FlashcardReview
        {
            StudentUserId = studentId, Flashcard = card, ReviewRequestId = Guid.NewGuid(),
            Rating = rating, ReviewedAtUtc = reviewed, CreatedAtUtc = reviewed
        });
        db.FlashcardReviewStates.Add(new FlashcardReviewState
        {
            StudentUserId = studentId, Flashcard = card, LastRating = rating, LastReviewedAtUtc = reviewed,
            NextDueAtUtc = schedule.NextDueAtUtc, Repetitions = schedule.Repetitions, CreatedAtUtc = reviewed
        });
        db.GamificationEvents.Add(Activity(studentId, GamificationEventType.FlashcardsReviewed, deck.Id, reviewed));
    }

    private static GamificationEvent Activity(Guid studentId, GamificationEventType type, Guid sourceId, DateTimeOffset timestamp) =>
        new()
        {
            StudentUserId = studentId, EventType = type, SourceId = sourceId, OccurredAtUtc = timestamp, CreatedAtUtc = timestamp,
            WindowKey = GamificationPolicy.WindowKey(type, timestamp), Xp = GamificationPolicy.XpFor(type)
        };

    private static Practice BuildPractice(Guid teacherId, Lesson lesson, string audioUrl, DateTimeOffset created)
    {
        var stimulus = new Stimulus
        {
            OwnerTeacherUserId = teacherId, Title = "Gọi cà phê mang đi", StimulusType = StimulusType.Audio,
            ResourceUrl = audioUrl, CreatedAtUtc = created
        };
        stimulus.LockContent(created);

        var choices = MakeQuestion(QuestionType.MultipleChoice, EnglishSkill.Vocabulary,
            "Cụm nào nghĩa là uống tại quán?", "“For here” là dùng tại quán. “To go” là mang đi.");
        choices.Options = new[] { "For here", "To go", "By card" }
            .Select((text, index) => new QuestionOption { Content = text, IsCorrect = index == 0, OrderIndex = index }).ToList();

        var fill = MakeQuestion(QuestionType.FillBlank, EnglishSkill.Grammar,
            "Điền từ còn thiếu: Could I ___ a small iced coffee, please?", "Dùng “have” trong mẫu câu “Could I have …?”.");
        fill.AcceptedAnswers = [new QuestionAcceptedAnswer { AnswerText = "have" }];

        var matching = MakeQuestion(QuestionType.Matching, EnglishSkill.Vocabulary,
            "Ghép cụm từ với nghĩa.", "Chú ý phân biệt nguyên liệu, cách dùng tại quán và cách thanh toán.");
        matching.StructuredDefinitionJson = StructuredQuestionEvaluator.CreateDefinition(QuestionType.Matching,
            [new("Oat milk", "Sữa yến mạch"), new("For here", "Uống tại quán"), new("By card", "Bằng thẻ")]);

        var ordering = MakeQuestion(QuestionType.Ordering, EnglishSkill.Grammar,
            "Sắp xếp các cụm để tạo thành câu gọi món.", "Lời yêu cầu bắt đầu với “Could I have”, sau đó là tên món, rồi đến “please?”.");
        ordering.StructuredDefinitionJson = StructuredQuestionEvaluator.CreateDefinition(QuestionType.Ordering,
            [new("Could I have"), new("a small iced coffee"), new("with oat milk"), new("please?")]);

        var dictation = MakeQuestion(QuestionType.Dictation, EnglishSkill.Listening,
            "Nghe và chép lại câu gọi món.", "Khách gọi cà phê đá mang đi. Chú ý nghe cụm “to go”.");
        dictation.Stimulus = stimulus;
        dictation.AcceptedAnswers = [new QuestionAcceptedAnswer { AnswerText = Transcript }];

        Question[] questions = [choices, fill, matching, ordering, dictation];
        foreach (var question in questions)
            question.LockContent(created);

        return new Practice
        {
            OwnerTeacherUserId = teacherId, Lesson = lesson, Title = "Luyện gọi món theo 5 dạng câu hỏi",
            Description = "Từ chọn đáp án và điền từ đến ghép cặp, sắp xếp và nghe chép chính tả.",
            Status = PracticeStatus.Published, CreatedAtUtc = created,
            Questions = questions.Select((question, index) => new PracticeQuestion { Question = question, OrderIndex = index }).ToList()
        };

        Question MakeQuestion(QuestionType type, EnglishSkill skill, string text, string explanation) => new()
        {
            OwnerTeacherUserId = teacherId, QuestionType = type, PrimarySkill = skill, Content = text, Explanation = explanation,
            Level = "A1", Status = QuestionStatus.Published, CreatedAtUtc = created
        };
    }

    private static async Task<string> StoreAudioAsync(IStimulusAudioStore audioStore)
    {
        await using var stream = typeof(LearningDemoWorkshopSeeder).Assembly
            .GetManifestResourceStream("EnglishLearningPlatform.Infrastructure.DemoContent.coffee-to-go.wav")
            ?? throw new InvalidOperationException("Embedded coffee-order audio was not found.");
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        return await audioStore.StoreAsync(bytes.ToArray());
    }
}

using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Application.Gamification;
using EnglishLearningPlatform.Application.Learning;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

public sealed class FlashcardReviewTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task Replayed_review_is_idempotent_and_not_due_card_cannot_be_reviewed_again()
    {
        var data = await SeedAsync();
        var key = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
        var first = await service.ReviewAsync(data.Student, data.Deck, data.Card, key, FlashcardRating.Good);
        var replay = await service.ReviewAsync(data.Student, data.Deck, data.Card, key, FlashcardRating.Good);
        var changedPayload = await service.ReviewAsync(data.Student, data.Deck, data.Card, key, FlashcardRating.Again);
        var early = await service.ReviewAsync(data.Student, data.Deck, data.Card, Guid.NewGuid(), FlashcardRating.Good);
        Assert.Equal(FlashcardWriteStatus.Saved, first.Status);
        Assert.Equal(FlashcardWriteStatus.Saved, replay.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.NextDueAtUtc, replay.NextDueAtUtc);
        Assert.Equal(FlashcardWriteStatus.Conflict, changedPayload.Status);
        Assert.Equal(FlashcardWriteStatus.Conflict, early.Status);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<FlashcardReview>().CountAsync(r => r.FlashcardId == data.Card));
        var state = await db.Set<FlashcardReviewState>().SingleAsync(s => s.FlashcardId == data.Card && s.StudentUserId == data.Student);
        Assert.Equal(1, state.Repetitions);
        Assert.Equal(state.LastReviewedAtUtc.AddDays(1), state.NextDueAtUtc);
        var activity = await db.StudentActivityDays.SingleAsync(d => d.StudentUserId == data.Student);
        Assert.Equal(GamificationPolicy.LocalDate(state.LastReviewedAtUtc), activity.ActivityDate);
        var summary = (await scope.ServiceProvider.GetRequiredService<IGamificationService>().GetSummaryAsync(data.Student)).Value!;
        Assert.Equal(1, summary.CurrentStreak);
        Assert.Equal(0, summary.TotalXp);
        var sheet = await service.GetStudyAsync(data.Student, data.Deck);
        Assert.Equal(0, sheet.Value!.DueCount);
        Assert.Null(sheet.Value.Card);
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_persist_one_review_and_one_state()
    {
        var data = await SeedAsync();
        var key = Guid.NewGuid();
        async Task<FlashcardReviewResult> SubmitAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IFlashcardService>()
                .ReviewAsync(data.Student, data.Deck, data.Card, key, FlashcardRating.Hard);
        }
        var results = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        Assert.All(results, r => Assert.Equal(FlashcardWriteStatus.Saved, r.Status));
        Assert.Single(results, r => r.IsReplay);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<FlashcardReview>().CountAsync(r => r.ReviewRequestId == key));
        Assert.Equal(1, await db.Set<FlashcardReviewState>().CountAsync(s => s.StudentUserId == data.Student && s.FlashcardId == data.Card));
        Assert.Equal(1, await db.StudentActivityDays.CountAsync(d => d.StudentUserId == data.Student));
    }

    [Fact]
    public async Task Only_enrolled_students_and_course_owner_can_access_deck()
    {
        var data = await SeedAsync();
        var otherStudent = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        var otherTeacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        using var scope = factory.Services.CreateScope();
        var students = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
        var teachers = scope.ServiceProvider.GetRequiredService<ITeacherFlashcardService>();
        Assert.Equal(LearningAccessResult.NotFound, (await students.GetStudyAsync(otherStudent.Id, data.Deck)).Access);
        Assert.Equal(FlashcardWriteStatus.Unavailable, (await students.ReviewAsync(otherStudent.Id, data.Deck, data.Card, Guid.NewGuid(), FlashcardRating.Good)).Status);
        Assert.Equal(LearningAccessResult.NotFound, (await teachers.GetDeckAsync(otherTeacher.Id, data.Deck)).Access);
        Assert.Equal(FlashcardWriteStatus.NotFound, (await teachers.ArchiveAsync(otherTeacher.Id, data.Deck)).Status);
        Assert.Equal(FlashcardWriteStatus.Forbidden, (await students.ReviewAsync(data.Teacher, data.Deck, data.Card, Guid.NewGuid(), FlashcardRating.Good)).Status);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<FlashcardReview>().AnyAsync(r => r.FlashcardId == data.Card));
    }

    [Fact]
    public async Task Lesson_from_another_course_cannot_be_attached_and_empty_deck_cannot_publish()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = new Course { OwnerTeacherUserId = data.Teacher, Title = "Other course", Status = CourseStatus.Published };
        var lesson = new Lesson { Module = new Module { Course = other, Title = "Module" }, Title = "Other lesson", Status = LessonStatus.Published };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        var teachers = scope.ServiceProvider.GetRequiredService<ITeacherFlashcardService>();
        var invalid = await teachers.CreateAsync(data.Teacher, new(data.Course, lesson.Id, "Wrong lesson", null));
        Assert.Equal(FlashcardWriteStatus.Invalid, invalid.Status);
        var created = await teachers.CreateAsync(data.Teacher, new(data.Course, null, "Empty deck", null));
        Assert.Equal(FlashcardWriteStatus.Saved, created.Status);
        Assert.Equal(FlashcardWriteStatus.Invalid, (await teachers.PublishAsync(data.Teacher, created.Id!.Value)).Status);
    }

    [Fact]
    public async Task Teacher_can_author_preview_publish_and_archive_a_deck()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var teachers = scope.ServiceProvider.GetRequiredService<ITeacherFlashcardService>();
        var created = await teachers.CreateAsync(data.Teacher, new(data.Course, null, "New words", "Examples"));
        var deckId = created.Id!.Value;
        var card = await teachers.SaveCardAsync(data.Teacher, deckId, null, new("learn", "học", "I learn English."));
        Assert.Equal(FlashcardWriteStatus.Saved, card.Status);
        var preview = await teachers.GetDeckAsync(data.Teacher, deckId);
        Assert.Equal("learn", Assert.Single(preview.Value!.Cards).FrontText);
        Assert.Equal(FlashcardWriteStatus.Saved, (await teachers.PublishAsync(data.Teacher, deckId)).Status);
        Assert.Equal(FlashcardWriteStatus.Invalid, (await teachers.DeleteCardAsync(data.Teacher, deckId, card.Id!.Value)).Status);
        var students = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
        Assert.Equal(LearningAccessResult.Allowed, (await students.GetStudyAsync(data.Student, deckId)).Access);
        Assert.Equal(FlashcardWriteStatus.Saved, (await teachers.ArchiveAsync(data.Teacher, deckId)).Status);
        Assert.Equal(LearningAccessResult.NotFound, (await students.GetStudyAsync(data.Student, deckId)).Access);
    }

    [Fact]
    public async Task Hidden_lesson_or_unpublished_course_removes_student_access()
    {
        var data = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses.SingleAsync(c => c.Id == data.Course);
        course.Status = CourseStatus.Unpublished;
        await db.SaveChangesAsync();
        var students = scope.ServiceProvider.GetRequiredService<IFlashcardService>();
        Assert.Equal(LearningAccessResult.NotFound, (await students.GetStudyAsync(data.Student, data.Deck)).Access);
        course.Status = CourseStatus.Published;
        var lesson = new Lesson { Module = new Module { CourseId = data.Course, Title = "Hidden module", Visibility = ModuleVisibility.Hidden }, Title = "Hidden lesson", Status = LessonStatus.Published };
        db.Lessons.Add(lesson);
        var deck = await db.Set<FlashcardDeck>().SingleAsync(d => d.Id == data.Deck);
        deck.Lesson = lesson;
        await db.SaveChangesAsync();
        Assert.Equal(LearningAccessResult.NotFound, (await students.GetStudyAsync(data.Student, data.Deck)).Access);
    }

    private async Task<SeedData> SeedAsync()
    {
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var student = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Student);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course { OwnerTeacherUserId = teacher.Id, Title = "Flashcards " + Guid.NewGuid(), Status = CourseStatus.Published };
        var card = new Flashcard { FrontText = "hello", BackText = "xin chào", OrderIndex = 0 };
        var deck = new FlashcardDeck { OwnerTeacherUserId = teacher.Id, Course = course, Title = "Greetings", Status = FlashcardDeckStatus.Published, Cards = [card] };
        db.Set<FlashcardDeck>().Add(deck);
        db.Enrollments.Add(new Enrollment { Course = course, StudentUserId = student.Id });
        await db.SaveChangesAsync();
        return new(teacher.Id, student.Id, course.Id, deck.Id, card.Id);
    }
    private sealed record SeedData(Guid Teacher, Guid Student, Guid Course, Guid Deck, Guid Card);
}

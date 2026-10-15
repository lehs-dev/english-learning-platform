using EnglishLearningPlatform.Application.QuestionBank;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests.QuestionBank;

public sealed class QuestionUsageValidatorTests
{
    private readonly QuestionUsageValidator _validator =
        new QuestionUsageValidator(new CreateQuestionValidator());

    // Mỗi test nhận một entity mới, không dùng chung dữ liệu có thể bị sửa.
    private static Question CreateValidQuestion()
    {
        Question question = new Question
        {
            OwnerTeacherUserId = Guid.NewGuid(),
            Content = "Choose the correct answer.",
            PrimarySkill = EnglishSkill.Reading,
            Level = "Beginner",
            Difficulty = "Easy",
            Explanation = "Option 2 is correct.",
            Status = QuestionStatus.Draft
        };

        question.Options.Add(new QuestionOption
        {
            Content = "Option 1",
            OrderIndex = 0,
            IsCorrect = false
        });
        question.Options.Add(new QuestionOption
        {
            Content = "Option 2",
            OrderIndex = 1,
            IsCorrect = true
        });

        return question;
    }

    // Gắn cả navigation và FK như dữ liệu đã Include từ database.
    // URL chỉ là dữ liệu test; validator không gọi mạng hoặc tải audio.
    private static Stimulus AttachStimulus(
        Question question,
        StimulusType stimulusType)
    {
        Stimulus stimulus = new Stimulus
        {
            OwnerTeacherUserId = question.OwnerTeacherUserId,
            StimulusType = stimulusType,
            Title = "Stimulus for validator tests",
            ContentText = "This is a short reading passage.",
            ResourceUrl = "https://example.test/audio.mp3"
        };

        question.StimulusId = stimulus.Id;
        question.Stimulus = stimulus;
        return stimulus;
    }

    // Kiểm tra Field và Code thay vì phụ thuộc câu chữ thông báo tiếng Việt.
    private static void AssertError(
        IReadOnlyList<QuestionError> errors,
        string field,
        string code)
    {
        Assert.Contains(errors, error =>
            error.Field == field && error.Code == code);
    }

    [Fact]
    public void Constructor_NullCreateValidator_ThrowsArgumentNullException()
    {
        // null! cố ý tạo dữ liệu sai trong test, không sử dụng trong code chức năng.
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => new QuestionUsageValidator(null!));

        Assert.Equal("createQuestionValidator", error.ParamName);
    }

    [Theory]
    [InlineData(QuestionStatus.Draft)]
    [InlineData(QuestionStatus.Published)]
    public void ValidateForNewUse_ValidSelectableStatus_ReturnsNoErrors(
        QuestionStatus status)
    {
        // Draft cũng được chọn nếu nội dung hợp lệ; không tự yêu cầu Published.
        Question question = CreateValidQuestion();
        question.Status = status;

        var errors = _validator.ValidateForNewUse(
            question.OwnerTeacherUserId, question);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateForNewUse_EmptyOwner_ReturnsOnlyInvalidOwnerWithoutDelegating()
    {
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        var errors = validator.ValidateForNewUse(Guid.Empty, null);

        Assert.Single(errors);
        AssertError(errors, "ownerTeacherUserId", "invalid_owner");
        Assert.Equal(0, recording.CallCount);
    }

    [Fact]
    public void ValidateForNewUse_NullQuestion_ReturnsOnlyNotFoundWithoutDelegating()
    {
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        var errors = validator.ValidateForNewUse(Guid.NewGuid(), null);

        Assert.Single(errors);
        AssertError(errors, "QuestionId", "not_found");
        Assert.Equal(0, recording.CallCount);
    }

    [Fact]
    public void ValidateForNewUse_ForeignQuestion_ReturnsOnlyForbiddenWithoutInspectingStructure()
    {
        Question question = CreateValidQuestion();
        question.Content = "";
        question.PrimarySkill = EnglishSkill.Listening;
        question.Status = QuestionStatus.Archived;
        question.Options = null!;
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        // Dữ liệu có nhiều lỗi nhưng người không sở hữu chỉ nhận lỗi quyền.
        var errors = validator.ValidateForNewUse(
            Guid.NewGuid(), question, (EnglishSkill)999);

        Assert.Single(errors);
        AssertError(errors, "QuestionId", "forbidden");
        Assert.Equal(0, recording.CallCount);
    }

    [Fact]
    public void ValidateForNewUse_ArchivedQuestion_ReturnsArchivedError()
    {
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Single(errors);
        AssertError(errors, nameof(Question.Status), "archived");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void ValidateForNewUse_UndefinedStatus_ReturnsInvalidError(int status)
    {
        Question question = CreateValidQuestion();
        question.Status = (QuestionStatus)status;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Status), "invalid");
    }

    [Fact]
    public void ValidateForNewUse_NullOptions_ReturnsRequiredErrorWithoutDelegating()
    {
        Question question = CreateValidQuestion();
        question.Options = null!;
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        var errors = validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Options), "required");
        Assert.Equal(0, recording.CallCount);
    }

    [Fact]
    public void ValidateForNewUse_NullOption_ReturnsItsIndexWithoutThrowing()
    {
        Question question = CreateValidQuestion();
        question.Options.Add(null!);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, "Options[2]", "required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ValidateForNewUse_FewerThanTwoOptions_ReturnsMinCountError(int count)
    {
        Question question = CreateValidQuestion();
        question.Options.Clear();
        if (count == 1)
        {
            // Vẫn có đúng một đáp án đúng để cô lập lỗi thiếu option.
            question.Options.Add(new QuestionOption
            {
                Content = "Only option",
                OrderIndex = 0,
                IsCorrect = true
            });
        }

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Options), "min_count");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidateForNewUse_IncorrectCorrectOptionCount_ReturnsExactlyOneCorrectError(
        int correctCount)
    {
        Question question = CreateValidQuestion();
        foreach (QuestionOption option in question.Options)
        {
            option.IsCorrect = correctCount == 2;
        }

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Options), "exactly_one_correct");
    }

    [Fact]
    public void ValidateForNewUse_MoreThanTwoOptionsWithOneCorrect_ReturnsNoErrors()
    {
        Question question = CreateValidQuestion();
        question.Options.Add(new QuestionOption
        {
            Content = "Option 3", OrderIndex = 2, IsCorrect = false
        });
        question.Options.Add(new QuestionOption
        {
            Content = "Option 4", OrderIndex = 3, IsCorrect = false
        });

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void ValidateForNewUse_UndefinedPrimarySkill_ReturnsInvalidError(int skill)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = (EnglishSkill)skill;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.PrimarySkill), "invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateForNewUse_BlankContent_ReturnsRequiredError(string? content)
    {
        Question question = CreateValidQuestion();
        question.Content = content!;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Content), "required");
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(4001)]
    public void ValidateForNewUse_ContentLength_EnforcesExistingBoundary(int length)
    {
        Question question = CreateValidQuestion();
        question.Content = new string('a', length);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        if (length == 4000)
        {
            Assert.Empty(errors);
        }
        else
        {
            AssertError(errors, nameof(Question.Content), "max_length");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateForNewUse_BlankOptionContent_ReturnsIndexedRequiredError(string? content)
    {
        Question question = CreateValidQuestion();
        question.Options.First().Content = content!;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, "Options[0].Content", "required");
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1001)]
    public void ValidateForNewUse_OptionContentLength_EnforcesExistingBoundary(int length)
    {
        Question question = CreateValidQuestion();
        question.Options.First().Content = new string('a', length);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        if (length == 1000)
        {
            Assert.Empty(errors);
        }
        else
        {
            AssertError(errors, "Options[0].Content", "max_length");
        }
    }

    [Theory]
    [InlineData(nameof(Question.Level), 32)]
    [InlineData(nameof(Question.Level), 33)]
    [InlineData(nameof(Question.Difficulty), 32)]
    [InlineData(nameof(Question.Difficulty), 33)]
    public void ValidateForNewUse_MetadataLength_EnforcesExistingBoundary(string field, int length)
    {
        Question question = CreateValidQuestion();
        if (field == nameof(Question.Level))
        {
            question.Level = new string('a', length);
        }
        else
        {
            question.Difficulty = new string('a', length);
        }

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        if (length == 32)
        {
            Assert.Empty(errors);
        }
        else
        {
            AssertError(errors, field, "max_length");
        }
    }

    [Theory]
    [InlineData(EnglishSkill.Reading)]
    [InlineData(EnglishSkill.Listening)]
    [InlineData(EnglishSkill.Vocabulary)]
    [InlineData(EnglishSkill.Grammar)]
    public void ValidateForNewUse_MatchingTargetSkill_ReturnsNoErrors(EnglishSkill skill)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = skill;
        if (skill == EnglishSkill.Listening)
        {
            AttachStimulus(question, StimulusType.Audio);
        }

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question, skill);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void ValidateForNewUse_UndefinedTargetSkill_ReturnsInvalidWithoutMismatchError(int skill)
    {
        Question question = CreateValidQuestion();

        var errors = _validator.ValidateForNewUse(
            question.OwnerTeacherUserId, question, (EnglishSkill)skill);

        AssertError(errors, "targetSkill", "invalid");
        Assert.DoesNotContain(errors, error => error.Code == "skill_mismatch");
    }

    [Fact]
    public void ValidateForNewUse_DifferentTargetSkill_ReturnsSkillMismatch()
    {
        Question question = CreateValidQuestion();

        var errors = _validator.ValidateForNewUse(
            question.OwnerTeacherUserId, question, EnglishSkill.Grammar);

        Assert.Single(errors);
        AssertError(errors, nameof(Question.PrimarySkill), "skill_mismatch");
    }

    [Fact]
    public void ValidateForNewUse_OwnPassage_ReturnsNoErrors()
    {
        Question question = CreateValidQuestion();
        AttachStimulus(question, StimulusType.Passage);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateForNewUse_ListeningWithoutStimulus_ReturnsAudioRequired()
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Single(errors);
        AssertError(errors, nameof(Question.StimulusId), "audio_required");
    }

    [Theory]
    [InlineData(EnglishSkill.Reading)]
    [InlineData(EnglishSkill.Listening)]
    public void ValidateForNewUse_NavigationWithoutStimulusId_RejectsInconsistentLink(EnglishSkill skill)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = skill;
        AttachStimulus(question, StimulusType.Audio);
        question.StimulusId = null;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.StimulusId), "invalid");
        if (skill == EnglishSkill.Listening)
        {
            AssertError(errors, nameof(Question.StimulusId), "audio_required");
        }
    }

    [Fact]
    public void ValidateForNewUse_StimulusIdWithoutLoadedNavigation_ReturnsInvalidLink()
    {
        Question question = CreateValidQuestion();
        question.StimulusId = Guid.NewGuid();

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.StimulusId), "invalid");
    }

    [Fact]
    public void ValidateForNewUse_MismatchedStimulusId_ReturnsInvalidLink()
    {
        Question question = CreateValidQuestion();
        AttachStimulus(question, StimulusType.Passage);
        question.StimulusId = Guid.NewGuid();

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.StimulusId), "invalid");
    }

    [Fact]
    public void ValidateForNewUse_ForeignStimulus_ReturnsForbiddenWithoutInspectingAudioUrl()
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;
        Stimulus stimulus = AttachStimulus(question, StimulusType.Audio);
        stimulus.OwnerTeacherUserId = Guid.NewGuid();
        stimulus.ResourceUrl = "invalid-url";

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Single(errors);
        AssertError(errors, nameof(Question.StimulusId), "forbidden");
        Assert.DoesNotContain(errors, error => error.Code == "invalid_audio_url");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void ValidateForNewUse_UndefinedStimulusType_ReturnsInvalidError(int type)
    {
        Question question = CreateValidQuestion();
        AttachStimulus(question, (StimulusType)type);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Stimulus.StimulusType), "invalid");
    }

    [Fact]
    public void ValidateForNewUse_ListeningWithPassage_ReturnsAudioRequired()
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;
        AttachStimulus(question, StimulusType.Passage);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.StimulusId), "audio_required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/audio/sample.mp3")]
    [InlineData("//example.test/audio.mp3")]
    [InlineData("ftp://example.test/audio.mp3")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-url")]
    public void ValidateForNewUse_InvalidAudioUrl_ReturnsInvalidAudioUrl(string? url)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;
        Stimulus stimulus = AttachStimulus(question, StimulusType.Audio);
        stimulus.ResourceUrl = url;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Stimulus.ResourceUrl), "invalid_audio_url");
    }

    [Theory]
    [InlineData("http://example.test/audio.mp3")]
    [InlineData("https://example.test/audio.mp3")]
    public void ValidateForNewUse_ValidAudioUrl_ReturnsNoErrors(string url)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;
        Stimulus stimulus = AttachStimulus(question, StimulusType.Audio);
        stimulus.ResourceUrl = url;

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(2049)]
    public void ValidateForNewUse_AudioUrlLength_EnforcesExistingBoundary(int length)
    {
        Question question = CreateValidQuestion();
        question.PrimarySkill = EnglishSkill.Listening;
        Stimulus stimulus = AttachStimulus(question, StimulusType.Audio);
        const string prefix = "https://example.test/";
        stimulus.ResourceUrl = prefix + new string('a', length - prefix.Length);

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        if (length == 2048)
        {
            Assert.Empty(errors);
        }
        else
        {
            AssertError(errors, nameof(Stimulus.ResourceUrl), "invalid_audio_url");
        }
    }

    [Fact]
    public void ValidateForNewUse_NonListeningAudioWithInvalidUrl_StillRejectsInvalidResource()
    {
        Question question = CreateValidQuestion();
        Stimulus stimulus = AttachStimulus(question, StimulusType.Audio);
        stimulus.ResourceUrl = "invalid-url";

        var errors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Stimulus.ResourceUrl), "invalid_audio_url");
    }

    [Fact]
    public void ValidateForNewUse_MultipleIndependentErrors_ReturnsStructureSkillStatusAndAudioErrors()
    {
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;
        question.PrimarySkill = EnglishSkill.Listening;
        question.Content = "";
        question.Options.Clear();

        var errors = _validator.ValidateForNewUse(
            question.OwnerTeacherUserId, question, EnglishSkill.Reading);

        AssertError(errors, nameof(Question.Status), "archived");
        AssertError(errors, nameof(Question.Content), "required");
        AssertError(errors, nameof(Question.Options), "min_count");
        AssertError(errors, nameof(Question.Options), "exactly_one_correct");
        AssertError(errors, nameof(Question.PrimarySkill), "skill_mismatch");
        AssertError(errors, nameof(Question.StimulusId), "audio_required");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validation_DelegatesAllRequestFieldsAndPreservesReturnedErrors(bool existingUse)
    {
        Question question = CreateValidQuestion();
        Stimulus stimulus = AttachStimulus(question, StimulusType.Passage);
        QuestionError delegatedError = new QuestionError("Content", "recording_error", "Test error.");
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        recording.Errors.Add(delegatedError);
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        IReadOnlyList<QuestionError> errors;
        if (existingUse)
        {
            errors = validator.ValidateForExistingUse(question.OwnerTeacherUserId, question);
        }
        else
        {
            errors = validator.ValidateForNewUse(question.OwnerTeacherUserId, question);
        }

        Assert.Equal(1, recording.CallCount);
        Assert.NotNull(recording.LastRequest);
        CreateQuestionRequest request = recording.LastRequest;
        Assert.Equal(question.Content, request.Content);
        Assert.Equal(question.PrimarySkill, request.PrimarySkill);
        Assert.Equal(question.Level, request.Level);
        Assert.Equal(question.Difficulty, request.Difficulty);
        Assert.Equal(question.Explanation, request.Explanation);
        Assert.Equal(stimulus.Id, request.StimulusId);
        QuestionOption[] options = question.Options.ToArray();
        Assert.Equal(options.Length, request.Options.Count);
        for (int index = 0; index < options.Length; index++)
        {
            Assert.Equal(options[index].Content, request.Options[index].Content);
            Assert.Equal(options[index].IsCorrect, request.Options[index].IsCorrect);
        }
        Assert.Single(errors);
        Assert.Same(delegatedError, errors[0]);
    }

    [Theory]
    [InlineData(QuestionStatus.Draft, false)]
    [InlineData(QuestionStatus.Draft, true)]
    [InlineData(QuestionStatus.Archived, false)]
    [InlineData(QuestionStatus.Archived, true)]
    public void Validation_DoesNotMutateQuestionOptionsOrStimulus(
        QuestionStatus status,
        bool existingUse)
    {
        Question question = CreateValidQuestion();
        question.Status = status;
        Stimulus stimulus = AttachStimulus(question, StimulusType.Passage);
        var originalOptions = question.Options;
        QuestionOption[] options = question.Options.ToArray();
        Guid questionId = question.Id;
        Guid ownerId = question.OwnerTeacherUserId;
        DateTimeOffset createdAt = question.CreatedAtUtc;
        DateTimeOffset? updatedAt = question.UpdatedAtUtc;
        Guid[] optionIds = options.Select(option => option.Id).ToArray();

        if (existingUse)
        {
            _validator.ValidateForExistingUse(ownerId, question);
        }
        else
        {
            _validator.ValidateForNewUse(ownerId, question);
        }

        Assert.Equal(questionId, question.Id);
        Assert.Equal(ownerId, question.OwnerTeacherUserId);
        Assert.Equal(createdAt, question.CreatedAtUtc);
        Assert.Equal(updatedAt, question.UpdatedAtUtc);
        Assert.Equal(status, question.Status);
        Assert.Equal(EnglishSkill.Reading, question.PrimarySkill);
        Assert.Equal("Choose the correct answer.", question.Content);
        Assert.Equal("Beginner", question.Level);
        Assert.Equal("Easy", question.Difficulty);
        Assert.Equal("Option 2 is correct.", question.Explanation);
        Assert.Same(originalOptions, question.Options);
        Assert.Equal(2, question.Options.Count);
        Assert.Same(options[0], question.Options.First());
        Assert.Same(options[1], question.Options.Last());
        Assert.Equal(optionIds[0], options[0].Id);
        Assert.Equal(optionIds[1], options[1].Id);
        Assert.Equal("Option 1", options[0].Content);
        Assert.Equal("Option 2", options[1].Content);
        Assert.Equal(0, options[0].OrderIndex);
        Assert.Equal(1, options[1].OrderIndex);
        Assert.False(options[0].IsCorrect);
        Assert.True(options[1].IsCorrect);
        Assert.Same(stimulus, question.Stimulus);
        Assert.Equal(stimulus.Id, question.StimulusId);
        Assert.Equal(ownerId, stimulus.OwnerTeacherUserId);
        Assert.Equal(StimulusType.Passage, stimulus.StimulusType);
        Assert.Equal("Stimulus for validator tests", stimulus.Title);
        Assert.Equal("This is a short reading passage.", stimulus.ContentText);
        Assert.Equal("https://example.test/audio.mp3", stimulus.ResourceUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validation_ReturnedErrorsAreReadOnlyAndIndependentOfDependencyList(bool existingUse)
    {
        Question question = CreateValidQuestion();
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        recording.Errors.Add(new QuestionError("Content", "test_error", "Test error."));
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        IReadOnlyList<QuestionError> errors;
        if (existingUse)
        {
            errors = validator.ValidateForExistingUse(question.OwnerTeacherUserId, question);
        }
        else
        {
            errors = validator.ValidateForNewUse(question.OwnerTeacherUserId, question);
        }
        recording.Errors.Clear();

        Assert.Single(errors);
        // Không thể thêm lỗi từ bên ngoài hoặc thay đổi danh sách kết quả đã trả.
        var collection = Assert.IsAssignableFrom<ICollection<QuestionError>>(errors);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(
            new QuestionError("Content", "extra_error", "Must not be added.")));
    }

    [Theory]
    [InlineData(QuestionStatus.Draft)]
    [InlineData(QuestionStatus.Published)]
    [InlineData(QuestionStatus.Archived)]
    public void ValidateForExistingUse_ValidQuestionStatus_ReturnsNoErrors(QuestionStatus status)
    {
        // Validator chỉ kiểm tra câu hỏi; service sẽ kiểm tra trạng thái của bài.
        Question question = CreateValidQuestion();
        question.Status = status;

        var errors = _validator.ValidateForExistingUse(question.OwnerTeacherUserId, question);

        Assert.Empty(errors);
    }

    [Fact]
    public void ArchivedQuestion_IsRejectedForNewUseButAcceptedForExistingUse()
    {
        // Cùng một câu hỏi, không đổi trạng thái để lách điều kiện Archived.
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;

        var newErrors = _validator.ValidateForNewUse(question.OwnerTeacherUserId, question);
        var existingErrors = _validator.ValidateForExistingUse(question.OwnerTeacherUserId, question);

        Assert.Single(newErrors);
        AssertError(newErrors, nameof(Question.Status), "archived");
        Assert.Empty(existingErrors);
        Assert.Equal(QuestionStatus.Archived, question.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void ValidateForExistingUse_UndefinedStatus_IsStillRejected(int status)
    {
        Question question = CreateValidQuestion();
        question.Status = (QuestionStatus)status;

        var errors = _validator.ValidateForExistingUse(question.OwnerTeacherUserId, question);

        AssertError(errors, nameof(Question.Status), "invalid");
    }

    [Theory]
    [InlineData(EnglishSkill.Reading)]
    [InlineData(EnglishSkill.Listening)]
    [InlineData(EnglishSkill.Vocabulary)]
    [InlineData(EnglishSkill.Grammar)]
    public void ValidateForExistingUse_ArchivedQuestionWithMatchingSkill_ReturnsNoErrors(EnglishSkill skill)
    {
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;
        question.PrimarySkill = skill;
        if (skill == EnglishSkill.Listening)
        {
            AttachStimulus(question, StimulusType.Audio);
        }

        var errors = _validator.ValidateForExistingUse(question.OwnerTeacherUserId, question, skill);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateForExistingUse_NullQuestion_ReturnsOnlyNotFoundWithoutDelegating()
    {
        RecordingCreateQuestionValidator recording = new RecordingCreateQuestionValidator();
        QuestionUsageValidator validator = new QuestionUsageValidator(recording);

        var errors = validator.ValidateForExistingUse(Guid.NewGuid(), null);

        Assert.Single(errors);
        AssertError(errors, "QuestionId", "not_found");
        Assert.Equal(0, recording.CallCount);
    }

    // Bỏ qua Archived không có nghĩa là bỏ qua các điều kiện khác.
    // Mỗi dòng dữ liệu tạo một lỗi độc lập trên câu hỏi Archived.
    [Theory]
    [InlineData("empty_owner", "ownerTeacherUserId", "invalid_owner")]
    [InlineData("foreign_question", "QuestionId", "forbidden")]
    [InlineData("null_options", "Options", "required")]
    [InlineData("null_option", "Options[2]", "required")]
    [InlineData("blank_content", "Content", "required")]
    [InlineData("long_content", "Content", "max_length")]
    [InlineData("blank_option", "Options[0].Content", "required")]
    [InlineData("long_option", "Options[0].Content", "max_length")]
    [InlineData("long_level", "Level", "max_length")]
    [InlineData("long_difficulty", "Difficulty", "max_length")]
    [InlineData("no_options", "Options", "min_count")]
    [InlineData("one_option", "Options", "min_count")]
    [InlineData("no_correct", "Options", "exactly_one_correct")]
    [InlineData("two_correct", "Options", "exactly_one_correct")]
    [InlineData("undefined_primary_skill", "PrimarySkill", "invalid")]
    [InlineData("skill_mismatch", "PrimarySkill", "skill_mismatch")]
    [InlineData("undefined_target_skill", "targetSkill", "invalid")]
    [InlineData("unloaded_stimulus", "StimulusId", "invalid")]
    [InlineData("navigation_without_fk", "StimulusId", "invalid")]
    [InlineData("mismatched_stimulus", "StimulusId", "invalid")]
    [InlineData("foreign_stimulus", "StimulusId", "forbidden")]
    [InlineData("undefined_stimulus_type", "StimulusType", "invalid")]
    [InlineData("listening_without_audio", "StimulusId", "audio_required")]
    [InlineData("listening_with_passage", "StimulusId", "audio_required")]
    [InlineData("missing_audio_url", "ResourceUrl", "invalid_audio_url")]
    [InlineData("invalid_audio_url", "ResourceUrl", "invalid_audio_url")]
    [InlineData("non_listening_invalid_audio", "ResourceUrl", "invalid_audio_url")]
    public void ValidateForExistingUse_ArchivedQuestionWithOtherFault_IsStillRejected(
        string fault,
        string expectedField,
        string expectedCode)
    {
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;
        Guid ownerId = question.OwnerTeacherUserId;
        EnglishSkill? targetSkill = null;

        switch (fault)
        {
            case "empty_owner":
                ownerId = Guid.Empty;
                break;
            case "foreign_question":
                question.OwnerTeacherUserId = Guid.NewGuid();
                question.Content = "";
                question.Options = null!;
                break;
            case "null_options":
                question.Options = null!;
                break;
            case "null_option":
                question.Options.Add(null!);
                break;
            case "blank_content":
                question.Content = "";
                break;
            case "long_content":
                question.Content = new string('a', 4001);
                break;
            case "blank_option":
                question.Options.First().Content = "";
                break;
            case "long_option":
                question.Options.First().Content = new string('a', 1001);
                break;
            case "long_level":
                question.Level = new string('a', 33);
                break;
            case "long_difficulty":
                question.Difficulty = new string('a', 33);
                break;
            case "no_options":
                question.Options.Clear();
                break;
            case "one_option":
                QuestionOption correctOption = question.Options.Last();
                question.Options.Clear();
                question.Options.Add(correctOption);
                break;
            case "no_correct":
            case "two_correct":
                foreach (QuestionOption option in question.Options)
                {
                    option.IsCorrect = fault == "two_correct";
                }
                break;
            case "undefined_primary_skill":
                question.PrimarySkill = (EnglishSkill)999;
                break;
            case "skill_mismatch":
                targetSkill = EnglishSkill.Grammar;
                break;
            case "undefined_target_skill":
                targetSkill = (EnglishSkill)999;
                break;
            case "unloaded_stimulus":
                question.StimulusId = Guid.NewGuid();
                break;
            case "navigation_without_fk":
                AttachStimulus(question, StimulusType.Passage);
                question.StimulusId = null;
                break;
            case "mismatched_stimulus":
                AttachStimulus(question, StimulusType.Passage);
                question.StimulusId = Guid.NewGuid();
                break;
            case "foreign_stimulus":
                Stimulus foreign = AttachStimulus(question, StimulusType.Passage);
                foreign.OwnerTeacherUserId = Guid.NewGuid();
                break;
            case "undefined_stimulus_type":
                AttachStimulus(question, (StimulusType)999);
                break;
            case "listening_without_audio":
                question.PrimarySkill = EnglishSkill.Listening;
                break;
            case "listening_with_passage":
                question.PrimarySkill = EnglishSkill.Listening;
                AttachStimulus(question, StimulusType.Passage);
                break;
            case "missing_audio_url":
                question.PrimarySkill = EnglishSkill.Listening;
                AttachStimulus(question, StimulusType.Audio).ResourceUrl = null;
                break;
            case "invalid_audio_url":
                question.PrimarySkill = EnglishSkill.Listening;
                AttachStimulus(question, StimulusType.Audio).ResourceUrl = "javascript:alert(1)";
                break;
            case "non_listening_invalid_audio":
                AttachStimulus(question, StimulusType.Audio).ResourceUrl = "/audio/sample.mp3";
                break;
            default:
                throw new ArgumentException("Unknown test case.", nameof(fault));
        }

        var existingErrors = _validator.ValidateForExistingUse(ownerId, question, targetSkill);
        var newErrors = _validator.ValidateForNewUse(ownerId, question, targetSkill);

        // Kết quả phải có lỗi cụ thể, không chỉ giống nhau hoặc không ném exception.
        AssertError(existingErrors, expectedField, expectedCode);
        Assert.DoesNotContain(existingErrors, error => error.Code == "archived");
        // Hai chế độ có cùng các rule; chỉ khác việc từ chối Archived.
        Assert.Equal(
            newErrors.Where(error => error.Code != "archived").ToArray(),
            existingErrors.ToArray());
        if (fault == "foreign_question")
        {
            Assert.Single(existingErrors);
        }
    }

    [Fact]
    public void ValidateForExistingUse_MultipleFaults_CollectsErrorsWithoutArchivedError()
    {
        Question question = CreateValidQuestion();
        question.Status = QuestionStatus.Archived;
        question.PrimarySkill = EnglishSkill.Listening;
        question.Content = "";
        question.Options.Clear();

        var errors = _validator.ValidateForExistingUse(
            question.OwnerTeacherUserId, question, EnglishSkill.Reading);

        AssertError(errors, nameof(Question.Content), "required");
        AssertError(errors, nameof(Question.Options), "min_count");
        AssertError(errors, nameof(Question.Options), "exactly_one_correct");
        AssertError(errors, nameof(Question.PrimarySkill), "skill_mismatch");
        AssertError(errors, nameof(Question.StimulusId), "audio_required");
        Assert.DoesNotContain(errors, error => error.Code == "archived");
    }

    // Test double ghi nhận lời gọi để kiểm tra việc tái sử dụng validator MCQ.
    // Không thay thế code chức năng, không kết nối database.
    private sealed class RecordingCreateQuestionValidator : ICreateQuestionValidator
    {
        public int CallCount { get; private set; }
        public CreateQuestionRequest? LastRequest { get; private set; }
        public List<QuestionError> Errors { get; } = new List<QuestionError>();

        public IReadOnlyList<QuestionError> Validate(CreateQuestionRequest request)
        {
            CallCount++;
            LastRequest = request;
            return Errors;
        }
    }
}

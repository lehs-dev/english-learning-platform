using EnglishLearningPlatform.Domain.Common;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using Xunit;

namespace EnglishLearningPlatform.UnitTests;

public sealed class LessonMediaResourceTests
{
    [Theory]
    [InlineData("png", LessonResourceType.Image, "image/png")]
    [InlineData("mp3", LessonResourceType.Audio, "audio/mpeg")]
    public void OnlyCanonicalUrlsForTheSelectedMediaTypeAreAccepted(string format, LessonResourceType type, string contentType)
    {
        var media = new LessonMediaResource(Guid.NewGuid(), Guid.NewGuid(), format);

        Assert.True(LessonMediaResource.TryParse(media.Url, out var parsed));
        Assert.Equal(media, parsed);
        Assert.Equal(contentType, parsed.ContentType);
        Assert.True(CourseRules.ValidResource(type, null, media.Url));
        Assert.False(CourseRules.ValidResource(LessonResourceType.Link, null, media.Url));
        Assert.False(CourseRules.ValidResource(type, null, media.Url + "/extra"));
        Assert.False(CourseRules.ValidResource(type, null, media.Url + "?x=1"));
        Assert.False(CourseRules.ValidResource(type, null, media.Url.Replace(format, "svg")));
        Assert.True(CourseRules.ValidResource(type, null, "https://example.test/media"));
    }
}

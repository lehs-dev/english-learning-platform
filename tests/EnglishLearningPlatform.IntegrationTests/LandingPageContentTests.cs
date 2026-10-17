using System.Net;
using System.Text.RegularExpressions;
using EnglishLearningPlatform.Domain.Entities;
using EnglishLearningPlatform.Domain.Enums;
using EnglishLearningPlatform.Infrastructure.Identity;
using EnglishLearningPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishLearningPlatform.IntegrationTests;

/// <summary>
/// Opaque-box E2E test suite for Tier 3: Guest Landing Page Content & Experience.
/// Covers requirements from ORIGINAL_REQUEST.md § R2 and PROJECT.md Feature 11.
/// Validates that the public landing page (GET /) serves an engaging, high-conversion
/// discovery experience with Hero section, highlights, featured courses, and assessment CTA,
/// completely replacing the legacy empty div.dashboard-space placeholder.
/// </summary>
public sealed class LandingPageContentTests(IntegrationTestFactory factory) : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task LandingPage_DoesNotContainEmptyDashboardSpace()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Negative assertion: legacy placeholder must be completely removed
        Assert.DoesNotContain("dashboard-space", html);
    }

    [Fact]
    public async Task LandingPage_ContainsHeroBannerAndHeadline()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Must contain Hero section / banner
        Assert.True(
            html.Contains("hero", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("banner", StringComparison.OrdinalIgnoreCase),
            "Landing page must contain a prominent Hero banner section.");

        // Must contain headline promoting English learning platform
        Assert.True(
            html.Contains("Estudy") &&
            (html.Contains("học", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("tiếng Anh", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("English", StringComparison.OrdinalIgnoreCase)),
            "Landing page Hero banner must introduce Estudy and its English learning proposition.");
    }

    [Fact]
    public async Task LandingPage_ContainsPlatformHighlightCards()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Platform highlights / key features section
        Assert.True(
            html.Contains("highlight", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("feature", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("điểm nổi bật", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("lộ trình", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("tại sao", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("lợi ích", StringComparison.OrdinalIgnoreCase),
            "Landing page must showcase key platform highlights / feature cards.");
    }

    [Fact]
    public async Task LandingPage_ContainsFeaturedCoursesCatalog()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // An empty catalog must offer discovery without inventing course cards.
        var section = Regex.Match(html, "<section\\b[^>]*aria-labelledby=\"courses-heading\"[^>]*>.*?</section>",
            RegexOptions.Singleline);
        Assert.True(section.Success, "Landing page must contain its course discovery section.");
        // Estudy V2 empty state shows a "preparing" panel without a catalog link.
        Assert.Contains("Khóa học đang được chuẩn bị", section.Value);
        Assert.DoesNotContain("class=\"course-card\"", section.Value);

        // Published courses must actually appear once the database has content.
        var teacher = await IdentityTestHelpers.CreateUserAsync(factory, AppRoles.Teacher);
        var course = new Course
        {
            OwnerTeacherUserId = teacher.Id,
            Title = "LANDING_PUBLISHED_" + Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Published,
            Skills = [new CourseSkill { Skill = EnglishSkill.Grammar }]
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        }
        using var populatedResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, populatedResponse.StatusCode);
        var populatedHtml = WebUtility.HtmlDecode(await populatedResponse.Content.ReadAsStringAsync());
        var populatedSection = Regex.Match(populatedHtml, "<section\\b[^>]*aria-labelledby=\"courses-heading\"[^>]*>.*?</section>",
            RegexOptions.Singleline);
        Assert.True(populatedSection.Success);
        Assert.Contains("class=\"homepage-courses-grid\"", populatedSection.Value);
        Assert.Contains("class=\"course-item-card\"", populatedSection.Value);
        Assert.Contains(course.Title, populatedSection.Value);
        Assert.Contains($"/Courses/Details/{course.Id}", populatedSection.Value);
        Assert.DoesNotContain("Khóa học đang được chuẩn bị", populatedSection.Value);
    }

    [Fact]
    public async Task LandingPage_ContainsPlacementTestOrAssessmentCta()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Call to action for English level assessment / placement test
        Assert.True(
            html.Contains("đánh giá", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("kiểm tra trình độ", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("placement", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("assessment", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("test", StringComparison.OrdinalIgnoreCase),
            "Landing page must include a Call-to-Action for English level assessment.");
    }

    [Fact]
    public async Task LandingPage_ContainsAccessibleLayoutAndResponsiveMeta()
    {
        using var client = IdentityTestHelpers.CreateClient(factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Responsive viewport meta tag
        Assert.Contains("name=\"viewport\"", html);

        // Accessibility skip-link
        Assert.Contains("skip-link", html);

        // Estudy brand link
        Assert.Contains("Estudy", html);
    }
}

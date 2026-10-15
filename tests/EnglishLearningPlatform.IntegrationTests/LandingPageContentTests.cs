using System.Net;
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

        // Featured courses section / catalog grid
        Assert.True(
            html.Contains("Khóa học", StringComparison.OrdinalIgnoreCase) &&
            (html.Contains("tiêu biểu", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("nổi bật", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("catalog", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("course-card", StringComparison.OrdinalIgnoreCase) ||
             html.Contains("course-grid", StringComparison.OrdinalIgnoreCase)),
            "Landing page must display featured courses catalog.");
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

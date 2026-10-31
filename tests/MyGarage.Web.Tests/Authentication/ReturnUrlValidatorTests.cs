using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/api-check")]
    [InlineData("/vehicles/42?tab=service")]
    public void Sanitize_KeepsLocalRelativePaths(string url)
    {
        Assert.Equal(url, ReturnUrlValidator.Sanitize(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://localhost/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("api-check")]
    public void Sanitize_ReplacesAnythingElseWithHome(string? url)
    {
        Assert.Equal("/", ReturnUrlValidator.Sanitize(url));
    }
}

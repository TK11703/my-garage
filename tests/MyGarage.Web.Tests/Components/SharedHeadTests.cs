using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Components;

public class SharedHeadTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public SharedHeadTests(WebAppFactory factory) => this.factory = factory;

    private async Task<string> GetSignedOutPageAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/authentication/signed-out");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Head_LinksTheSvgFaviconThatResolves()
    {
        var html = await GetSignedOutPageAsync();

        var match = Regex.Match(html, "<link[^>]*rel=\"icon\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "No favicon link found.");
        Assert.Contains("type=\"image/svg+xml\"", System.Net.WebUtility.HtmlDecode(match.Value));

        var href = Regex.Match(match.Value, "href=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains("favicon", href);

        var client = factory.CreateClient();
        var response = await client.GetAsync(href.StartsWith('/') ? href : "/" + href);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Body_LoadsTheBootstrapBundleDeferredBeforeBlazor()
    {
        var html = await GetSignedOutPageAsync();

        var bundle = Regex.Match(html, "<script[^>]*bootstrap\\.bundle\\.min[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(bundle.Success, "No Bootstrap bundle script found.");
        Assert.Contains("defer", bundle.Value);

        var blazor = Regex.Match(html, "<script[^>]*blazor\\.web[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(blazor.Success, "No blazor.web.js script found.");
        Assert.True(blazor.Index > bundle.Index, "The Bootstrap bundle must come before blazor.web.js.");

        var src = Regex.Match(bundle.Value, "src=\"([^\"]+)\"").Groups[1].Value;
        var response = await factory.CreateClient().GetAsync(src.StartsWith('/') ? src : "/" + src);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Head_DeclaresTheThemeColor()
    {
        var html = await GetSignedOutPageAsync();

        Assert.Matches("<meta[^>]*name=\"theme-color\"[^>]*content=\"#1976D2\"", html);
    }

    [Fact]
    public async Task Head_AppliesTheSavedThemeBeforeTheStylesheetsAreUsed_AndTheShellScriptLoadsDeferred()
    {
        var html = await GetSignedOutPageAsync();

        var init = Regex.Match(html, "<script[^>]*theme-init[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(init.Success, "No theme-init script found.");
        Assert.DoesNotContain("defer", init.Value);
        Assert.DoesNotContain("async", init.Value);
        Assert.True(init.Index < html.IndexOf("</head>", StringComparison.Ordinal), "theme-init must be in the head.");

        var shell = Regex.Match(html, "<script[^>]*app-shell[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(shell.Success, "No app-shell script found.");
        Assert.Contains("defer", shell.Value);

        foreach (var tag in new[] { init.Value, shell.Value })
        {
            var src = Regex.Match(tag, "src=\"([^\"]+)\"").Groups[1].Value;
            var response = await factory.CreateClient().GetAsync(src.StartsWith('/') ? src : "/" + src);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}

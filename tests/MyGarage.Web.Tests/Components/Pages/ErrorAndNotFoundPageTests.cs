using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Components.Pages;

public class ErrorAndNotFoundPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public ErrorAndNotFoundPageTests(WebAppFactory factory) => this.factory = factory;

    private async Task<string> GetHtmlAsync(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Ada Lovelace");
        var response = await client.GetAsync(path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRoute_RendersTheStandardNotFoundPage()
    {
        var html = await GetHtmlAsync("/this-page-does-not-exist");

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Page not found</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Contains("Not found", html);
        Assert.Contains("href=\"#main-content\"", html);
        Assert.Contains("aria-label=\"Main\"", html);
    }

    [Fact]
    public async Task ErrorPage_RendersInsideTheStandardFrame()
    {
        var html = await GetHtmlAsync("/Error");

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Something went wrong</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Contains("href=\"#main-content\"", html);
        Assert.Contains("aria-label=\"Main\"", html);
    }
}

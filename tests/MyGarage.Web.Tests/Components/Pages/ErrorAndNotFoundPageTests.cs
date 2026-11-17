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
        Assert.Contains("404 Error</h1>", html);
        Assert.Contains("PAGE NOT FOUND", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wrong turn", html);
        Assert.Contains("data-testid=\"return-home\"", html);
        Assert.Matches("class=\"btn btn-primary[^\"]*\"[^>]*data-testid=\"return-home\"", html);
        Assert.Contains("Return Home", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Contains("href=\"#main-content\"", html);
        Assert.Contains("aria-label=\"Site menu\"", html);
    }

    [Fact]
    public async Task NotFound_ShowsAnOdometerThatRollsTo404_AndOffersSearchBesideHome()
    {
        var html = await GetHtmlAsync("/this-page-does-not-exist");

        Assert.Matches("<div[^>]*class=\"mg-notfound-code mg-odometer\"[^>]*role=\"img\"[^>]*aria-label=\"404\"", html);
        Assert.Equal(3, Regex.Matches(html, "class=\"mg-odo-wheel mg-odo-d[04]\"").Count);
        Assert.Equal(2, Regex.Matches(html, "mg-odo-d4").Count);
        Assert.Matches("<form[^>]*action=\"/search\"[^>]*data-testid=\"search-instead\"", html);
        Assert.Contains("MILES FROM HOME: 404", html);
        Assert.Matches("href=\"/faq\"", html);
    }

    [Fact]
    public async Task ErrorPage_RendersInsideTheStandardFrame()
    {
        var html = await GetHtmlAsync("/Error");

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Something went wrong</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Contains("href=\"#main-content\"", html);
        Assert.Contains("aria-label=\"Site menu\"", html);
    }
}

using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Help;

public class AboutPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AboutPageTests(WebAppFactory factory) => this.factory = factory;

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string? signedInAs = null)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (signedInAs is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, signedInAs);
        }

        var response = await client.GetAsync("/about");
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task IsViewableWithoutSigningIn()
    {
        var (status, html) = await GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<title>About My Garage – My Garage</title>", html);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("About My Garage</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.DoesNotMatch("breadcrumb-item[^>]*>(<[^>]+>)*\\s*About", html);
    }

    [Fact]
    public async Task AnonymousVisitorsSeeTheSignedOutBar()
    {
        var (_, html) = await GetAsync();

        Assert.Contains("href=\"/authentication/login\"", html);
        Assert.DoesNotContain("Account menu for", html);
    }

    [Fact]
    public async Task SignedInUsersSeeTheProfileMenu()
    {
        var (status, html) = await GetAsync("Ada Lovelace");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Account menu for Ada Lovelace", html);
    }

    [Fact]
    public async Task ItHasMoreThanAnIntroduction_Features_Steps_AndAPrivacyNote()
    {
        var (_, html) = await GetAsync();
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        Assert.Contains("My Garage keeps a private list of the vehicles you own.", html);
        Assert.True(document.QuerySelectorAll("[data-testid='about-feature']").Length >= 8);
        Assert.Equal(3, document.QuerySelectorAll("[data-testid='about-step']").Length);
        Assert.NotNull(document.QuerySelector("[data-testid='about-facts']"));
        Assert.Contains("Your vehicles are visible only to you", html);
    }

    [Fact]
    public async Task TheCallToAction_DependsOnWhetherYouAreSignedIn()
    {
        var (_, anonymous) = await GetAsync();
        var (_, signedIn) = await GetAsync("Ada Lovelace");

        Assert.Contains("href=\"/authentication/login\"", anonymous);
        Assert.DoesNotContain(">My vehicles<", anonymous);
        Assert.Contains(">My vehicles<", signedIn);
    }
}

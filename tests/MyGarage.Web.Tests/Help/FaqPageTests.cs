using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Help;

public class FaqPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public FaqPageTests(WebAppFactory factory) => this.factory = factory;

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string? signedInAs = null)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (signedInAs is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, signedInAs);
        }

        var response = await client.GetAsync("/faq");
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task IsViewableWithoutSigningIn()
    {
        var (status, html) = await GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<title>Frequently Asked Questions – My Garage</title>", html);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Frequently Asked Questions</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.Matches("aria-current=\"page\"[^>]*>(<[^>]+>)*\\s*FAQ", html);
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
}

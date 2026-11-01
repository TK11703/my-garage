using System.Net;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Authentication;

public class LoginRedirectTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private HttpClient CreateClient() =>
        factory.CreateClient(new() { AllowAutoRedirect = false });

    private string? LastRedirectUri()
    {
        string? last = null;
        while (factory.RedirectUris.TryDequeue(out var next)) last = next;
        return last;
    }

    [Fact]
    public async Task AnonymousRequest_ForProtectedPage_ChallengesAndReturnsToThatPage()
    {
        var response = await CreateClient().GetAsync("/not-found");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(WebAppFactory.FakeAuthority, response.Headers.Location!.ToString());
        Assert.Equal("/not-found", LastRedirectUri());
    }

    [Fact]
    public async Task AnonymousRequest_ForHome_ChallengesAndReturnsToHome()
    {
        var response = await CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(WebAppFactory.FakeAuthority, response.Headers.Location!.ToString());
        Assert.Equal("/", LastRedirectUri());
    }

    [Fact]
    public async Task Login_WithLocalReturnUrl_ReturnsToIt()
    {
        var response = await CreateClient().GetAsync("/authentication/login?returnUrl=%2Fapi-check");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(WebAppFactory.FakeAuthority, response.Headers.Location!.ToString());
        Assert.Equal("/api-check", LastRedirectUri());
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.com")]
    [InlineData("%2F%2Fevil.com")]
    public async Task Login_WithExternalReturnUrl_ReturnsHomeInstead(string returnUrl)
    {
        var response = await CreateClient().GetAsync($"/authentication/login?returnUrl={returnUrl}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", LastRedirectUri());
    }

    [Fact]
    public async Task SignedInUser_SeesNameOnHomePage()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice Garage", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignedInPages_AreNotCachedSoBackAfterSignOutRequestsSignInAgain()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");

        var response = await client.GetAsync("/");

        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}

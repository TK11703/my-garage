using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Authentication;

public partial class LogoutEndpointTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private HttpClient CreateSignedInClient()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        return client;
    }

    [Fact]
    public async Task Get_IsNotAllowed()
    {
        var response = await CreateSignedInClient().GetAsync("/authentication/logout");

        // The status-code re-execution page may rewrite 405 to 404; either way GET must never sign the user out.
        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Post_FromAnonymousUser_IsChallengedNotSignedOut()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PostAsync("/authentication/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("/logout", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_IsRejected()
    {
        var response = await CreateSignedInClient().PostAsync("/authentication/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAntiforgeryToken_SignsOutAndReturnsToSignedOutPage()
    {
        var client = CreateSignedInClient();
        var page = await client.GetAsync("/");
        var html = await page.Content.ReadAsStringAsync();

        var token = TokenPattern().Match(html).Groups["token"].Value;
        var cookie = page.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0])
            .First(value => value.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        Assert.NotEmpty(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/authentication/logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith($"{WebAppFactory.FakeAuthority}/logout", response.Headers.Location!.ToString());
        Assert.Equal("\"cache\"", response.Headers.GetValues("Clear-Site-Data").Single());
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenPattern();
}

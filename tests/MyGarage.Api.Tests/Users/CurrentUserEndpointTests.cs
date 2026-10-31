using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Users;

namespace MyGarage.Api.Tests.Users;

public class CurrentUserEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Path = "/api/users/me";

    [Fact]
    public async Task ValidToken_ReturnsIdentityFromClaims()
    {
        var client = factory.CreateClientWithToken(TestJwt.Create());

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.NotNull(user);
        Assert.Equal("oid-1234", user.UserId);
        Assert.Equal("Alice Garage", user.DisplayName);
        Assert.Equal("alice@contoso.test", user.Email);
    }

    [Fact]
    public async Task NoToken_Returns401ProblemDetails()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Alice", await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "expired", TestJwt.Create(expiresIn: TimeSpan.FromHours(-2)) },
        { "wrong audience", TestJwt.Create(audience: "99999999-9999-9999-9999-999999999999") },
        { "wrong issuer", TestJwt.Create(issuer: "https://evil.test/other-tenant/v2.0") },
        { "bad signature", TestJwt.Create(signWithOtherKey: true) },
        { "garbage", "not-a-jwt" },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task InvalidToken_Returns401ProblemDetails(string description, string token)
    {
        var response = await factory.CreateClientWithToken(token).GetAsync(Path);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{description} should be rejected");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TokenWithoutRequiredScope_Returns403ProblemDetails()
    {
        var client = factory.CreateClientWithToken(TestJwt.Create(scope: "some.other.scope"));

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RequestSuppliedIdentifiers_AreIgnored()
    {
        var client = factory.CreateClientWithToken(TestJwt.Create());
        client.DefaultRequestHeaders.Add("X-User-Id", "attacker");
        client.DefaultRequestHeaders.Add("X-Owner-Id", "attacker");

        var response = await client.GetAsync($"{Path}?userId=attacker&ownerId=attacker");

        var user = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.Equal("oid-1234", user!.UserId);
    }

    [Fact]
    public async Task Health_IsAnonymousAndReturnsNoUserData()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class SessionValidationTests
{
    private const string ThisInstance = "instance-b";

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<string?> SignedOutSchemes { get; } = [];

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutSchemes.Add(scheme);
            return Task.CompletedTask;
        }
    }

    private static (CookieValidatePrincipalContext Context, RecordingAuthenticationService Auth) CreateContext(string? instanceClaim)
    {
        var auth = new RecordingAuthenticationService();
        var services = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var identity = new ClaimsIdentity([new Claim("name", "Ada")], "Cookies");
        if (instanceClaim is not null)
        {
            identity.AddClaim(new Claim(SessionValidation.InstanceClaim, instanceClaim));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        return (new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket), auth);
    }

    [Fact]
    public async Task SessionFromThisInstance_IsKept()
    {
        var (context, auth) = CreateContext(ThisInstance);

        await SessionValidation.RejectSessionFromAnotherInstanceAsync(context, ThisInstance);

        Assert.NotNull(context.Principal);
        Assert.Empty(auth.SignedOutSchemes);
    }

    [Theory]
    [InlineData("instance-a")]
    [InlineData(null)]
    public async Task SessionFromAnotherRunOrWithoutAStamp_IsRejectedSoTheUserSignsInAgain(string? claim)
    {
        var (context, auth) = CreateContext(claim);

        await SessionValidation.RejectSessionFromAnotherInstanceAsync(context, ThisInstance);

        Assert.Null(context.Principal);
        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme], auth.SignedOutSchemes);
    }

    [Fact]
    public void StampInstance_AddsTheClaimOnce()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Ada")], "oidc"));

        SessionValidation.StampInstance(principal, "instance-a");
        SessionValidation.StampInstance(principal, "instance-b");

        var claim = Assert.Single(principal.FindAll(SessionValidation.InstanceClaim));
        Assert.Equal("instance-b", claim.Value);
    }

    [Fact]
    public void StampInstance_WithoutAPrincipal_DoesNothing()
    {
        SessionValidation.StampInstance(null, "instance-a");
    }

    [Theory]
    [InlineData("/authentication/logout", false)]
    [InlineData("/authentication/login", false)]
    [InlineData("/vehicles", true)]
    [InlineData("/", true)]
    [InlineData("/authenticationx", true)]
    public void ChecksEveryRequestExceptTheAuthenticationEndpoints(string path, bool expected)
    {
        Assert.Equal(expected, SessionValidation.ShouldCheck(new PathString(path)));
    }
}

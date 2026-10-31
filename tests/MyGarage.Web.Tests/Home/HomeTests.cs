using System.Security.Claims;
using Bunit;
using MyGarage.Web.Home;

namespace MyGarage.Web.Tests.Home;

public class HomeTests : BunitContext
{
    [Fact]
    public void ShowsNameClaim()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        auth.SetClaims(new Claim("name", "Alice Garage"), new Claim("preferred_username", "alice@contoso.test"));

        var cut = Render<MyGarage.Web.Home.Home>();

        Assert.Contains("Alice Garage", cut.Markup);
        Assert.DoesNotContain("alice@contoso.test", cut.Markup);
    }

    [Fact]
    public void FallsBackToPreferredUsername()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        auth.SetClaims(new Claim("preferred_username", "alice@contoso.test"));

        var cut = Render<MyGarage.Web.Home.Home>();

        Assert.Contains("alice@contoso.test", cut.Markup);
    }

    [Fact]
    public void FallsBackToNeutralLabel()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("ignored");

        var cut = Render<MyGarage.Web.Home.Home>();

        Assert.Contains("Signed-in user", cut.Markup);
    }
}

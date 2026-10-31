using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class LoginDisplayTests : BunitContext
{
    private sealed class FakeAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("fake-token", "__RequestVerificationToken");
    }

    public LoginDisplayTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
    }

    [Fact]
    public void Authenticated_ShowsNameAndSignOutForm()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        auth.SetClaims(new Claim("name", "Alice Garage"));

        var cut = Render<LoginDisplay>();

        Assert.Contains("Alice Garage", cut.Markup);
        var form = cut.Find("form");
        Assert.Equal("/authentication/logout", form.GetAttribute("action"));
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.NotNull(cut.Find("input[name='__RequestVerificationToken']"));
        Assert.Contains("Sign out", cut.Find("form button").TextContent);
    }

    [Fact]
    public void Anonymous_ShowsSignInLink()
    {
        AddAuthorization();

        var cut = Render<LoginDisplay>();

        Assert.Equal("/authentication/login", cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("form"));
    }
}

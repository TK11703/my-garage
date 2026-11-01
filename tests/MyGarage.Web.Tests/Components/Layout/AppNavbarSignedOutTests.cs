using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class AppNavbarSignedOutTests : BunitContext
{
    public AppNavbarSignedOutTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        AddAuthorization();
    }

    [Fact]
    public void ShowsBrandHelpAndSignIn()
    {
        var cut = Render<AppNavbar>();

        Assert.Equal("/", cut.Find("a.navbar-brand").GetAttribute("href"));
        Assert.NotNull(cut.Find("button[aria-label='Help']"));
        Assert.Equal("Sign in", cut.Find("a[href='/authentication/login']").TextContent.Trim());
    }

    [Fact]
    public void OmitsSearchNotificationsAndProfile()
    {
        var cut = Render<AppNavbar>();

        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("input[type='search']"));
        Assert.Empty(cut.FindAll("button[aria-label='Notifications']"));
        Assert.Empty(cut.FindAll("button[aria-label^='Account menu']"));
        Assert.Empty(cut.FindAll("button[aria-label='Toggle search']"));
    }
}

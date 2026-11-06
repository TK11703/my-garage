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
    public void ShowsBrandHelpThemeToggleAndSignIn()
    {
        var cut = Render<AppNavbar>();

        Assert.Equal("/", cut.Find("a.navbar-brand").GetAttribute("href"));
        Assert.NotNull(cut.Find("button[data-mg-theme-toggle]"));
        Assert.Equal("Sign in", cut.Find("a[href='/authentication/login']").TextContent.Trim());
    }

    [Fact]
    public void HelpMenuLinksToFaqAndAbout()
    {
        var cut = Render<AppNavbar>();

        var toggle = cut.Find("button[aria-label='Help']");
        Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));

        var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
        Assert.Equal("FAQ", menu.QuerySelector("a[href='/faq']")!.TextContent.Trim());
        Assert.Equal("About", menu.QuerySelector("a[href='/about']")!.TextContent.Trim());
    }

    [Fact]
    public void OmitsSearchNotificationsMenuButtonAndProfile()
    {
        var cut = Render<AppNavbar>();

        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("input[type='search']"));
        Assert.Empty(cut.FindAll("button[aria-label='Notifications']"));
        Assert.Empty(cut.FindAll("button[aria-label^='Account menu']"));
        Assert.Empty(cut.FindAll("a[href='/preferences']"));
        Assert.Empty(cut.FindAll("button[aria-label='Toggle search']"));
        Assert.Empty(cut.FindAll("button[data-mg-sidebar-toggle]"));
    }
}

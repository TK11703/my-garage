using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class PublicBarTests : BunitContext
{
    public PublicBarTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
    }

    [Fact]
    public void ShowsBrandHelpLinksThemeToggleAndSignIn_InThatOrder()
    {
        var cut = Render<PublicBar>();
        var markup = cut.Markup;

        Assert.Equal("/", cut.Find("a.mg-publicbar-brand").GetAttribute("href"));
        Assert.Contains("My Garage", cut.Find("a.mg-publicbar-brand").TextContent);
        Assert.Equal("Sign in", cut.Find("a[href='/authentication/login']").TextContent.Trim());

        var order = new[]
        {
            markup.IndexOf("mg-publicbar-brand", StringComparison.Ordinal),
            markup.IndexOf("aria-label=\"Help\"", StringComparison.Ordinal),
            markup.IndexOf("data-mg-theme-toggle", StringComparison.Ordinal),
            markup.IndexOf("/authentication/login", StringComparison.Ordinal),
        };
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void HelpLinksGoToFaqAndAbout()
    {
        var cut = Render<PublicBar>();

        var nav = cut.Find("nav[aria-label='Help']");
        Assert.Equal("FAQ", nav.QuerySelector("a[href='/faq']")!.TextContent.Trim());
        Assert.Equal("About", nav.QuerySelector("a[href='/about']")!.TextContent.Trim());
    }

    [Theory]
    [InlineData("/faq", "FAQ")]
    [InlineData("/about", "About")]
    public void TheCurrentHelpPageIsMarked(string path, string expected)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var cut = Render<PublicBar>();

        var current = Assert.Single(cut.FindAll("a[aria-current='page']"));
        Assert.Equal(expected, current.TextContent.Trim());
    }

    [Fact]
    public void HasNoRailSearchNotificationsOrAccount()
    {
        var cut = Render<PublicBar>();

        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(cut.FindAll("input[type='search']"));
        Assert.Empty(cut.FindAll("button[aria-label='Notifications']"));
        Assert.Empty(cut.FindAll("button[aria-label^='Account menu']"));
        Assert.Empty(cut.FindAll("a[href='/preferences']"));
        Assert.Empty(cut.FindAll("[data-mg-rail-toggle]"));
        Assert.Empty(cut.FindAll("aside"));
    }
}

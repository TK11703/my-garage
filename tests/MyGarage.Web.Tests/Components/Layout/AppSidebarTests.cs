using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class AppSidebarTests : BunitContext
{
    private readonly BunitAuthorizationContext auth;

    public AppSidebarTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        auth.SetClaims(new Claim("name", "Ada Lovelace"), new Claim("email", "ada@example.com"));
    }

    private IRenderedComponent<AppSidebar> RenderSidebar(string path = "/", int drafts = 0)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<AppSidebar>(ps => ps.Add(p => p.DraftsAwaitingReview, drafts));
    }

    [Fact]
    public void ProfileStartsTheMenu_WithInitialsNameAndEmail()
    {
        var cut = RenderSidebar();

        var aside = cut.Find("aside#mg-sidebar");
        Assert.Contains("mg-profile", aside.Children[0].ClassName);
        var profile = cut.Find("button.mg-profile-btn");
        Assert.Equal("Account menu for Ada Lovelace", profile.GetAttribute("aria-label"));
        Assert.Equal("dropdown", profile.GetAttribute("data-bs-toggle"));
        Assert.Equal("AL", cut.Find(".mg-avatar").TextContent.Trim());
        Assert.Equal("Ada Lovelace", cut.Find(".mg-profile-name").TextContent.Trim());
        Assert.Equal("ada@example.com", cut.Find("[data-testid='profile-email']").TextContent.Trim());
    }

    [Fact]
    public void WithoutAnEmail_NoEmailLineIsShown()
    {
        auth.SetClaims(new Claim("name", "Ada Lovelace"));

        var cut = RenderSidebar();

        Assert.Empty(cut.FindAll("[data-testid='profile-email']"));
    }

    [Fact]
    public void ProfileDropdown_HasPreferencesAndSignOutOnly()
    {
        var cut = RenderSidebar();

        var menu = cut.Find(".mg-profile .dropdown-menu");
        Assert.Equal(["Preferences", "Sign out"], menu.QuerySelectorAll(".dropdown-item").Select(item => item.TextContent.Trim()));
        Assert.Equal("/preferences", menu.QuerySelector("a.dropdown-item")!.GetAttribute("href"));

        var form = menu.QuerySelector("form")!;
        Assert.Equal("/authentication/logout", form.GetAttribute("action"));
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.NotNull(form.QuerySelector("input[name='__RequestVerificationToken']"));
        Assert.Equal("submit", form.QuerySelector("button")!.GetAttribute("type"));
    }

    [Fact]
    public void DisplayNameAndEmailAreEncoded()
    {
        auth.SetClaims(new Claim("name", "<b>x</b>"), new Claim("email", "<i>@x"));

        var cut = RenderSidebar();

        Assert.Empty(cut.FindAll("b, i"));
        Assert.Equal("<b>x</b>", cut.Find(".mg-profile-name").TextContent);
    }

    [Fact]
    public void ItemsAreGroupedUnderSmallMutedHeadings_EachWithAnIcon()
    {
        var cut = RenderSidebar();

        Assert.Equal(["Garage", "Maintenance", "Help"], cut.FindAll(".mg-nav-heading").Select(heading => heading.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".mg-nav-heading svg"));

        var lists = cut.FindAll("ul.mg-nav-list");
        Assert.Equal(4, lists.Count);
        Assert.Equal("Home", lists[0].QuerySelector(".mg-nav-label")!.TextContent.Trim());
        Assert.Single(lists[0].QuerySelectorAll("a"));
        Assert.All(lists.Skip(1), list => Assert.False(string.IsNullOrEmpty(list.GetAttribute("aria-labelledby"))));
        Assert.Equal(
            ["Home", "My Vehicles", "Add Vehicle", "Maintenance Drafts", "Add Maintenance", "FAQ", "About"],
            cut.FindAll("a.mg-nav-link").Select(link => link.QuerySelector(".mg-nav-label")!.TextContent.Trim()));
        Assert.All(cut.FindAll("a.mg-nav-link"), link =>
        {
            Assert.NotNull(link.QuerySelector("svg.bi"));
            Assert.Equal(link.QuerySelector(".mg-nav-label")!.TextContent.Trim(), link.GetAttribute("title"));
        });
    }

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/vehicles", "My Vehicles")]
    [InlineData("/vehicles?q=civic", "My Vehicles")]
    [InlineData("/vehicles/3f2b6f1e-0000-0000-0000-000000000000/edit", "My Vehicles")]
    [InlineData("/vehicles/new", "Add Vehicle")]
    [InlineData("/drafts", "Maintenance Drafts")]
    [InlineData("/drafts/3f2b6f1e-0000-0000-0000-000000000000", "Maintenance Drafts")]
    [InlineData("/drafts/upload", "Add Maintenance")]
    [InlineData("/faq", "FAQ")]
    [InlineData("/about", "About")]
    public void TheCurrentPageIsTheOnlyActiveItem(string path, string expected)
    {
        var cut = RenderSidebar(path);

        var active = Assert.Single(cut.FindAll("a.mg-nav-link.active"));
        Assert.Equal(expected, active.QuerySelector(".mg-nav-label")!.TextContent.Trim());
        Assert.Equal("page", active.GetAttribute("aria-current"));
        Assert.Single(cut.FindAll("a[aria-current]"));
    }

    [Fact]
    public void APageWithoutAMenuItem_ActivatesNone()
    {
        var cut = RenderSidebar("/preferences");

        Assert.Empty(cut.FindAll("a.mg-nav-link.active"));
    }

    [Fact]
    public void TheDraftsItemShowsTheAwaitingReviewCount()
    {
        var cut = RenderSidebar(drafts: 4);

        var link = cut.Find("a.mg-nav-link[href='/drafts']");
        Assert.Equal("4", link.QuerySelector("[data-testid='nav-drafts-count']")!.TextContent.Trim());
        Assert.Contains("4 awaiting review", link.TextContent);
    }

    [Fact]
    public void TheDraftsItemShowsNoCountWhenNothingIsWaiting()
    {
        var cut = RenderSidebar();

        Assert.Empty(cut.FindAll("[data-testid='nav-drafts-count']"));
    }

    [Fact]
    public void ABackdropClosesTheMenuOnAPhone()
    {
        var cut = RenderSidebar();

        Assert.NotNull(cut.Find(".mg-sidebar-backdrop[data-mg-sidebar-close]"));
    }
}

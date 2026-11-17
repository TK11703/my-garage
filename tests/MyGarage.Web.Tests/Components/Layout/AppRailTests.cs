using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Notifications;
using MyGarage.Web.Components.Layout;
using MyGarage.Web.Notifications;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Components.Layout;

internal sealed class FakeAntiforgeryStateProvider : AntiforgeryStateProvider
{
    public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("fake-token", "__RequestVerificationToken");
}

public class AppRailTests : BunitContext
{
    private readonly BunitAuthorizationContext auth;
    private UnreadCountDto unread = new() { Unread = 0, DraftsAwaitingReview = 0 };
    private HttpStatusCode? notificationsFailure;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    public AppRailTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero)));
        var handler = new StubHandler(request =>
        {
            if (notificationsFailure is { } status)
            {
                return new HttpResponseMessage(status);
            }

            return request.RequestUri!.AbsolutePath == "/api/notifications/unread-count"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(unread) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<NotificationDto>()) };
        });
        Services.AddSingleton(new NotificationsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        auth.SetClaims(new Claim("name", "Ada Lovelace"), new Claim("email", "ada@example.com"));
    }

    private IRenderedComponent<AppRail> RenderRail(string path = "/")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<AppRail>();
    }

    [Fact]
    public void TheRailLogoIsTheMarkNotLetters_AndStillNamedForAssistiveTechnology()
    {
        var cut = RenderRail();

        var logo = cut.Find("a.mg-rail-logo");
        Assert.NotNull(logo.QuerySelector("svg.mg-logo"));
        Assert.Equal(string.Empty, logo.TextContent.Trim());
        Assert.Equal("My Garage home", logo.GetAttribute("aria-label"));
        Assert.Equal("/", logo.GetAttribute("href"));
    }

    [Fact]
    public void TheRailIsALandmarkWithAnAccessibleName()
    {
        var cut = RenderRail();

        var rail = cut.Find("aside#mg-rail");
        Assert.Equal("Site menu", rail.GetAttribute("aria-label"));
        Assert.NotNull(rail.QuerySelector("nav[aria-label='Primary']"));
    }

    [Fact]
    public void ItemsAreInOrder_EachNamedForAssistiveTechnology()
    {
        var cut = RenderRail();

        var links = cut.FindAll("a.mg-rail-link");
        Assert.Equal(
            ["Garage", "Add vehicle", "Reminders", "Drafts", "Search", "Dashboard"],
            links.Select(link => link.QuerySelector(".visually-hidden")!.TextContent.Trim()));
        Assert.All(links, link =>
        {
            var icon = link.QuerySelector(".mg-rail-icon[aria-hidden='true'] svg.bi");
            Assert.NotNull(icon);
            Assert.Equal(string.Empty, icon.TextContent.Trim());
        });
        Assert.Equal(links.Count, links.Select(link => link.QuerySelector("svg.bi")!.InnerHtml).Distinct().Count());
        Assert.Equal(["/vehicles", "/vehicles/new", "/reminders", "/drafts", "/search", "/dashboard"], links.Select(link => link.GetAttribute("href")));
        Assert.All(links, link => Assert.False(string.IsNullOrEmpty(link.GetAttribute("title"))));
    }

    [Theory]
    [InlineData("/", "Garage")]
    [InlineData("/vehicles", "Garage")]
    [InlineData("/vehicles?q=civic", "Garage")]
    [InlineData("/vehicles/3f2b6f1e-0000-0000-0000-000000000000/edit", "Garage")]
    [InlineData("/vehicles/new", "Add vehicle")]
    [InlineData("/reminders", "Reminders")]
    [InlineData("/reminders?status=overdue", "Reminders")]
    [InlineData("/drafts", "Drafts")]
    [InlineData("/drafts/3f2b6f1e-0000-0000-0000-000000000000", "Drafts")]
    [InlineData("/drafts/upload", "Drafts")]
    [InlineData("/search", "Search")]
    [InlineData("/dashboard", "Dashboard")]
    public void TheCurrentPageIsTheOnlyActiveItem(string path, string expected)
    {
        var cut = RenderRail(path);

        var active = Assert.Single(cut.FindAll("a.mg-rail-link.active"));
        Assert.Equal(expected, active.QuerySelector(".visually-hidden")!.TextContent.Trim());
        Assert.Equal("page", active.GetAttribute("aria-current"));
        Assert.Single(cut.FindAll("a[aria-current]"));
    }

    [Fact]
    public void APageWithoutARailItem_ActivatesNone()
    {
        var cut = RenderRail("/preferences");

        Assert.Empty(cut.FindAll("a.mg-rail-link.active"));
    }

    [Fact]
    public void TheDraftsItemShowsTheAwaitingReviewCountOnceTheBellHasLoaded()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 4 };

        var cut = RenderRail();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("a.mg-rail-link[href='/drafts']");
            Assert.Equal("4", link.QuerySelector("[data-testid='nav-drafts-count']")!.TextContent.Trim());
            Assert.Contains("4 awaiting review", link.TextContent);
        });
    }

    [Fact]
    public void TheDraftsItemShowsNoCountWhenNothingIsWaiting()
    {
        var cut = RenderRail();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='notifications-menu']")));
        Assert.Empty(cut.FindAll("[data-testid='nav-drafts-count']"));
    }

    [Fact]
    public void ABackdropAndAMenuButtonOperateTheDrawerOnAPhone()
    {
        var cut = RenderRail();

        Assert.NotNull(cut.Find(".mg-rail-backdrop[data-mg-rail-close]"));
        var button = cut.Find("button[data-mg-rail-toggle]");
        Assert.Equal("Open menu", button.GetAttribute("aria-label"));
        Assert.Equal("mg-rail", button.GetAttribute("aria-controls"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Fact]
    public void ShowsToolsInOrder_NotificationsHelpThemeAccount()
    {
        var cut = RenderRail();
        var markup = cut.Markup;

        var order = new[]
        {
            markup.IndexOf("data-testid=\"notifications-menu\"", StringComparison.Ordinal),
            markup.IndexOf("aria-label=\"Help\"", StringComparison.Ordinal),
            markup.IndexOf("data-mg-theme-toggle", StringComparison.Ordinal),
            markup.IndexOf("Account menu for", StringComparison.Ordinal),
        };

        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void TheThemeToggleIsAButton()
    {
        var cut = RenderRail();

        var toggle = cut.Find("button[data-mg-theme-toggle]");
        Assert.Equal("Dark mode", toggle.GetAttribute("aria-label"));
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        Assert.Equal("button", toggle.GetAttribute("type"));
    }

    [Fact]
    public void HelpMenuLinksToFaqAndAbout()
    {
        var cut = RenderRail();

        var toggle = cut.Find("button[aria-label='Help']");
        Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));
        var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
        Assert.Equal("FAQ", menu.QuerySelector("a[href='/faq']")!.TextContent.Trim());
        Assert.Equal("About", menu.QuerySelector("a[href='/about']")!.TextContent.Trim());
    }

    [Fact]
    public void TheAccountMenuShowsInitialsNameAndEmail()
    {
        var cut = RenderRail();

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

        var cut = RenderRail();

        Assert.Empty(cut.FindAll("[data-testid='profile-email']"));
    }

    [Fact]
    public void AccountDropdown_HasPreferencesAndSignOutOnly()
    {
        var cut = RenderRail();

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

        var cut = RenderRail();

        Assert.Empty(cut.FindAll("b, i"));
        Assert.Equal("<b>x</b>", cut.Find(".mg-profile-name").TextContent);
    }

    [Fact]
    public void TheNotificationsMenuOpensToTheRightOfTheRail()
    {
        var cut = RenderRail();

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("button[aria-label='Notifications']");
            Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));
            Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
            Assert.Contains("dropend", toggle.ParentElement!.ClassName);

            var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
            Assert.Contains("You have no notifications.", menu.TextContent);
            Assert.DoesNotContain("dropdown-menu-end", menu.ClassName);
        });
    }

    [Fact]
    public void UnreadNotifications_AppearAsADotOnTheBell()
    {
        unread = new UnreadCountDto { Unread = 3, DraftsAwaitingReview = 4 };

        var cut = RenderRail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Notifications, 3 unread", cut.Find("button[data-bs-toggle='dropdown'][aria-label^='Notifications']").GetAttribute("aria-label"));
            Assert.NotNull(cut.Find("[data-testid='notification-badge']"));
        });
    }

    [Fact]
    public void WhenTheNotificationsApiFails_TheRailStillRenders_WithNoDot()
    {
        notificationsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderRail();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("button[aria-label='Notifications']"));
            Assert.Empty(cut.FindAll("[data-testid='notification-badge']"));
            Assert.Empty(cut.FindAll("[data-testid='nav-drafts-count']"));
            Assert.NotNull(cut.Find("button[data-mg-theme-toggle]"));
            Assert.Equal(6, cut.FindAll("a.mg-rail-link").Count);
        });
    }
}

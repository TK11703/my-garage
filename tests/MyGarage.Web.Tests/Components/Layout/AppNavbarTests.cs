using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
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

public class AppNavbarTests : BunitContext
{
    private readonly BunitAuthorizationContext auth;
    private UnreadCountDto unread = new() { Unread = 0, DraftsAwaitingReview = 0 };
    private HttpStatusCode? notificationsFailure;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    public AppNavbarTests()
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
        auth.SetClaims(new Claim("name", "Ada Lovelace"));
    }

    [Fact]
    public void IsAFixedFullWidthBar()
    {
        var cut = Render<AppNavbar>();

        var nav = cut.Find("nav[aria-label='Main']");
        Assert.Contains("fixed-top", nav.ClassName);
        Assert.NotNull(nav.QuerySelector(".container-fluid"));
    }

    [Fact]
    public void ShowsPartsInOrder()
    {
        var cut = Render<AppNavbar>();
        var markup = cut.Markup;

        var order = new[]
        {
            markup.IndexOf("navbar-brand", StringComparison.Ordinal),
            markup.IndexOf("role=\"search\"", StringComparison.Ordinal),
            markup.IndexOf("aria-label=\"Notifications\"", StringComparison.Ordinal),
            markup.IndexOf("aria-label=\"Help\"", StringComparison.Ordinal),
            markup.IndexOf("aria-label=\"Account menu for Ada Lovelace\"", StringComparison.Ordinal),
        };

        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void BrandLinksHome()
    {
        var cut = Render<AppNavbar>();

        Assert.Equal("/", cut.Find("a.navbar-brand").GetAttribute("href"));
        Assert.Equal("My Garage", cut.Find("a.navbar-brand").TextContent.Trim());
    }

    [Fact]
    public void SearchIsAGetFormToTheVehicleList()
    {
        var cut = Render<AppNavbar>();

        var form = cut.Find("form[role='search']");
        Assert.Equal("get", form.GetAttribute("method"), ignoreCase: true);
        Assert.Equal("/vehicles", form.GetAttribute("action"));

        var input = form.QuerySelector("input[type='search']")!;
        Assert.Equal("q", input.GetAttribute("name"));
        Assert.Equal("100", input.GetAttribute("maxlength"));
        Assert.Equal("Search vehicles", input.GetAttribute("aria-label"));

        var submit = form.QuerySelector("button[type='submit']")!;
        Assert.Equal("Search", submit.GetAttribute("aria-label"));
    }

    [Fact]
    public void SearchSitsInACollapseOpenedByAToggle()
    {
        var cut = Render<AppNavbar>();

        var toggle = cut.Find("button[aria-label='Toggle search']");
        Assert.Equal("collapse", toggle.GetAttribute("data-bs-toggle"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        var targetId = toggle.GetAttribute("aria-controls")!;
        var container = cut.Find($"#{targetId}");
        Assert.Contains("collapse", container.ClassName);
        Assert.NotNull(container.QuerySelector("form[role='search']"));
    }

    [Fact]
    public void TheStaticBellIsReplacedByTheNotificationsMenu()
    {
        var cut = Render<AppNavbar>();

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("button[aria-label='Notifications']");
            Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));
            Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

            var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
            Assert.Contains("You have no notifications.", menu.TextContent);
            Assert.Empty(menu.QuerySelectorAll("a[href='/drafts']"));
            Assert.Empty(cut.FindAll(".badge"));
            Assert.NotNull(cut.Find("[data-testid='notifications-menu']"));
        });
    }

    [Fact]
    public void UnreadNotifications_AppearAsABadgeOnTheBell()
    {
        unread = new UnreadCountDto { Unread = 3, DraftsAwaitingReview = 4 };

        var cut = Render<AppNavbar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Notifications, 3 unread", cut.Find("button[data-bs-toggle='dropdown'][aria-label^='Notifications']").GetAttribute("aria-label"));
            Assert.Equal("3", cut.Find("[data-testid='notification-badge']").TextContent.Trim());
        });
    }

    [Fact]
    public void HelpMenuLinksToFaqAndAbout()
    {
        var cut = Render<AppNavbar>();

        var toggle = cut.Find("button[aria-label='Help']");
        Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));

        var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
        Assert.Equal("/faq", menu.QuerySelector("a[href='/faq']")!.GetAttribute("href"));
        Assert.Equal("FAQ", menu.QuerySelector("a[href='/faq']")!.TextContent.Trim());
        Assert.Equal("About", menu.QuerySelector("a[href='/about']")!.TextContent.Trim());
    }

    [Fact]
    public void ProfileMenuShowsInitialsNameMyVehiclesEmailDraftsPreferencesAndSignOut()
    {
        var cut = Render<AppNavbar>();

        var toggle = cut.Find("button[aria-label='Account menu for Ada Lovelace']");
        Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));
        Assert.Contains("AL", toggle.TextContent);

        var menu = toggle.ParentElement!.QuerySelector(".dropdown-menu")!;
        Assert.Contains("Ada Lovelace", menu.TextContent);
        Assert.Equal("My Vehicles", menu.QuerySelector("a[href='/vehicles']")!.TextContent.Trim());
        Assert.Equal("Preferences", menu.QuerySelector("a[href='/preferences']")!.TextContent.Trim());
        Assert.Equal(
            ["Ada Lovelace", "My Vehicles", "Drafts", "Preferences", "Sign out"],
            menu.QuerySelectorAll(".dropdown-item, .dropdown-item-text").Select(item => item.TextContent.Trim()));
        Assert.NotNull(menu.QuerySelector(".dropdown-divider"));

        var form = menu.QuerySelector("form")!;
        Assert.Equal("/authentication/logout", form.GetAttribute("action"));
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.NotNull(form.QuerySelector("input[name='__RequestVerificationToken']"));
        var signOut = form.QuerySelector("button[type='submit']")!;
        Assert.Equal("Sign out", signOut.TextContent.Trim());
        Assert.Contains("dropdown-item", signOut.ClassName);
    }

    [Fact]
    public void TheDraftsEntry_LinksToTheList_AndShowsTheAwaitingReviewCount()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 4 };

        var cut = Render<AppNavbar>();

        cut.WaitForAssertion(() =>
        {
            var entry = cut.Find(".dropdown-menu a.dropdown-item[href='/drafts']");
            Assert.StartsWith("Drafts", entry.TextContent.Trim());
            var count = entry.QuerySelector("[data-testid='account-drafts-count']")!;
            Assert.Equal("4", count.TextContent.Trim());
            Assert.Contains("4 awaiting review", entry.TextContent);
        });
    }

    [Fact]
    public void TheEmailDraftsEntry_ShowsNoCountWhenNothingIsWaiting()
    {
        var cut = Render<AppNavbar>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find(".dropdown-menu a.dropdown-item[href='/drafts']"));
            Assert.Empty(cut.FindAll("[data-testid='account-drafts-count']"));
        });
    }

    [Fact]
    public void WhenTheNotificationsApiFails_TheBarStillRenders_WithNoBadgeAndNoCount()
    {
        notificationsFailure = HttpStatusCode.InternalServerError;

        var cut = Render<AppNavbar>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("button[aria-label='Notifications']"));
            Assert.Empty(cut.FindAll("[data-testid='notification-badge']"));
            Assert.Empty(cut.FindAll("[data-testid='account-drafts-count']"));
            Assert.NotNull(cut.Find("button[aria-label='Account menu for Ada Lovelace']"));
        });
    }

    [Fact]
    public void DisplayNameIsEncoded()
    {
        auth.SetClaims(new Claim("name", "<b>x</b>"));

        var cut = Render<AppNavbar>();

        Assert.Empty(cut.FindAll("b"));
        var menuName = cut.Find(".dropdown-item-text");
        Assert.Equal("<b>x</b>", menuName.TextContent);
    }
}

using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Notifications;
using MyGarage.Web.Notifications;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Notifications;

public class NotificationsMenuTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider time = new(Now);
    private UnreadCountDto unread = new() { Unread = 0, DraftsAwaitingReview = 0 };
    private List<NotificationDto> latest = [];
    private HttpStatusCode? failure;

    public NotificationsMenuTests()
    {
        Services.AddSingleton<TimeProvider>(time);
    }

    private static NotificationDto Notification(string title = "New draft service record ready to review", Guid? draftId = null, bool isRead = false, TimeSpan? age = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = "draft_ready",
            Title = title,
            DraftId = draftId ?? Guid.NewGuid(),
            CreatedAt = Now - (age ?? TimeSpan.FromMinutes(5)),
            IsRead = isRead,
        };

    private IRenderedComponent<NotificationsMenu> RenderMenu(Action<ComponentParameterCollectionBuilder<NotificationsMenu>>? configure = null)
    {
        RegisterApi(request =>
        {
            if (failure is { } status)
            {
                return new HttpResponseMessage(status);
            }

            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/notifications/unread-count")
            {
                return Json(unread);
            }

            if (request.Method == HttpMethod.Get && path == "/api/notifications")
            {
                return Json(latest);
            }

            if (request.Method == HttpMethod.Post && path == "/api/notifications/read-all")
            {
                unread = new UnreadCountDto { Unread = 0, DraftsAwaitingReview = unread.DraftsAwaitingReview };
                latest = [.. latest.Select(item => new NotificationDto { Id = item.Id, Kind = item.Kind, Title = item.Title, DraftId = item.DraftId, CreatedAt = item.CreatedAt, IsRead = true })];
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete && path == "/api/notifications/read")
            {
                latest = [.. latest.Where(item => !item.IsRead)];
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/notifications/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path["/api/notifications/".Length..]);
                var removed = latest.FirstOrDefault(item => item.Id == id);
                latest = [.. latest.Where(item => item.Id != id)];
                if (removed is { IsRead: false })
                {
                    unread = new UnreadCountDto { Unread = unread.Unread - 1, DraftsAwaitingReview = unread.DraftsAwaitingReview };
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return Render<NotificationsMenu>(parameters => configure?.Invoke(parameters));
    }

    [Fact]
    public void WithUnreadNotifications_ADotIsShownAndTheButtonNamesTheCount()
    {
        unread = new UnreadCountDto { Unread = 3, DraftsAwaitingReview = 4 };
        latest = [Notification()];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("button[data-bs-toggle='dropdown']");
            Assert.Equal("Notifications, 3 unread", toggle.GetAttribute("aria-label"));
            Assert.Equal("Notifications, 3 unread", toggle.GetAttribute("title"));
            Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
            Assert.NotNull(cut.Find("[data-testid='notification-badge']"));
            Assert.Contains("mg-bell-ring", cut.Find("[data-testid='notification-bell']").ClassList);
        });
    }

    [Fact]
    public void WithNoUnreadNotifications_NoDotIsShownAndTheButtonIsJustNotifications()
    {
        latest = [Notification(isRead: true)];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Notifications", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label"));
            Assert.Empty(cut.FindAll("[data-testid='notification-badge']"));
            Assert.DoesNotContain("mg-bell-ring", cut.Find("[data-testid='notification-bell']").ClassList);
            Assert.Empty(cut.FindAll("form[data-testid='mark-all-read']"));
        });
    }

    [Fact]
    public void AVeryLargeCount_ShowsOneDotAndTheFullCountInTheName()
    {
        unread = new UnreadCountDto { Unread = 250, DraftsAwaitingReview = 250 };

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='notification-badge']"));
            Assert.Equal("Notifications, 250 unread", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void TheLatestNotifications_LinkToTheirDrafts_WithTheTimeSinceTheyArrived()
    {
        var first = Notification("New draft service record ready to review", age: TimeSpan.FromMinutes(5));
        var second = Notification("Another draft", isRead: true, age: TimeSpan.FromHours(3));
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 2 };
        latest = [first, second];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-testid='notification']");
            Assert.Equal(2, items.Count);
            Assert.Equal($"/drafts/{first.DraftId}", items[0].QuerySelector("a")!.GetAttribute("href"));
            Assert.Contains("New draft service record ready to review", items[0].TextContent);
            Assert.Contains("5 minutes ago", items[0].TextContent);
            Assert.Contains("3 hours ago", items[1].TextContent);
            Assert.Contains("fw-semibold", items[0].QuerySelector("a")!.InnerHtml);
            Assert.DoesNotContain("fw-semibold", items[1].QuerySelector("a")!.InnerHtml);
        });
    }

    [Fact]
    public void NoMoreThanTwentyAreShown_AndTheRequestAsksForTwenty()
    {
        unread = new UnreadCountDto { Unread = 25, DraftsAwaitingReview = 25 };
        latest = [.. Enumerable.Range(0, 25).Select(index => Notification($"Draft {index}"))];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(20, cut.FindAll("[data-testid='notification']").Count);
            Assert.Contains(Requests, request => request.RequestUri!.AbsolutePath == "/api/notifications" && request.RequestUri.Query.Contains("take=20", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void NotificationTitles_AreEncoded()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 1 };
        latest = [Notification("<b>Bold</b> draft")];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Contains("<b>Bold</b> draft", cut.Find("[data-testid='notification']").TextContent);
        });
    }

    [Fact]
    public void WithNoNotifications_TheMenuSaysSo_AndHasNoDraftsLinkOrMarkAllForm()
    {
        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You have no notifications.", cut.Find(".dropdown-menu").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='view-all-drafts']"));
            Assert.Empty(cut.FindAll("form[data-testid='mark-all-read']"));
            Assert.Empty(cut.FindAll(".dropdown-menu hr"));
        });
    }

    [Fact]
    public void MarkAllAsRead_CallsTheApi_AndClearsTheBadge()
    {
        unread = new UnreadCountDto { Unread = 2, DraftsAwaitingReview = 2 };
        latest = [Notification(), Notification()];
        var cut = RenderMenu();
        cut.WaitForElement("[data-testid='notification-badge']");

        var form = cut.Find("form[data-testid='mark-all-read']");
        Assert.Equal("Mark all as read", form.QuerySelector("button[type='submit']")!.TextContent.Trim());
        form.Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/notifications/read-all");
            Assert.Empty(cut.FindAll("[data-testid='notification-badge']"));
            Assert.Equal("Notifications", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void Dismiss_RemovesThatNotification_AndNamesItInTheButton()
    {
        var first = Notification("First draft");
        var second = Notification("Second draft", isRead: true);
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 2 };
        latest = [first, second];
        var cut = RenderMenu();
        cut.WaitForElement("[data-testid='notification']");

        var forms = cut.FindAll("form[data-testid='dismiss-notification']");
        Assert.Equal(2, forms.Count);
        Assert.Equal("Dismiss notification: First draft", forms[0].QuerySelector("button")!.GetAttribute("aria-label"));
        Assert.Equal("Dismiss notification", forms[0].QuerySelector("button")!.GetAttribute("title"));
        forms[1].Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Requests, request => request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath == $"/api/notifications/{second.Id}");
            var remaining = cut.FindAll("[data-testid='notification']");
            Assert.Single(remaining);
            Assert.Contains("First draft", remaining[0].TextContent);
        });
    }

    [Fact]
    public void ClearRead_IsOfferedOnlyWhenSomethingIsRead_AndRemovesTheReadOnes()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 1 };
        latest = [Notification("Unread draft"), Notification("Read draft", isRead: true)];
        var cut = RenderMenu();
        cut.WaitForElement("[data-testid='clear-read']");
        Assert.Equal("Remove all read notifications", cut.Find("form[data-testid='clear-read'] button").GetAttribute("title"));

        cut.Find("form[data-testid='clear-read']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Requests, request => request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath == "/api/notifications/read");
            var remaining = cut.FindAll("[data-testid='notification']");
            Assert.Single(remaining);
            Assert.Contains("Unread draft", remaining[0].TextContent);
            Assert.Empty(cut.FindAll("form[data-testid='clear-read']"));
        });
    }

    [Fact]
    public void WithOnlyUnreadNotifications_ClearReadIsNotOffered()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 1 };
        latest = [Notification()];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("form[data-testid='mark-all-read']"));
            Assert.Empty(cut.FindAll("form[data-testid='clear-read']"));
        });
    }

    [Fact]
    public void TheCountsAreReportedToTheParent()
    {
        unread = new UnreadCountDto { Unread = 3, DraftsAwaitingReview = 4 };
        UnreadCountDto? reported = null;

        var cut = RenderMenu(parameters => parameters.Add(menu => menu.OnCountLoaded, count => reported = count));

        cut.WaitForState(() => reported is not null);
        Assert.Equal(4, reported?.DraftsAwaitingReview);
        Assert.Equal(3, reported?.Unread);
    }

    [Fact]
    public void ItRefreshesEverySixtySeconds()
    {
        unread = new UnreadCountDto { Unread = 1, DraftsAwaitingReview = 1 };
        var cut = RenderMenu();
        cut.WaitForAssertion(() => Assert.Equal("Notifications, 1 unread", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label")));
        Assert.Equal(1, time.ActiveTimers);

        unread = new UnreadCountDto { Unread = 4, DraftsAwaitingReview = 4 };
        latest = [Notification("A newer draft")];
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("Notifications, 1 unread", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label"));

        time.Advance(TimeSpan.FromSeconds(1));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Notifications, 4 unread", cut.Find("button[data-bs-toggle='dropdown']").GetAttribute("aria-label"));
            Assert.Contains("A newer draft", cut.Markup);
        });
    }

    [Fact]
    public async Task TheTimerIsDisposedWithTheComponent()
    {
        var cut = RenderMenu();
        cut.WaitForAssertion(() => Assert.Equal(1, time.ActiveTimers));

        await DisposeComponentsAsync();

        Assert.Equal(0, time.ActiveTimers);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void WhenTheApiFails_NoBadgeIsShown_NothingIsThrown_AndTheBellStillWorks(HttpStatusCode status)
    {
        failure = status;

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("button[data-bs-toggle='dropdown']");
            Assert.Equal("Notifications", toggle.GetAttribute("aria-label"));
            Assert.Empty(cut.FindAll("[data-testid='notification-badge']"));
            Assert.Contains("Notifications are not available right now.", cut.Find(".dropdown-menu").TextContent);
        });
    }
}

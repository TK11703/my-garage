using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Notifications;
using MyGarage.Web.Notifications;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Notifications;

public class ReminderNotificationMenuTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private List<NotificationDto> latest = [];

    public ReminderNotificationMenuTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));

    private static NotificationDto ReminderNotification(Guid? vehicleId, Guid? reminderId, string kind = "reminder_overdue", bool isRead = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Title = "Oil change on 2020 Ford F-150 is overdue",
            ReminderId = reminderId,
            VehicleId = vehicleId,
            CreatedAt = Now.AddMinutes(-5),
            IsRead = isRead,
        };

    private IRenderedComponent<NotificationsMenu> RenderMenu()
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/notifications/unread-count" => Json(new UnreadCountDto { Unread = latest.Count(item => !item.IsRead), DraftsAwaitingReview = 0 }),
            "/api/notifications" => Json(latest),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        return Render<NotificationsMenu>();
    }

    [Fact]
    public void AReminderNotification_LinksToTheRemindersDetailPage_WithItsTitleUnreadDotAndDismissForm()
    {
        var vehicleId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        latest = [ReminderNotification(vehicleId, reminderId)];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("[data-testid='notification']");
            Assert.Equal($"/vehicles/{vehicleId}/reminders/{reminderId}", item.QuerySelector("a.mg-notif-link")!.GetAttribute("href"));
            Assert.Contains("Oil change on 2020 Ford F-150 is overdue", item.TextContent);
            Assert.NotNull(item.QuerySelector("svg.bi"));
            Assert.NotNull(item.QuerySelector(".mg-notif-unread"));
            Assert.NotNull(item.QuerySelector("form[data-testid='dismiss-notification']"));
            Assert.Contains("Unread:", item.TextContent);
        });
    }

    [Fact]
    public void ADueSoonReminderNotification_LinksTheSameWay()
    {
        var vehicleId = Guid.NewGuid();
        var reminderId = Guid.NewGuid();
        latest = [ReminderNotification(vehicleId, reminderId, "reminder_due_soon", isRead: true)];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("[data-testid='notification']");
            Assert.Equal($"/vehicles/{vehicleId}/reminders/{reminderId}", item.QuerySelector("a.mg-notif-link")!.GetAttribute("href"));
            Assert.Null(item.QuerySelector(".mg-notif-unread"));
        });
    }

    [Fact]
    public void ADraftNotification_StillLinksToTheDraft()
    {
        var draftId = Guid.NewGuid();
        latest =
        [
            new NotificationDto
            {
                Id = Guid.NewGuid(), Kind = "draft_ready", Title = "New draft service record ready to review", DraftId = draftId,
                CreatedAt = Now.AddMinutes(-1), IsRead = false,
            },
        ];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
            Assert.Equal($"/drafts/{draftId}", cut.Find("[data-testid='notification'] a.mg-notif-link").GetAttribute("href")));
    }

    [Fact]
    public void AReminderNotificationWithoutAVehicle_IsPlainTextNotABrokenLink()
    {
        latest = [ReminderNotification(null, Guid.NewGuid())];

        var cut = RenderMenu();

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("[data-testid='notification']");
            Assert.Empty(item.QuerySelectorAll("a.mg-notif-link"));
            Assert.Contains("Oil change on 2020 Ford F-150 is overdue", item.TextContent);
        });
    }
}

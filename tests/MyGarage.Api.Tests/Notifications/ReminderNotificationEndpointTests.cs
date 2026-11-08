using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Notifications;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Notifications;

public class ReminderNotificationEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> InsertAsync(string ownerId, Guid reminderId, string kind = "reminder_overdue", bool read = false, DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await factory.ExecuteScalarAsync(
            "INSERT INTO notifications (id, owner_id, kind, reminder_id, title, created_at, read_at) VALUES (@id, @ownerId, @kind, @reminderId, 'Oil change on 2020 Ford F-150 is overdue', @created, @readAt)",
            ("id", id), ("ownerId", ownerId), ("kind", kind), ("reminderId", reminderId), ("created", createdAt ?? DateTimeOffset.UtcNow),
            ("readAt", read ? DateTimeOffset.UtcNow : (DateTimeOffset?)null));
        return id;
    }

    private async Task<(Guid VehicleId, Guid ReminderId)> SeedReminderAsync(HttpClient client)
    {
        var vehicle = await CreateVehicleAsync(client);
        // A reminder that is not yet due leaves the notifier idle, so the tests control every notification.
        var reminder = await CreateAsync(client, vehicle.Id, Request(lastDoneOn: Today, lastDoneMileage: 100));
        return (vehicle.Id, reminder.Id);
    }

    [Fact]
    public async Task List_ReturnsReminderNotificationsWithTheirReminderAndVehicle_AndDraftOnesWithNone()
    {
        var alice = factory.CreateClientForUser(Alice);
        var (vehicleId, reminderId) = await SeedReminderAsync(alice);
        await factory.InsertNotificationAsync(Alice, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var notificationId = await InsertAsync(Alice, reminderId, "reminder_due_soon");

        var items = (await alice.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;

        Assert.Equal(["reminder_due_soon", "draft_ready"], items.Select(item => item.Kind));
        Assert.Equal(notificationId, items[0].Id);
        Assert.Equal(reminderId, items[0].ReminderId);
        Assert.Equal(vehicleId, items[0].VehicleId);
        Assert.Null(items[0].DraftId);
        Assert.Null(items[1].ReminderId);
        Assert.Null(items[1].VehicleId);
    }

    [Fact]
    public async Task UnreadCount_IncludesReminderNotifications()
    {
        var alice = factory.CreateClientForUser(Alice);
        var (_, reminderId) = await SeedReminderAsync(alice);
        await InsertAsync(Alice, reminderId);
        await InsertAsync(Alice, reminderId, read: true);

        var count = (await alice.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!;

        Assert.Equal(1, count.Unread);
    }

    [Fact]
    public async Task MarkRead_ReadAll_AndDelete_WorkForReminderNotifications()
    {
        var alice = factory.CreateClientForUser(Alice);
        var (_, reminderId) = await SeedReminderAsync(alice);
        var first = await InsertAsync(Alice, reminderId);
        var second = await InsertAsync(Alice, reminderId, "reminder_due_soon");
        var third = await InsertAsync(Alice, reminderId, "reminder_due_soon");

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync($"/api/notifications/{first}/read", null)).StatusCode);
        Assert.Equal(2, (await alice.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/notifications/{second}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await alice.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync("/api/notifications/read")).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!);
        Assert.NotEqual(Guid.Empty, third);
    }

    [Fact]
    public async Task AnotherUsersReminderNotification_IsNeverReturnedOrChanged()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var (_, reminderId) = await SeedReminderAsync(alice);
        var notificationId = await InsertAsync(Alice, reminderId);

        Assert.Empty((await bob.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/notifications/{notificationId}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/notifications/{notificationId}")).StatusCode);
        Assert.Equal(1, (await alice.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Unread);
    }
}

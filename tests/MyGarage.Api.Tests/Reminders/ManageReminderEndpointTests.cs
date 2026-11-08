using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Notifications;
using MyGarage.Shared.Reminders;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

public class ManageReminderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-manage-oid";
    private const string Bob = "bob-manage-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid vehicleId, Guid reminderId, ReminderRequest request) =>
        client.PutAsJsonAsync(Url(vehicleId, reminderId), request);

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, Guid vehicleId, Guid reminderId, string action) =>
        client.PostAsync(Url(vehicleId, reminderId, action), null);

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Unread;

    // ---- update ----

    [Fact]
    public async Task Put_RecalculatesTheNextDuePoint_AndReturnsTheUpdatedReminder()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 41_000);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(45_000, created.NextDueMileage);

        var response = await PutAsync(client, vehicle.Id, created.Id, Request("Oil change", miles: 10_000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ReminderDto>())!;
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(50_000, updated.NextDueMileage);
        Assert.Equal(9_000, updated.MilesRemaining);
    }

    [Fact]
    public async Task Put_ChangesTheNameAndTheTimeInterval()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var updated = (await (await PutAsync(client, vehicle.Id, created.Id, Request(
            "Hitch check", miles: null, time: Time(2, ReminderTimeUnit.Years),
            lastDoneOn: new DateOnly(2026, 9, 1), lastDoneMileage: null))).Content.ReadFromJsonAsync<ReminderDto>())!;

        Assert.Equal("Hitch check", updated.Name);
        Assert.Null(updated.IntervalMiles);
        Assert.Equal(new DateOnly(2028, 9, 1), updated.NextDueDate);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400_AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await PutAsync(client, vehicle.Id, created.Id, new ReminderRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Oil change", (await ListAsync(client, vehicle.Id)).Single().Name);
    }

    [Fact]
    public async Task Put_AnotherUsersAnotherVehiclesAndUnknownReminders_Return404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var other = await CreateVehicleAsync(alice);
        var created = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(bob, vehicle.Id, created.Id, Request("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(alice, other.Id, created.Id, Request("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(alice, vehicle.Id, Guid.NewGuid(), Request("X"))).StatusCode);
        Assert.Equal("Oil change", (await ListAsync(alice, vehicle.Id)).Single().Name);
    }

    [Fact]
    public async Task Put_EditingAnOverdueReminderBackToUpcoming_ReadsItsNotificationsAndResetsTheNotifiedState()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(1, await UnreadAsync(client));
        Assert.Equal("overdue", await factory.NotifiedStateAsync(created.Id));

        var response = await PutAsync(client, vehicle.Id, created.Id, Request("Oil change", miles: 20_000, lastDoneMileage: 40_000));

        Assert.Equal(ReminderStatus.Upcoming, (await response.Content.ReadFromJsonAsync<ReminderDto>())!.Status);
        Assert.Equal(0, await UnreadAsync(client));
        Assert.Null(await factory.NotifiedStateAsync(created.Id));
    }

    [Fact]
    public async Task Put_EditingAReminderIntoAnOverdueState_RaisesTheNotification()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        var created = await CreateAsync(client, vehicle.Id, Request(miles: 20_000));
        Assert.Empty(await factory.NotificationKindsAsync(Alice));

        await PutAsync(client, vehicle.Id, created.Id, Request(miles: 5_000));

        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, created.Id));
    }

    // ---- delete ----

    [Fact]
    public async Task Delete_Returns204_AndTheReminderIsGone()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(vehicle.Id, created.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, created.Id))).StatusCode);
        Assert.Empty(await ListAsync(client, vehicle.Id));
    }

    [Fact]
    public async Task Delete_RemovesTheRemindersNotifications()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(1, await UnreadAsync(client));

        await client.DeleteAsync(Url(vehicle.Id, created.Id));

        Assert.Equal(0, await UnreadAsync(client));
        Assert.Empty(await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task Delete_AnotherUsersOrUnknownReminders_Return404_AndDeleteNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var created = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync(Url(vehicle.Id, Guid.NewGuid()))).StatusCode);
        Assert.Single(await ListAsync(alice, vehicle.Id));
    }

    [Fact]
    public async Task DeletingAVehicle_RemovesItsReminders()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Empty(await client.GetFromJsonAsync<List<ReminderDto>>("/api/reminders") ?? []);
    }

    // ---- pause and resume ----

    [Fact]
    public async Task Pause_ShowsPaused_ReadsItsNotifications_IsIdempotent_AndLeavesTheOverdueCount()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(1, await UnreadAsync(client));

        var first = await PostActionAsync(client, vehicle.Id, created.Id, "pause");
        var second = await PostActionAsync(client, vehicle.Id, created.Id, "pause");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(ReminderStatus.Paused, (await first.Content.ReadFromJsonAsync<ReminderDto>())!.Status);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(0, await UnreadAsync(client));
        Assert.Empty(await client.GetFromJsonAsync<List<ReminderDto>>("/api/reminders?status=overdue") ?? []);
    }

    [Fact]
    public async Task APausedReminder_NeverRaisesANotification_EvenWhenAVehicleWriteChecksIt()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);
        await PostActionAsync(client, vehicle.Id, created.Id, "pause");

        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);

        Assert.Empty(await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task Resume_RecomputesTheStatus_AndRaisesANewlyDueNotificationOnce_AndIsIdempotent()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);
        await PostActionAsync(client, vehicle.Id, created.Id, "pause");
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        Assert.Empty(await factory.NotificationKindsAsync(Alice));

        var first = await PostActionAsync(client, vehicle.Id, created.Id, "resume");
        var second = await PostActionAsync(client, vehicle.Id, created.Id, "resume");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var resumed = (await first.Content.ReadFromJsonAsync<ReminderDto>())!;
        Assert.False(resumed.IsPaused);
        Assert.Equal(ReminderStatus.Overdue, resumed.Status);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, created.Id));
    }

    [Fact]
    public async Task PauseAndResume_AnotherUsersAndUnknownReminders_Return404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var created = await CreateAsync(alice, vehicle.Id);

        foreach (var action in new[] { "pause", "resume" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PostActionAsync(bob, vehicle.Id, created.Id, action)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostActionAsync(alice, vehicle.Id, Guid.NewGuid(), action)).StatusCode);
        }

        Assert.False((await alice.GetFromJsonAsync<ReminderDetailDto>(Url(vehicle.Id, created.Id)))!.IsPaused);
    }

    [Fact]
    public async Task TheManagingEndpoints_RequireASignedInUser_AndRefuseAppOnlyTokens()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var app = factory.CreateClientForWorkflow();
        var id = Guid.NewGuid();

        foreach (var client in new[] { anonymous, app })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await PutAsync(client, id, id, Request())).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync(Url(id, id))).StatusCode);
            Assert.Equal(expected, (await PostActionAsync(client, id, id, "pause")).StatusCode);
            Assert.Equal(expected, (await PostActionAsync(client, id, id, "resume")).StatusCode);
        }
    }
}

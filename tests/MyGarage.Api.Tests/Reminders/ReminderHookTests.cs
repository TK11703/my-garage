using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Notifications;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

// The writes that can change a status check the vehicle's reminders at once, so the owner hears about it when the mileage is saved.
public class ReminderHookTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-hook-oid";
    private const string Bob = "bob-hook-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class ThrowingEvaluator(ReminderRepository repository, TimeProvider clock, ILogger<ReminderEvaluator> logger)
        : ReminderEvaluator(repository, clock, logger)
    {
        public override Task<int> EvaluateVehicleAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("injected");
    }

    private HttpClient ClientWithFailingEvaluator(string oid) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ReminderEvaluator>();
            services.AddSingleton<ReminderEvaluator>(sp => new ThrowingEvaluator(
                sp.GetRequiredService<ReminderRepository>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<ReminderEvaluator>>()));
        })).CreateUserClient(oid);

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Unread;

    private static Task<HttpResponseMessage> PutVehicleAsync(HttpClient client, VehicleDetailDto vehicle, int latestOdometer) =>
        client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", new VehicleRequest { Make = vehicle.Make, Model = vehicle.Model, Year = vehicle.Year, LatestOdometerReading = latestOdometer });

    [Fact]
    public async Task CreatingAReminderThatIsAlreadyOverdue_RaisesOneNotification()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);

        var reminder = await CreateAsync(client, vehicle.Id);

        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder.Id));
        Assert.Equal(1, await UnreadAsync(client));
    }

    [Fact]
    public async Task CreatingAnUpcomingReminder_RaisesNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 41_000);

        await CreateAsync(client, vehicle.Id);

        Assert.Empty(await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task SavingAVehicleMileagePastTheDuePoint_RaisesTheNotificationAtThatTime()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var reminder = await CreateAsync(client, vehicle.Id);
        Assert.Equal(ReminderStatus.MileageNeeded, reminder.Status);
        Assert.Empty(await factory.NotificationKindsAsync(Alice));

        Assert.Equal(HttpStatusCode.OK, (await PutVehicleAsync(client, vehicle, 44_800)).StatusCode);
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Alice, reminder.Id));

        Assert.Equal(HttpStatusCode.OK, (await PutVehicleAsync(client, vehicle, 45_100)).StatusCode);
        Assert.Equal(["reminder_due_soon", "reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder.Id));
    }

    [Fact]
    public async Task ServiceRecordCreateUpdateAndDelete_CheckTheVehiclesReminders()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var reminder = await CreateAsync(client, vehicle.Id);

        var record = await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Alice, reminder.Id));

        var update = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}", new ServiceRecordRequest
        {
            DateServiced = new DateOnly(2026, 6, 1),
            OdometerReading = 45_100,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(["reminder_due_soon", "reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
        Assert.Equal(2, (await factory.NotificationKindsAsync(Alice, reminder.Id)).Count);
        Assert.Null(await factory.NotifiedStateAsync(reminder.Id));
    }

    [Fact]
    public async Task MarkingAReminderDone_ReadsItsNotifications_AndStartsTheNextCycleClean()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        var reminder = await CreateAsync(client, vehicle.Id);
        Assert.Equal(1, await UnreadAsync(client));

        var response = await client.PostAsJsonAsync(Url(vehicle.Id, reminder.Id, "complete"), new CompleteReminderRequest { DoneOn = Today, Mileage = 50_000 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await UnreadAsync(client));
        Assert.Null(await factory.NotifiedStateAsync(reminder.Id));
        Assert.Single(await factory.NotificationKindsAsync(Alice, reminder.Id));
    }

    [Fact]
    public async Task AVehicleHook_NeverEvaluatesAnotherUsersReminders()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var bobsVehicle = await factory.InsertVehicleAsync(Bob, latestOdometer: 50_000);
        await factory.InsertReminderAsync(bobsVehicle);

        await PutVehicleAsync(alice, vehicle, 50_000);

        Assert.Empty(await factory.NotificationKindsAsync(Bob));
    }

    [Fact]
    public async Task AFailingCheck_NeverFailsTheWriteThatTriggeredIt()
    {
        var client = ClientWithFailingEvaluator(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var created = await PostAsync(client, vehicle.Id, Request());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reminder = (await created.Content.ReadFromJsonAsync<ReminderDto>())!;

        Assert.Equal(HttpStatusCode.OK, (await PutVehicleAsync(client, vehicle, 50_000)).StatusCode);
        var record = await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records", new ServiceRecordRequest { DateServiced = new DateOnly(2026, 6, 1), OdometerReading = 51_000 });
        Assert.Equal(HttpStatusCode.Created, record.StatusCode);
        var complete = await client.PostAsJsonAsync(Url(vehicle.Id, reminder.Id, "complete"), new CompleteReminderRequest { DoneOn = Today, Mileage = 51_000 });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Empty(await factory.NotificationKindsAsync(Alice));
    }
}

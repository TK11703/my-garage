using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

public class CompleteReminderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid vehicleId, Guid reminderId, CompleteReminderRequest request) =>
        client.PostAsJsonAsync(Url(vehicleId, reminderId, "complete"), request);

    private static CompleteReminderRequest Done(DateOnly? doneOn = null, int? mileage = 46_200, bool updateVehicle = false) =>
        new() { DoneOn = doneOn ?? Today, Mileage = mileage, UpdateVehicleMileage = updateVehicle };

    [Fact]
    public async Task Complete_RollsAnOverdueOilChangeForward_AndListsTheCompletionFirst()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 46_200);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(ReminderStatus.Overdue, created.Status);

        var response = await CompleteAsync(client, vehicle.Id, created.Id, Done());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<ReminderDetailDto>())!;
        Assert.Equal(46_200, detail.LastDoneMileage);
        Assert.Equal(Today, detail.LastDoneOn);
        Assert.Equal(51_200, detail.NextDueMileage);
        Assert.Equal(ReminderStatus.Upcoming, detail.Status);
        var completion = Assert.Single(detail.Completions);
        Assert.Equal(Today, completion.DoneOn);
        Assert.Equal(46_200, completion.Mileage);
    }

    [Fact]
    public async Task Complete_ATimeReminder_RollsTheNextDueDateFromTheDateDone()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id, Request(
            "Inspection", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddYears(-1).AddDays(-5), lastDoneMileage: null));
        Assert.Equal(ReminderStatus.Overdue, created.Status);

        var detail = (await (await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-2), mileage: null)))
            .Content.ReadFromJsonAsync<ReminderDetailDto>())!;

        Assert.Equal(Today.AddDays(-2).AddYears(1), detail.NextDueDate);
        Assert.Equal(ReminderStatus.Upcoming, detail.Status);
        Assert.Null(detail.Completions.Single().Mileage);
        Assert.Null(detail.LastDoneMileage);
    }

    [Fact]
    public async Task Complete_SeveralTimes_ListsTheCompletionsMostRecentFirst()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-20), 42_000));
        var response = await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-1), 46_000));

        var detail = (await response.Content.ReadFromJsonAsync<ReminderDetailDto>())!;
        Assert.Equal([46_000, 42_000], detail.Completions.Select(item => item.Mileage));
        Assert.Equal(detail.Completions.Count, (await client.GetFromJsonAsync<ReminderDetailDto>(Url(vehicle.Id, created.Id)))!.Completions.Count);
    }

    [Fact]
    public async Task Complete_ADistanceReminderWithoutAMileage_Returns400OnMileage_AndChangesNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await CompleteAsync(client, vehicle.Id, created.Id, Done(mileage: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, "mileage", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await client.GetFromJsonAsync<ReminderDetailDto>(Url(vehicle.Id, created.Id)))!.Completions);
    }

    [Fact]
    public async Task Complete_AMileageBelowTheLastDoneMileage_Returns400OnMileage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id, Request(lastDoneMileage: 40_000));

        var response = await CompleteAsync(client, vehicle.Id, created.Id, Done(mileage: 39_999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, "mileage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Complete_AMileageEqualToTheLastDoneMileage_IsAccepted()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id, Request(lastDoneMileage: 40_000));

        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, vehicle.Id, created.Id, Done(mileage: 40_000))).StatusCode);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(0, -1)]
    [InlineData(0, 10_000_000)]
    public async Task Complete_InvalidValues_Return400_AndStoreNothing(int daysAhead, int? mileage)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(daysAhead), mileage));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ReminderDetailDto>(Url(vehicle.Id, created.Id)))!.Completions);
    }

    [Fact]
    public async Task Complete_UpdateVehicleMileage_RaisesTheLatestOdometerReadingOnlyWhenAskedAndHigher()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-3), 46_200, updateVehicle: false));
        Assert.Null((await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.LatestOdometerReading);

        await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-2), 47_000, updateVehicle: true));
        Assert.Equal(47_000, (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.LatestOdometerReading);

        await CompleteAsync(client, vehicle.Id, created.Id, Done(Today.AddDays(-1), 47_000, updateVehicle: true));
        Assert.Equal(47_000, (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.LatestOdometerReading);
    }

    [Fact]
    public async Task Complete_AnotherUsersAnotherVehiclesAndUnknownReminders_Return404_AndChangeNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var other = await CreateVehicleAsync(alice);
        var created = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(bob, vehicle.Id, created.Id, Done())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(alice, other.Id, created.Id, Done())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(alice, vehicle.Id, Guid.NewGuid(), Done())).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<ReminderDetailDto>(Url(vehicle.Id, created.Id)))!.Completions);
    }

    [Fact]
    public async Task Complete_RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(factory.CreateClientWithToken(null), id, id, Done())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CompleteAsync(factory.CreateClientForWorkflow(), id, id, Done())).StatusCode);
    }
}

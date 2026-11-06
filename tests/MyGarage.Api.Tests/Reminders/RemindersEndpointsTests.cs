using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

public class RemindersEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_Returns201WithLocationAndTheReminder_ForADistanceInterval()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reminder = await response.Content.ReadFromJsonAsync<ReminderDto>();
        Assert.NotNull(reminder);
        Assert.Equal(vehicle.Id, reminder.VehicleId);
        Assert.Equal("2020 Ford F-150", reminder.VehicleName);
        Assert.Equal("Oil change", reminder.Name);
        Assert.Equal(ReminderAppliesTo.Vehicle, reminder.AppliesTo);
        Assert.Null(reminder.Subject);
        Assert.Equal(5_000, reminder.IntervalMiles);
        Assert.Equal(45_000, reminder.NextDueMileage);
        Assert.Null(reminder.NextDueDate);
        Assert.False(reminder.IsPaused);
        Assert.EndsWith(Url(vehicle.Id, reminder.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_ATimeInterval_ShowsTheNextDueDate()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var reminder = await CreateAsync(client, vehicle.Id, Request(
            "Inspection", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: new DateOnly(2026, 3, 15), lastDoneMileage: null));

        Assert.Equal(new DateOnly(2027, 3, 15), reminder.NextDueDate);
        Assert.Equal(1, reminder.IntervalTime!.Value);
        Assert.Equal(ReminderTimeUnit.Years, reminder.IntervalTime.Unit);
        Assert.Null(reminder.NextDueMileage);
    }

    [Fact]
    public async Task Post_BothIntervals_ShowBothDuePoints()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var reminder = await CreateAsync(client, vehicle.Id, Request(time: Time(6, ReminderTimeUnit.Months), lastDoneOn: Today.AddDays(-10)));

        Assert.Equal(45_000, reminder.NextDueMileage);
        Assert.Equal(Today.AddDays(-10).AddMonths(6), reminder.NextDueDate);
    }

    [Theory]
    [InlineData(ReminderAppliesTo.Equipment, "Trailer hitch")]
    [InlineData(ReminderAppliesTo.Location, "Home garage permit")]
    public async Task Post_EquipmentAndLocationReminders_KeepTheirSubject(ReminderAppliesTo appliesTo, string subject)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var reminder = await CreateAsync(client, vehicle.Id, Request("Check", appliesTo, subject));

        Assert.Equal(appliesTo, reminder.AppliesTo);
        Assert.Equal(subject, reminder.Subject);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField_AndStoreNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, new ReminderRequest
        {
            Name = " ",
            AppliesTo = ReminderAppliesTo.Equipment,
            IntervalMiles = 0,
            LastDoneOn = Today.AddDays(1),
            LastDoneMileage = -1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "name", "subject", "intervalMiles", "lastDoneOn", "lastDoneMileage" })
        {
            Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Empty(await ListAsync(client, vehicle.Id));
    }

    [Fact]
    public async Task Post_NoInterval_Return400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(miles: null, lastDoneMileage: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_AnotherUsersAndUnknownVehicles_AnswerTheSame404_AndStoreNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        var foreign = await PostAsync(bob, vehicle.Id, Request());
        var unknown = await PostAsync(bob, Guid.NewGuid(), Request());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(await ListAsync(alice, vehicle.Id));
    }

    [Fact]
    public async Task Post_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        var response = await bob.PostAsJsonAsync(Url(vehicle.Id), new
        {
            name = "Oil change",
            appliesTo = "vehicle",
            intervalMiles = 5000,
            lastDoneOn = Today.AddDays(-1).ToString("yyyy-MM-dd"),
            lastDoneMileage = 1,
            ownerId = Bob,
            userId = Bob,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ListAsync(alice, vehicle.Id));
    }

    [Fact]
    public async Task List_ReturnsTheVehiclesReminders_AndAnEmptyArrayForNone()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);

        Assert.Empty(await ListAsync(client, vehicle.Id));

        var created = await CreateAsync(client, vehicle.Id);
        await CreateAsync(client, other.Id, Request("Elsewhere"));

        var list = await ListAsync(client, vehicle.Id);
        Assert.Equal([created.Id], list.Select(item => item.Id));
    }

    [Fact]
    public async Task List_AnotherUsersVehicle_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsTheReminderWithAnEmptyHistory()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.GetAsync(Url(vehicle.Id, created.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ReminderDetailDto>();
        Assert.Equal(created.Id, detail!.Id);
        Assert.Equal(created.NextDueMileage, detail.NextDueMileage);
        Assert.Empty(detail.Completions);
    }

    [Fact]
    public async Task Get_AnotherUsersAnotherVehiclesAndUnknownReminders_Return404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var other = await CreateVehicleAsync(alice);
        var created = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Url(other.Id, created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Url(vehicle.Id, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task TheVehiclesCurrentMileage_DrivesTheStatus()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);
        Assert.Equal(ReminderStatus.MileageNeeded, created.Status);

        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        var reminder = (await ListAsync(client, vehicle.Id)).Single();

        Assert.Equal(200, reminder.MilesRemaining);
        Assert.Equal(ReminderStatus.DueSoon, reminder.Status);
    }

    [Fact]
    public async Task Endpoints_RequireASignedInUser_AndRefuseAppOnlyTokens()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var app = factory.CreateClientForWorkflow();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(app, id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync(Url(id, id))).StatusCode);
    }
}

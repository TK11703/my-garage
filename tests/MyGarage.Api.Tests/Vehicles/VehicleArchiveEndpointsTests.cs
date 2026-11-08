using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Tests.Reminders;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

[Collection("Logging")]
public class VehicleArchiveEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-archive-oid";
    private const string Bob = "bob-archive-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, string model = "F-150", string make = "Ford")
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = make, Model = model, Year = 2020 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static Task<HttpResponseMessage> ArchiveAsync(HttpClient client, Guid id) => client.PostAsync($"/api/vehicles/{id}/archive", null);

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid id) => client.PostAsync($"/api/vehicles/{id}/restore", null);

    private static async Task<PagedResultDto<VehicleSummaryDto>> ListAsync(HttpClient client, string? status = null) =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>(status is null ? "/api/vehicles" : $"/api/vehicles?status={status}"))!;

    [Fact]
    public async Task Archive_Returns200WithArchivedAt_AndTheVehicleLeavesTheDefaultListButStaysReadable()
    {
        var client = factory.CreateClientForUser(Alice);
        var keep = await CreateAsync(client, "Outback", "Subaru");
        var vehicle = await CreateAsync(client);

        var response = await ArchiveAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.NotNull(archived.ArchivedAt);
        Assert.InRange(archived.ArchivedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal([keep.Id], (await ListAsync(client)).Items.Select(item => item.Id));
        Assert.Equal([keep.Id], (await ListAsync(client, "active")).Items.Select(item => item.Id));
        var onlyArchived = await ListAsync(client, "archived");
        Assert.Equal([vehicle.Id], onlyArchived.Items.Select(item => item.Id));
        Assert.NotNull(onlyArchived.Items.Single().ArchivedAt);
        Assert.Equal(2, (await ListAsync(client, "all")).TotalCount);
        Assert.Equal(archived.ArchivedAt, (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.ArchivedAt);
    }

    [Fact]
    public async Task Archive_KeepsEverythingAttached()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);
        await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/tags", new AddVehicleTagRequest { Name = "sunroof" });
        await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes", new JournalNoteRequest { Body = "kept note" });
        await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records", new ServiceRecordRequest { DateServiced = new DateOnly(2026, 1, 1), OdometerReading = 100 });
        await factory.InsertReminderAsync(vehicle.Id);

        await ArchiveAsync(client, vehicle.Id);

        var detail = (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!;
        Assert.Single(detail.Tags);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResultDto<JournalNoteDto>>($"/api/vehicles/{vehicle.Id}/notes"))!.TotalCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResultDto<ServiceRecordSummaryDto>>($"/api/vehicles/{vehicle.Id}/service-records"))!.TotalCount);
        Assert.Single((await client.GetFromJsonAsync<List<ReminderDto>>($"/api/vehicles/{vehicle.Id}/reminders"))!);
    }

    [Fact]
    public async Task FilterOptions_FollowTheStatus_AndABadStatusIsAValidationProblem()
    {
        var client = factory.CreateClientForUser(Alice);
        await CreateAsync(client);
        var subaru = await CreateAsync(client, "Outback", "Subaru");
        await ArchiveAsync(client, subaru.Id);

        Assert.Equal(["Ford"], (await client.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options"))!.Makes);
        Assert.Equal(["Subaru"], (await client.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options?status=archived"))!.Makes);
        Assert.Equal(["Ford", "Subaru"], (await client.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options?status=all"))!.Makes);

        var bad = await client.GetAsync("/api/vehicles/filter-options?status=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("status", (await bad.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_WithABadStatus_Returns400NamingStatus()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.GetAsync("/api/vehicles?status=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("status", (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchivingTwice_Returns409SayingItIsAlreadyArchived()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);
        await ArchiveAsync(client, vehicle.Id);

        var second = await ArchiveAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already archived", (await second.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Archive_AnUnknownOrAnotherUsersVehicle_IsTheSame404_AndChangesNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateAsync(alice);

        var unknown = await ArchiveAsync(alice, Guid.NewGuid());
        var foreign = await ArchiveAsync(bob, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal((await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!.Title, (await foreign.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Null((await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.ArchivedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await RestoreAsync(bob, vehicle.Id)).StatusCode);
    }

    [Fact]
    public async Task ArchivingOneUsersVehicle_ChangesNothingInAnotherUsersList()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var bobs = await CreateAsync(bob);
        var vehicle = await CreateAsync(alice);

        await ArchiveAsync(alice, vehicle.Id);

        Assert.Equal([bobs.Id], (await ListAsync(bob)).Items.Select(item => item.Id));
        Assert.Empty((await ListAsync(bob, "archived")).Items);
    }

    [Fact]
    public async Task ArchiveAndRestore_RequireAUserToken()
    {
        var id = Guid.NewGuid();
        var anonymous = factory.CreateClientWithToken(null);
        var app = factory.CreateClientForWorkflow();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ArchiveAsync(anonymous, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RestoreAsync(anonymous, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ArchiveAsync(app, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RestoreAsync(app, id)).StatusCode);
    }

    [Fact]
    public async Task Restore_Returns200WithoutArchivedAt_AndTheVehicleIsListedAndWritableAgain()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);
        await ArchiveAsync(client, vehicle.Id);

        var response = await RestoreAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!.ArchivedAt);
        Assert.Equal([vehicle.Id], (await ListAsync(client)).Items.Select(item => item.Id));
        Assert.Empty((await ListAsync(client, "archived")).Items);
        var record = await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records", new ServiceRecordRequest { DateServiced = new DateOnly(2026, 1, 1), OdometerReading = 1 });
        Assert.Equal(HttpStatusCode.Created, record.StatusCode);
    }

    [Fact]
    public async Task Restoring_AnActiveVehicle_Returns409SayingItIsNotArchived()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);

        var response = await RestoreAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not archived", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Restore_AnUnknownVehicle_Is404()
    {
        var client = factory.CreateClientForUser(Alice);

        Assert.Equal(HttpStatusCode.NotFound, (await RestoreAsync(client, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Restore_KeepsAUserPausedReminderPaused_AndTheRemindersEvaluateOnce()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);
        await factory.SetLatestOdometerAsync(vehicle.Id, 50_000);
        var paused = await factory.InsertReminderAsync(vehicle.Id, "Paused one", paused: true);
        var worsened = await factory.InsertReminderAsync(vehicle.Id, "Worsened while archived");
        var alreadyTold = await factory.InsertReminderAsync(vehicle.Id, "Already told", notifiedState: "overdue");
        await ArchiveAsync(client, vehicle.Id);

        await RestoreAsync(client, vehicle.Id);

        var reminders = (await client.GetFromJsonAsync<List<ReminderDto>>($"/api/vehicles/{vehicle.Id}/reminders"))!.ToDictionary(item => item.Id);
        Assert.Equal(ReminderStatus.Paused, reminders[paused].Status);
        Assert.False(reminders[paused].VehicleArchived);
        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, worsened));
        Assert.Empty(await factory.NotificationKindsAsync(Alice, alreadyTold));
        Assert.Empty(await factory.NotificationKindsAsync(Alice, paused));
    }

    [Fact]
    public async Task Delete_WorksForAnArchivedVehicle_AndRemovesEverythingAttached()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client);
        await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes", new JournalNoteRequest { Body = "gone soon" });
        await factory.InsertReminderAsync(vehicle.Id);
        await ArchiveAsync(client, vehicle.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
        Assert.Equal(0, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM reminders"))!);
        Assert.Equal(0, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM journal_notes"))!);
        Assert.Empty((await ListAsync(client, "all")).Items);
    }
}

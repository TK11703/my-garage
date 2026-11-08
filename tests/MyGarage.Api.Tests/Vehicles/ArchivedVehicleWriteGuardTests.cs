using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Tests.Reminders;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

// An archived vehicle refuses every change under it before validation, and reads still work (specs/014-archive-vehicle).
[Collection("Logging")]
public partial class ArchivedVehicleWriteGuardTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-guard-oid";
    private const string Bob = "bob-guard-oid";
    private const string ArchivedType = "urn:mygarage:problem:vehicle-archived";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[16]];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [GeneratedRegex(@"\{(\w+)(:\w+)?\}")]
    private static partial Regex RouteParameter();

    private static string Url(string pattern, Guid vehicleId) =>
        RouteParameter().Replace(pattern, match => match.Groups[1].Value == "vehicleId" ? vehicleId.ToString() : Guid.NewGuid().ToString());

    private static HttpRequestMessage Build(VehicleWriteRoutes.Route route, Guid vehicleId)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), Url(route.Pattern, vehicleId));
        request.Content = route.Pattern.TrimEnd('/').EndsWith("/images", StringComparison.Ordinal) && route.Method == "POST"
            ? new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "x.png" } }
            : new StringContent("{}", Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<long> RowCountAsync() =>
        (long)(await factory.ExecuteScalarAsync(
            "SELECT (SELECT count(*) FROM vehicles) + (SELECT count(*) FROM journal_notes) + (SELECT count(*) FROM vehicle_tags) + (SELECT count(*) FROM service_records) " +
            "+ (SELECT count(*) FROM reminders) + (SELECT count(*) FROM vehicle_images) + (SELECT count(*) FROM fuel_energy_entries) + (SELECT count(*) FROM registrations) " +
            "+ (SELECT count(*) FROM inspections) + (SELECT count(*) FROM insurance_policies) + (SELECT count(*) FROM taxes) + (SELECT count(*) FROM vehicle_documents)"))!;

    private async Task<(HttpClient Client, VehicleDetailDto Vehicle)> ArchivedVehicleWithHistoryAsync()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/tags", new AddVehicleTagRequest { Name = "sunroof" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes", new JournalNoteRequest { Body = "kept" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records", new ServiceRecordRequest { DateServiced = new DateOnly(2026, 1, 1), OdometerReading = 10 })).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/vehicles/{vehicle.Id}/images", new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "a.png" } })).EnsureSuccessStatusCode();
        await factory.InsertReminderAsync(vehicle.Id);
        (await client.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).EnsureSuccessStatusCode();
        return (client, vehicle);
    }

    [Fact]
    public async Task EveryChangeRouteOnAnArchivedVehicle_Returns409WithTheArchivedType_AndChangesNothing()
    {
        var (client, vehicle) = await ArchivedVehicleWithHistoryAsync();
        var before = await RowCountAsync();
        var routes = VehicleWriteRoutes.All(factory).Where(route => !VehicleWriteRoutes.IsAllowListed(route.Method, route.Pattern)).ToList();
        Assert.True(routes.Count > 30);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            using var request = Build(route, vehicle.Id);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Conflict || !body.Contains(ArchivedType, StringComparison.Ordinal))
            {
                failures.Add($"{route}: {(int)response.StatusCode} {body[..Math.Min(body.Length, 80)]}");
            }
        }

        Assert.Empty(failures);
        Assert.Equal(before, await RowCountAsync());
    }

    [Fact]
    public async Task TheRefusalComesBeforeValidation_AnInvalidBodyIsStill409()
    {
        var (client, vehicle) = await ArchivedVehicleWithHistoryAsync();

        var put = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", new VehicleRequest());
        var post = await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/service-records", new ServiceRecordRequest());

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Contains("archived", (await post.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadsOnAnArchivedVehicle_StillSucceedWithTheKeptData()
    {
        var (client, vehicle) = await ArchivedVehicleWithHistoryAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResultDto<JournalNoteDto>>($"/api/vehicles/{vehicle.Id}/notes"))!.TotalCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResultDto<ServiceRecordSummaryDto>>($"/api/vehicles/{vehicle.Id}/service-records"))!.TotalCount);
        var detail = (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!;
        Assert.Single(detail.Tags);
        var image = Assert.Single(detail.Images);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{vehicle.Id}/reminders")).StatusCode);
    }

    [Fact]
    public async Task AnotherUsersAndAnUnknownVehicle_NeverGetTheArchivedRefusal()
    {
        var (_, vehicle) = await ArchivedVehicleWithHistoryAsync();
        var bob = factory.CreateClientForUser(Bob);
        var routes = VehicleWriteRoutes.All(factory).Where(route => !VehicleWriteRoutes.IsAllowListed(route.Method, route.Pattern)).ToList();

        var leaks = new List<string>();
        foreach (var route in routes)
        {
            foreach (var target in new[] { vehicle.Id, Guid.NewGuid() })
            {
                using var request = Build(route, target);
                using var response = await bob.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    leaks.Add($"{route} on {(target == vehicle.Id ? "another user's vehicle" : "an unknown vehicle")}");
                }
            }
        }

        Assert.Empty(leaks);
    }

    [Fact]
    public async Task ArchiveRestoreAndDelete_AreNotBlocked_AndAnActiveVehicleIsUnaffected()
    {
        var (client, vehicle) = await ArchivedVehicleWithHistoryAsync();
        var active = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Subaru", Model = "Outback", Year = 2019 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/vehicles/{active.Id}/notes", new JournalNoteRequest { Body = "fine" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes", new JournalNoteRequest { Body = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
    }
}

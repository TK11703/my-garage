using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyGarage.Api.Drafts;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Drafts;

// An archived vehicle is never offered for new work, and a draft that named it reads as having no vehicle (specs/014-archive-vehicle).
[Collection("Logging")]
public class ArchivedVehicleDraftsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-archdraft-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    private static DraftProposal ServiceProposal() =>
        new()
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = new DateOnly(2025, 10, 20),
                OdometerReading = 45_500,
                ShopOrPerson = "Joe's Garage",
                LineItems = [new ServiceLineItemRequest { Description = "Oil change", FlatCost = 59.99m }],
            },
        };

    private Task ArchiveAsync(Guid vehicleId) =>
        factory.ExecuteScalarAsync("UPDATE vehicles SET archived_at = now() WHERE id = @id", ("id", vehicleId));

    private async Task<Guid> SeedDraftAsync(Guid? vehicleId)
    {
        var submissionId = await factory.InsertSubmissionAsync(Alice, status: "ready");
        return await factory.InsertDraftAsync(Alice, submissionId, "needs_review", "service_record", vehicleId, null, vehicleId is null ? "none" : "vin", DraftProposalJson.Serialize(ServiceProposal()));
    }

    [Fact]
    public async Task ADraftForAnArchivedVehicle_ReadsAsNeedingAVehicle_InTheListAndTheDetail_AndNoCandidateIsOffered()
    {
        var active = await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2012);
        var archived = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var draft = await SeedDraftAsync(archived);
        await ArchiveAsync(archived);

        var detail = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draft}"))!;
        var page = (await AliceClient().GetFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>("/api/drafts"))!;

        Assert.Null(detail.Vehicle.VehicleId);
        Assert.Equal(["2012 Honda Civic"], detail.Vehicle.Candidates.Select(candidate => candidate.Name));
        Assert.Equal(active, detail.Vehicle.Candidates.Single().VehicleId);
        Assert.Null(page.Items.Single().VehicleName);
    }

    [Fact]
    public async Task ADraftForAnActiveVehicle_StillNamesIt_AndItComesBackAfterARestore()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var draft = await SeedDraftAsync(vehicle);

        Assert.Equal(vehicle, (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draft}"))!.Vehicle.VehicleId);
        await ArchiveAsync(vehicle);
        Assert.Null((await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draft}"))!.Vehicle.VehicleId);
        (await AliceClient().PostAsync($"/api/vehicles/{vehicle}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(vehicle, (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draft}"))!.Vehicle.VehicleId);
    }

    [Fact]
    public async Task IncludingADraftIntoAnArchivedVehicle_IsRefusedLikeAnUnknownVehicle_AndNothingIsCreated()
    {
        var archived = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var draft = await SeedDraftAsync(null);
        await ArchiveAsync(archived);

        var response = await AliceClient().PostAsJsonAsync(
            $"/api/drafts/{draft}/accept", new AcceptDraftRequest { VehicleId = archived, Proposal = ServiceProposal() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.Equal("needs_review", (string?)await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", draft)));
    }

    [Fact]
    public async Task StartingAnUploadForAnArchivedVehicle_Is409WithTheArchivedType_AndAnActiveVehicleStillWorks()
    {
        var archived = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var active = await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2012);
        await ArchiveAsync(archived);

        using var refused = await AliceClient().PostAsync("/api/drafts/uploads", Upload(archived));
        using var accepted = await AliceClient().PostAsync("/api/drafts/uploads", Upload(active));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("urn:mygarage:problem:vehicle-archived", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task TheDraftPicker_NeverProposesAnArchivedVehicle_WhenAnEmailIsProcessed()
    {
        var archived = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019, "1FA6P8CF0K5100001");
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2012);
        await ArchiveAsync(archived);
        var draft = await SeedDraftAsync(null);

        var detail = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draft}"))!;

        Assert.DoesNotContain(detail.Vehicle.Candidates, candidate => candidate.VehicleId == archived);
        Assert.Single(detail.Vehicle.Candidates);
    }

    private static MultipartFormDataContent Upload(Guid vehicleId)
    {
        var content = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        var file = new ByteArrayContent(IntakeClient.PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "files", "invoice.pdf");
        content.Add(new StringContent(vehicleId.ToString()), "vehicleId");
        return content;
    }
}

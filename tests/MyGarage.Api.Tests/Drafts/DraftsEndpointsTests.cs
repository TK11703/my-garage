using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Drafts;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.VehicleDocuments;

namespace MyGarage.Api.Tests.Drafts;

public class DraftsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    private static readonly DateOnly Day = new(2025, 10, 20);

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    private static DraftProposal ServiceProposal(int? odometer = 45_500, params ServiceLineItemRequest[] lines) =>
        new()
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                OdometerReading = odometer,
                ShopOrPerson = "Joe's Garage",
                LineItems = lines.Length > 0 ? lines : [new ServiceLineItemRequest { Description = "Oil change", FlatCost = 59.99m }],
            },
        };

    private sealed record Seeded(Guid DraftId, Guid SubmissionId, Guid? AttachmentId, Guid NotificationId);

    // A submission, an optional stored PDF, a draft with the proposal, and the draft's notification, all owned by ownerId.
    private async Task<Seeded> SeedAsync(
        string ownerId,
        DraftProposal? proposal = null,
        Guid? vehicleId = null,
        bool withAttachment = false,
        string state = "needs_review",
        string vehicleMatch = "none",
        DateTimeOffset? createdAt = null)
    {
        var submissionId = await factory.InsertSubmissionAsync(ownerId, status: "ready", subject: "Fwd: Joe's invoice", bodyText: "Oil change today");
        Guid? attachmentId = withAttachment ? await factory.InsertAttachmentAsync(submissionId, ownerId, content: IntakeClient.PdfBytes) : null;
        var json = proposal is null ? "{}" : DraftProposalJson.Serialize(proposal);
        var kind = proposal?.Kind is { } draftKind ? DraftKinds.ToText(draftKind) : null;
        var draftId = await factory.InsertDraftAsync(
            ownerId, submissionId, state, kind, vehicleId, attachmentId, vehicleMatch, json, createdAt: createdAt);
        var notificationId = await factory.InsertNotificationAsync(ownerId, draftId);
        return new Seeded(draftId, submissionId, attachmentId, notificationId);
    }

    private async Task<string> StateAsync(Guid draftId) => (string)(await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", draftId)))!;

    // ---- list ----

    [Fact]
    public async Task List_PagesTheCallersOpenDrafts_NewestFirst_AndNeverShowsAnotherUsers()
    {
        var vehicleId = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var now = DateTimeOffset.UtcNow;
        var oldest = await SeedAsync(Alice, ServiceProposal(), createdAt: now.AddDays(-3));
        var newest = await SeedAsync(Alice, ServiceProposal(), vehicleId, createdAt: now.AddDays(-1));
        var manual = await SeedAsync(Alice, null, state: "manual_entry", createdAt: now.AddDays(-2));
        await SeedAsync(Alice, ServiceProposal(), state: "included", createdAt: now);
        await SeedAsync(Bob, ServiceProposal(), createdAt: now);

        var response = await AliceClient().GetAsync("/api/drafts?pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>())!;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal([newest.DraftId, manual.DraftId], page.Items.Select(item => item.Id));
        var first = page.Items[0];
        Assert.Equal(DraftState.NeedsReview, first.State);
        Assert.Equal(DraftKind.ServiceRecord, first.Kind);
        Assert.Equal("2019 Ford Mustang", first.VehicleName);
        Assert.Equal(59.99m, first.HeadlineTotal);
        Assert.Equal(DraftState.ManualEntry, page.Items[1].State);
        Assert.Null(page.Items[1].Kind);
        Assert.Null(page.Items[1].VehicleName);

        var second = (await (await AliceClient().GetAsync("/api/drafts?pageSize=2&page=2")).Content.ReadFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>())!;
        Assert.Equal([oldest.DraftId], second.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task List_AnOutOfRangePage_IsAnsweredWithTheLastPage_AndBadPagingIs400()
    {
        await SeedAsync(Alice, ServiceProposal());

        var beyond = (await (await AliceClient().GetAsync("/api/drafts?page=9")).Content.ReadFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>())!;

        Assert.Equal(1, beyond.Page);
        Assert.Single(beyond.Items);
        Assert.Equal(10, beyond.PageSize);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/drafts?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/drafts?pageSize=51")).StatusCode);
    }

    // ---- get ----

    [Fact]
    public async Task Get_ReturnsTheDraftWithProposalCandidatesCompatibilityAndRetainedEmail_AndReadsItsNotification()
    {
        var mine = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019, "1FA6P8CF0K5100001");
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2012);
        await factory.InsertVehicleAsync(Bob, "Tesla", "Model 3", 2021);
        var seeded = await SeedAsync(Alice, ServiceProposal(odometer: null), mine, withAttachment: true, vehicleMatch: "vin");

        var response = await AliceClient().GetAsync($"/api/drafts/{seeded.DraftId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;
        Assert.Equal(seeded.DraftId, draft.Id);
        Assert.Equal(DraftState.NeedsReview, draft.State);
        Assert.Equal(DraftKind.ServiceRecord, draft.Kind);
        Assert.Equal(DraftKind.ServiceRecord, draft.Proposal.Kind);
        Assert.Equal("Joe's Garage", draft.Proposal.ServiceRecord!.ShopOrPerson);
        Assert.Equal(mine, draft.Vehicle.VehicleId);
        Assert.Equal(VehicleMatch.Vin, draft.Vehicle.Match);
        Assert.Equal(["2012 Honda Civic", "2019 Ford Mustang"], draft.Vehicle.Candidates.Select(candidate => candidate.Name).Order());
        Assert.Equal(VehicleMatch.Vin, draft.Vehicle.Candidates.Single(candidate => candidate.VehicleId == mine).Match);
        Assert.DoesNotContain(draft.Vehicle.Candidates, candidate => candidate.Name.Contains("Tesla"));
        Assert.False(draft.Compatibility.IsAcceptable);
        Assert.Contains(draft.Compatibility.Fields, field => field.Path == "serviceRecord.odometerReading" && field.Status == CompatibilityStatus.Missing);
        Assert.True(draft.HasAttachment);
        Assert.Equal("attachment", draft.Source.Kind);
        Assert.Equal("invoice.pdf", draft.Source.FileName);
        Assert.Equal("application/pdf", draft.Source.ContentType);
        Assert.Equal("sender@contoso.test", draft.Email!.Sender);
        Assert.Equal("Fwd: Joe's invoice", draft.Email.Subject);
        Assert.Equal(0.80m, draft.Confidence);
        Assert.True(draft.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.NotNull(await factory.ExecuteScalarAsync("SELECT read_at FROM notifications WHERE id = @id", ("id", seeded.NotificationId)));
    }

    [Fact]
    public async Task Get_ATextDraftHasATextSourceAndNoAttachment()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());

        var draft = (await (await AliceClient().GetAsync($"/api/drafts/{seeded.DraftId}")).Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;

        Assert.Equal("text", draft.Source.Kind);
        Assert.Null(draft.Source.FileName);
        Assert.False(draft.HasAttachment);
    }

    [Fact]
    public async Task Get_AManualEntryDraft_HasNoKindAndAMissingKindField()
    {
        var seeded = await SeedAsync(Alice, null, state: "manual_entry");

        var draft = (await (await AliceClient().GetAsync($"/api/drafts/{seeded.DraftId}")).Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;

        Assert.Equal(DraftState.ManualEntry, draft.State);
        Assert.Null(draft.Kind);
        Assert.False(draft.Compatibility.IsAcceptable);
    }

    [Fact]
    public async Task Get_AnotherUsersDraftOrAnUnknownDraft_Is404()
    {
        var seeded = await SeedAsync(Bob, ServiceProposal());

        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{seeded.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{Guid.NewGuid()}")).StatusCode);
        Assert.Null(await factory.ExecuteScalarAsync("SELECT read_at FROM notifications WHERE id = @id", ("id", seeded.NotificationId)));
    }

    // ---- compatibility ----

    [Fact]
    public async Task Compatibility_ChecksAnEditedProposalWithoutSavingAnything()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(odometer: null));
        var before = await factory.ExecuteScalarAsync("SELECT proposal::text FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId));

        var response = await AliceClient().PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/compatibility", ServiceProposal(odometer: 50_000));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CompatibilityDto>())!;
        Assert.True(result.IsAcceptable);
        Assert.Equal(DraftKind.ServiceRecord, result.Kind);
        Assert.Equal(before, await factory.ExecuteScalarAsync("SELECT proposal::text FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId)));
    }

    [Fact]
    public async Task Compatibility_ForAnotherUsersDraft_Is404()
    {
        var seeded = await SeedAsync(Bob, ServiceProposal());

        var response = await AliceClient().PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/compatibility", ServiceProposal());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Compatibility_WarnsAboutAProbableDuplicateRecordOnTheDraftsVehicle()
    {
        var vehicleId = await factory.InsertVehicleAsync(Alice);
        await factory.ExecuteScalarAsync(
            "WITH r AS (INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @v, @d, 100, now(), now(), 'seed', 'seed') RETURNING id) " +
            "INSERT INTO service_line_items (id, service_record_id, description, flat_cost, total_cost, created_at, updated_at, created_by, updated_by) " +
            "SELECT gen_random_uuid(), id, 'Oil', 59.99, 59.99, now(), now(), 'seed', 'seed' FROM r",
            ("v", vehicleId), ("d", Day));
        var seeded = await SeedAsync(Alice, ServiceProposal(), vehicleId);

        var result = (await (await AliceClient().PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/compatibility", ServiceProposal())).Content.ReadFromJsonAsync<CompatibilityDto>())!;

        Assert.True(result.IsAcceptable);
        Assert.Contains(result.Warnings, warning => warning.Contains("same date and total", StringComparison.OrdinalIgnoreCase));
    }

    // ---- change kind ----

    [Fact]
    public async Task ChangeKind_RePresentsTheProposalAsTheNewKind_AndMovesManualEntryToNeedsReview()
    {
        var seeded = await SeedAsync(Alice, null, state: "manual_entry");

        var response = await AliceClient().PutAsJsonAsync($"/api/drafts/{seeded.DraftId}/kind", new ChangeDraftKindRequest { Kind = DraftKind.Tax });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;
        Assert.Equal(DraftState.NeedsReview, draft.State);
        Assert.Equal(DraftKind.Tax, draft.Kind);
        Assert.Equal(DraftKind.Tax, draft.Proposal.Kind);
        Assert.NotNull(draft.Proposal.Tax);
        Assert.Contains(draft.Compatibility.Fields, field => field.Path == "tax.amount" && field.Status == CompatibilityStatus.Missing);
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
        Assert.Equal("tax", await factory.ExecuteScalarAsync("SELECT kind FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId)));
    }

    [Fact]
    public async Task ChangeKind_CarriesTheDateAndTotalOver()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());

        var draft = (await (await AliceClient().PutAsJsonAsync($"/api/drafts/{seeded.DraftId}/kind", new ChangeDraftKindRequest { Kind = DraftKind.Inspection }))
            .Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;

        Assert.Equal(Day, draft.Proposal.Inspection!.Date);
        Assert.Equal(59.99m, draft.Proposal.Inspection.Cost);
        Assert.Null(draft.Proposal.ServiceRecord);
    }

    [Fact]
    public async Task ChangeKind_ADecidedDraftIs409_AnotherUsersDraftIs404_AndAMissingKindIs400()
    {
        var included = await SeedAsync(Alice, ServiceProposal(), state: "included");
        var other = await SeedAsync(Bob, ServiceProposal());
        var open = await SeedAsync(Alice, ServiceProposal());
        var request = new ChangeDraftKindRequest { Kind = DraftKind.Tax };

        Assert.Equal(HttpStatusCode.Conflict, (await AliceClient().PutAsJsonAsync($"/api/drafts/{included.DraftId}/kind", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().PutAsJsonAsync($"/api/drafts/{other.DraftId}/kind", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().PutAsJsonAsync($"/api/drafts/{open.DraftId}/kind", new ChangeDraftKindRequest())).StatusCode);
    }

    // ---- accept ----

    private async Task<(HttpResponseMessage Response, Guid VehicleId)> AcceptAsync(
        Seeded seeded, DraftProposal proposal, bool keepOriginal = false, string? user = null, Guid? vehicleId = null)
    {
        var owner = user ?? Alice;
        var vehicle = vehicleId ?? await factory.InsertVehicleAsync(owner);
        var client = factory.CreateClientForUser(owner, $"{owner}@contoso.test");
        var response = await client.PostAsJsonAsync(
            $"/api/drafts/{seeded.DraftId}/accept", new AcceptDraftRequest { VehicleId = vehicle, Proposal = proposal, KeepOriginal = keepOriginal });
        return (response, vehicle);
    }

    [Fact]
    public async Task Accept_AServiceRecordRaisingTheMileagePastAReminder_RaisesItsNotification()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var reminder = await MyGarage.Api.Tests.Reminders.ReminderSeed.InsertReminderAsync(factory, vehicle);
        var seeded = await SeedAsync(Alice, ServiceProposal(45_500));

        var (response, _) = await AcceptAsync(seeded, ServiceProposal(45_500), vehicleId: vehicle);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["reminder_overdue"], await MyGarage.Api.Tests.Reminders.ReminderSeed.NotificationKindsAsync(factory, Alice, reminder));
    }
    [Fact]
    public async Task Accept_CreatesTheServiceRecordFromEmail_AndTheDraftLeavesTheList()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var lines = new[]
        {
            new ServiceLineItemRequest { Description = "Oil change", FlatCost = 59.99m },
            new ServiceLineItemRequest { Description = "Brakes", LaborHours = 1m, LaborRate = 100m, Parts = [new ServicePartRequest { Name = "Pads", Quantity = 2m, UnitPrice = 20m }] },
        };

        var (response, vehicleId) = await AcceptAsync(seeded, ServiceProposal(46_000, lines));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<AcceptedDraftDto>())!;
        Assert.Equal(DraftKind.ServiceRecord, accepted.Kind);
        Assert.Equal(vehicleId, accepted.VehicleId);
        Assert.Null(accepted.DocumentId);
        var record = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicleId}/service-records/{accepted.RecordId}")).Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        Assert.True(record.FromEmail);
        Assert.Equal(46_000, record.OdometerReading);
        Assert.Equal(2, record.LineItems.Count);
        Assert.Equal(199.99m, record.TotalCost);
        Assert.Equal("included", await StateAsync(seeded.DraftId));
        Assert.Equal("{}", await factory.ExecuteScalarAsync("SELECT proposal::text FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId)));
        Assert.Empty((await (await AliceClient().GetAsync("/api/drafts")).Content.ReadFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>())!.Items);
        Assert.Equal(0L, await factory.CountRowsAsync("notifications"));
        Assert.Null(await factory.ExecuteScalarAsync("SELECT sender FROM email_submissions WHERE id = @id", ("id", seeded.SubmissionId)));
        Assert.False(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
        Assert.Equal(0L, await factory.CountRowsAsync("vehicle_documents"));
    }

    [Fact]
    public async Task Accept_WithKeepOriginal_KeepsTheFileAsAVehicleDocumentLinkedToTheRecord()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);

        var (response, vehicleId) = await AcceptAsync(seeded, ServiceProposal(), keepOriginal: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<AcceptedDraftDto>())!;
        Assert.NotNull(accepted.DocumentId);
        Assert.True(await factory.DocumentBlobExistsAsync(vehicleId, accepted.DocumentId.Value));
        Assert.False(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
        var documents = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicleId}/documents")).Content.ReadFromJsonAsync<PagedResultDto<VehicleDocumentDto>>())!;
        var document = Assert.Single(documents.Items);
        Assert.Equal(accepted.DocumentId, document.Id);
        Assert.Equal(DraftKind.ServiceRecord, document.SourceKind);
        Assert.Equal(accepted.RecordId, document.SourceRecordId);
        Assert.Equal("invoice.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
    }

    [Fact]
    public async Task Accept_WithKeepOriginalOnATextDraft_KeepsNothing()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());

        var (response, _) = await AcceptAsync(seeded, ServiceProposal(), keepOriginal: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<AcceptedDraftDto>())!.DocumentId);
        Assert.Equal(0L, await factory.CountRowsAsync("vehicle_documents"));
    }

    [Fact]
    public async Task Accept_EveryOtherKind_CreatesItsRecord()
    {
        var fuel = new DraftProposal { Kind = DraftKind.Fuel, FuelEnergy = new FuelEnergyProposal { Date = Day, UnitPrice = 3.459m, Quantity = 12.345m } };
        var inspection = new DraftProposal
        {
            Kind = DraftKind.Inspection,
            Inspection = new InspectionProposal { Type = MyGarage.Shared.Inspections.InspectionType.Safety, Date = Day, Cost = 25m },
        };
        var registration = new DraftProposal
        {
            Kind = DraftKind.Registration,
            Registration = new RegistrationProposal { State = "WA", Cost = 78.75m, ExpirationDate = Day.AddYears(1) },
        };
        var insurance = new DraftProposal
        {
            Kind = DraftKind.Insurance,
            Insurance = new InsuranceProposal { StartDate = Day, EndDate = Day.AddYears(1), Duration = MyGarage.Shared.Insurance.InsuranceDuration.Annual, Cost = 1240m },
        };
        var tax = new DraftProposal
        {
            Kind = DraftKind.Tax,
            Tax = new TaxProposal { Type = MyGarage.Shared.Taxes.TaxType.Lease, Amount = 150m, Recurrence = MyGarage.Shared.Taxes.TaxRecurrence.Monthly },
        };
        var vehicleId = await factory.InsertVehicleAsync(Alice);

        foreach (var proposal in new[] { fuel, inspection, registration, insurance, tax })
        {
            var seeded = await SeedAsync(Alice, proposal, vehicleId);
            var (response, _) = await AcceptAsync(seeded, proposal, vehicleId: vehicleId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(proposal.Kind, (await response.Content.ReadFromJsonAsync<AcceptedDraftDto>())!.Kind);
        }

        foreach (var table in new[] { "fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes" })
        {
            Assert.Equal(1L, (long)(await factory.ExecuteScalarAsync($"SELECT count(*) FROM {table} WHERE from_email"))!);
        }
    }

    [Fact]
    public async Task Accept_AnEnergyDraft_StoresAChargingEntry()
    {
        var proposal = new DraftProposal { Kind = DraftKind.Energy, FuelEnergy = new FuelEnergyProposal { Date = Day, UnitPrice = 0.13m, Quantity = 30m } };
        var seeded = await SeedAsync(Alice, proposal);

        var (response, vehicleId) = await AcceptAsync(seeded, proposal);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entries = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicleId}/fuel-energy-entries")).Content.ReadFromJsonAsync<PagedResultDto<FuelEnergyEntryDto>>())!;
        var entry = Assert.Single(entries.Items);
        Assert.Equal(FuelEnergyKind.Energy, entry.Kind);
        Assert.Equal(3.90m, entry.TotalCost);
        Assert.True(entry.FromEmail);
    }

    [Fact]
    public async Task Accept_AnInvalidProposal_Is400ValidationProblem_AndNothingIsCreated()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);

        var (response, _) = await AcceptAsync(seeded, ServiceProposal(odometer: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains(problem.Errors.Keys, key => key.Equals("Proposal.ServiceRecord.OdometerReading", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.True(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
    }

    [Fact]
    public async Task Accept_ARequestWithoutAVehicleOrProposal_Is400()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());

        var response = await AliceClient().PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/accept", new AcceptDraftRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accept_AnotherUsersDraft_Is404_AndTheirDraftIsUntouched()
    {
        var seeded = await SeedAsync(Bob, ServiceProposal(), withAttachment: true);

        var (response, _) = await AcceptAsync(seeded, ServiceProposal(), user: Alice);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.True(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
    }

    [Fact]
    public async Task Accept_AVehicleThatIsNotTheCallers_Is404_AndNothingIsWritten()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var bobsVehicle = await factory.InsertVehicleAsync(Bob);

        var (response, _) = await AcceptAsync(seeded, ServiceProposal(), keepOriginal: true, vehicleId: bobsVehicle);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.Equal(0L, await factory.CountRowsAsync("vehicle_documents"));
        await foreach (var blob in factory.DocumentContainer.GetBlobsAsync())
        {
            Assert.Fail($"A document file was written for a foreign vehicle: {blob.Name}");
        }
    }

    [Fact]
    public async Task Accept_ASecondTime_Is409_AndCreatesNothingMore()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());
        var (first, vehicleId) = await AcceptAsync(seeded, ServiceProposal());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var (second, _) = await AcceptAsync(seeded, ServiceProposal(), vehicleId: vehicleId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1L, await factory.CountRowsAsync("service_records"));
    }

    [Fact]
    public async Task Accept_ConcurrentClicks_CreateExactlyOneRecord()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var vehicleId = await factory.InsertVehicleAsync(Alice);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AcceptAsync(seeded, ServiceProposal(), keepOriginal: true, vehicleId: vehicleId)));

        Assert.Equal(1, responses.Count(result => result.Response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(result => result.Response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await factory.CountRowsAsync("service_records"));
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        var blobs = 0;
        await foreach (var _ in factory.DocumentContainer.GetBlobsAsync())
        {
            blobs++;
        }

        Assert.Equal(1, blobs);
    }

    [Fact]
    public async Task Accept_WhenDocumentStorageIsDown_Is503_AndTheRecordIsNotCreated()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var vehicleId = await factory.InsertVehicleAsync(Alice);
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<MyGarage.Database.VehicleDocuments.IVehicleDocumentBlobStore>();
            services.AddSingleton<MyGarage.Database.VehicleDocuments.IVehicleDocumentBlobStore>(new DownDocumentStore());
        }));
        var client = broken.CreateUserClient(Alice, "alice@contoso.test");

        var response = await client.PostAsJsonAsync(
            $"/api/drafts/{seeded.DraftId}/accept", new AcceptDraftRequest { VehicleId = vehicleId, Proposal = ServiceProposal(), KeepOriginal = true });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.True(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
    }

    private sealed class DownDocumentStore : MyGarage.Database.VehicleDocuments.IVehicleDocumentBlobStore
    {
        private static MyGarage.Database.VehicleDocuments.DocumentStorageUnavailableException Down() => new("down");

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> CopyFromIntakeAsync(Guid submissionId, Guid attachmentId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task<Stream?> OpenReadAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) => throw Down();
    }

    // ---- discard ----

    [Fact]
    public async Task Discard_Returns204_RemovesTheDraftTheNotificationTheFileAndTheEmailText_AndCreatesNoRecord()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);

        var response = await AliceClient().PostAsync($"/api/drafts/{seeded.DraftId}/discard", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("discarded", await StateAsync(seeded.DraftId));
        Assert.Equal(0L, await factory.CountRowsAsync("notifications"));
        Assert.False(await factory.IntakeBlobExistsAsync(seeded.SubmissionId, seeded.AttachmentId!.Value));
        Assert.Null(await factory.ExecuteScalarAsync("SELECT subject FROM email_submissions WHERE id = @id", ("id", seeded.SubmissionId)));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.Empty((await (await AliceClient().GetAsync("/api/drafts")).Content.ReadFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>())!.Items);
    }

    [Fact]
    public async Task Discard_AnotherUsersDraftIs404_ADecidedDraftIs409()
    {
        var other = await SeedAsync(Bob, ServiceProposal(), withAttachment: true);
        var included = await SeedAsync(Alice, ServiceProposal(), state: "included");

        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().PostAsync($"/api/drafts/{other.DraftId}/discard", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().PostAsync($"/api/drafts/{Guid.NewGuid()}/discard", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AliceClient().PostAsync($"/api/drafts/{included.DraftId}/discard", null)).StatusCode);
        Assert.Equal("needs_review", await StateAsync(other.DraftId));
        Assert.True(await factory.IntakeBlobExistsAsync(other.SubmissionId, other.AttachmentId!.Value));
    }

    // ---- attachment ----

    [Fact]
    public async Task Attachment_StreamsTheOriginalWithTheDetectedTypeNosniffAndNoStore()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);

        var response = await AliceClient().GetAsync($"/api/drafts/{seeded.DraftId}/attachment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(IntakeClient.PdfBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Attachment_Is404ForAnotherUserAForATextDraftAndAfterTheFileWasRemoved()
    {
        var other = await SeedAsync(Bob, ServiceProposal(), withAttachment: true);
        var text = await SeedAsync(Alice, ServiceProposal());
        var gone = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        await factory.IntakeContainer.DeleteBlobIfExistsAsync($"{gone.SubmissionId}/{gone.AttachmentId}");

        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{other.DraftId}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{text.DraftId}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{gone.DraftId}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/drafts/{Guid.NewGuid()}/attachment")).StatusCode);
    }

    // ---- authentication ----

    private static IEnumerable<(HttpMethod Method, string Path)> AllRoutes(Guid id) =>
    [
        (HttpMethod.Get, "/api/drafts"),
        (HttpMethod.Get, $"/api/drafts/{id}"),
        (HttpMethod.Post, $"/api/drafts/{id}/compatibility"),
        (HttpMethod.Put, $"/api/drafts/{id}/kind"),
        (HttpMethod.Post, $"/api/drafts/{id}/accept"),
        (HttpMethod.Post, $"/api/drafts/{id}/discard"),
        (HttpMethod.Get, $"/api/drafts/{id}/attachment"),
    ];

    private static HttpRequestMessage Request(HttpMethod method, string path) =>
        new(method, path) { Content = method == HttpMethod.Get ? null : JsonContent.Create(new { }) };

    [Fact]
    public async Task EveryRoute_Is401WithoutAToken()
    {
        var client = factory.CreateClientWithToken(null);

        foreach (var (method, path) in AllRoutes(Guid.NewGuid()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, path))).StatusCode);
        }
    }

    [Fact]
    public async Task EveryRoute_RefusesTheWorkflowsAppOnlyToken()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var client = factory.CreateClientForWorkflow();

        foreach (var (method, path) in AllRoutes(seeded.DraftId))
        {
            var status = (await client.SendAsync(Request(method, path))).StatusCode;
            Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"{method} {path} answered {(int)status}");
        }

        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
    }

    [Fact]
    public async Task EveryRoute_IsCrossUserSafe_UserBGets404ForUserAsDraft()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal(), withAttachment: true);
        var bob = factory.CreateClientForUser(Bob, "bob@contoso.test");
        var vehicle = await factory.InsertVehicleAsync(Bob);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/drafts/{seeded.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/compatibility", ServiceProposal())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/drafts/{seeded.DraftId}/kind", new ChangeDraftKindRequest { Kind = DraftKind.Tax })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await bob.PostAsJsonAsync($"/api/drafts/{seeded.DraftId}/accept", new AcceptDraftRequest { VehicleId = vehicle, Proposal = ServiceProposal() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/drafts/{seeded.DraftId}/discard", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/drafts/{seeded.DraftId}/attachment")).StatusCode);
        Assert.Equal("needs_review", await StateAsync(seeded.DraftId));
    }

    [Fact]
    public async Task ProposalEnumsTravelAsSnakeCaseStrings()
    {
        var seeded = await SeedAsync(Alice, ServiceProposal());

        var json = await AliceClient().GetStringAsync($"/api/drafts/{seeded.DraftId}");

        using var document = JsonDocument.Parse(json);
        Assert.Equal("service_record", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("needs_review", document.RootElement.GetProperty("state").GetString());
        Assert.Equal("none", document.RootElement.GetProperty("vehicle").GetProperty("match").GetString());
    }
}

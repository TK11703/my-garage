using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Drafts;

[Collection(DatabaseCollection.Name)]
public class DraftAcceptanceRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required NpgsqlDataSource DataSource { get; init; }

        public required DraftAcceptanceRepository Repository { get; init; }

        public required Guid VehicleId { get; init; }

        public required Guid SubmissionId { get; init; }

        public required Guid AttachmentId { get; init; }

        public required Guid DraftId { get; init; }

        public required Guid NotificationId { get; init; }
    }

    // One owner-a vehicle plus a submission with one attachment, one open draft on it, and the draft's notification.
    private async Task<Harness> CreateAsync(string state = "needs_review", string? kind = "service_record")
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        var vehicle = TestData.Vehicle("owner-a");
        await new VehicleRepository(dataSource, scripts).InsertAsync(vehicle, CancellationToken.None);
        var submissionId = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready");
        var attachmentId = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, submissionId, fileName: "invoice.pdf", sizeBytes: 2048);
        var draftId = await EmailIntakeTestData.InsertDraftAsync(
            dataSource, "owner-a", submissionId, state: state, kind: kind, attachmentId: attachmentId, proposalJson: """{"kind":"service_record"}""");
        var notificationId = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", draftId);
        return new Harness
        {
            DataSource = dataSource,
            Repository = new DraftAcceptanceRepository(dataSource, scripts),
            VehicleId = vehicle.Id,
            SubmissionId = submissionId,
            AttachmentId = attachmentId,
            DraftId = draftId,
            NotificationId = notificationId,
        };
    }

    private static ServiceRecordValues ServiceValues() =>
        new(
            new DateOnly(2026, 10, 20),
            45_500,
            "Joe's Garage",
            [
                new ServiceLineItemInput("Oil change", 59.99m, null, null, []),
                new ServiceLineItemInput(
                    "Brake job",
                    null,
                    1.5m,
                    120m,
                    [new ServicePartInput("Pads", 2m, 35.25m), new ServicePartInput("Rotor", 1m, 80m)]),
            ]);

    private sealed class DraftRow
    {
        public string State { get; init; } = string.Empty;

        public DateTimeOffset? DecidedAt { get; init; }

        public string? CreatedRecordKind { get; init; }

        public Guid? CreatedRecordId { get; init; }

        public string Proposal { get; init; } = string.Empty;

        public Guid? VehicleId { get; init; }
    }

    private static async Task<DraftRow> ReadDraftAsync(Harness harness)
    {
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<DraftRow>(
            "SELECT state, decided_at, created_record_kind, created_record_id, proposal::text AS proposal, vehicle_id FROM maintenance_drafts WHERE id = @id",
            new { id = harness.DraftId });
    }

    private static async Task<int> CountAsync(Harness harness, string table)
    {
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private sealed class SubmissionRow
    {
        public string? Sender { get; init; }

        public string? Subject { get; init; }

        public string? BodyText { get; init; }

        public DateTimeOffset? ContentPurgedAt { get; init; }
    }

    private static async Task<SubmissionRow> ReadSubmissionAsync(Harness harness)
    {
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<SubmissionRow>(
            "SELECT sender, subject, body_text, content_purged_at FROM email_submissions WHERE id = @id", new { id = harness.SubmissionId });
    }

    [Fact]
    public async Task Accept_ServiceRecord_CreatesTheRecordWithLineItemsAndParts_FromEmail()
    {
        var harness = await CreateAsync();

        var result = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.Accepted, result.Outcome);
        Assert.NotNull(result.RecordId);
        Assert.Null(result.DocumentId);
        Assert.Equal(new DraftAttachmentRef(harness.SubmissionId, harness.AttachmentId), result.Attachment);

        await using var connection = await harness.DataSource.OpenConnectionAsync();
        var record = await connection.QuerySingleAsync<(bool FromEmail, string CreatedBy, string UpdatedBy, int Odometer, string? Shop, Guid VehicleId)>(
            "SELECT from_email, created_by, updated_by, odometer_reading, shop_or_person, vehicle_id FROM service_records WHERE id = @id", new { id = result.RecordId });
        Assert.True(record.FromEmail);
        Assert.Equal("owner-a", record.CreatedBy);
        Assert.Equal("owner-a", record.UpdatedBy);
        Assert.Equal(45_500, record.Odometer);
        Assert.Equal("Joe's Garage", record.Shop);
        Assert.Equal(harness.VehicleId, record.VehicleId);

        var lines = (await connection.QueryAsync<(string Description, decimal Total, bool FromCreator)>(
            "SELECT description, total_cost, created_by = 'owner-a' FROM service_line_items WHERE service_record_id = @id ORDER BY created_at, description",
            new { id = result.RecordId })).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(59.99m, lines.Single(line => line.Description == "Oil change").Total);
        // 1.5 h x 120.00 + 2 x 35.25 + 1 x 80.00
        Assert.Equal(330.50m, lines.Single(line => line.Description == "Brake job").Total);
        Assert.All(lines, line => Assert.True(line.FromCreator));

        var parts = (await connection.QueryAsync<(string Name, short Position, decimal Quantity)>(
            "SELECT p.name, p.position, p.quantity FROM service_line_item_parts p JOIN service_line_items li ON li.id = p.line_item_id WHERE li.service_record_id = @id ORDER BY p.position",
            new { id = result.RecordId })).ToList();
        Assert.Equal(["Pads", "Rotor"], parts.Select(part => part.Name));
        Assert.Equal<short>(1, parts[0].Position);
    }

    [Fact]
    public async Task Accept_UpdatesTheDraftAndRemovesTheNotificationAndTheEmailContent()
    {
        var harness = await CreateAsync();

        var result = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        var draft = await ReadDraftAsync(harness);
        Assert.Equal("included", draft.State);
        Assert.Equal(Now, draft.DecidedAt);
        Assert.Equal("service_record", draft.CreatedRecordKind);
        Assert.Equal(result.RecordId, draft.CreatedRecordId);
        Assert.Equal("{}", draft.Proposal);
        Assert.Equal(harness.VehicleId, draft.VehicleId);
        Assert.Equal(0, await CountAsync(harness, "notifications"));
        var submission = await ReadSubmissionAsync(harness);
        Assert.Null(submission.Sender);
        Assert.Null(submission.Subject);
        Assert.Null(submission.BodyText);
        Assert.Equal(Now, submission.ContentPurgedAt);
    }

    [Fact]
    public async Task Accept_KeepsTheEmailContent_WhileAnotherDraftOfTheSubmissionIsStillOpen()
    {
        var harness = await CreateAsync();
        await EmailIntakeTestData.InsertDraftAsync(harness.DataSource, "owner-a", harness.SubmissionId, state: "needs_review", kind: "fuel");

        await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        var submission = await ReadSubmissionAsync(harness);
        Assert.Equal("owner@example.com", submission.Sender);
        Assert.Equal("Fwd: invoice", submission.Subject);
        Assert.Equal("Oil change today", submission.BodyText);
        Assert.Null(submission.ContentPurgedAt);
    }

    [Fact]
    public async Task Accept_WithKeepOriginal_InsertsTheDocumentRowLinkedToTheRecord()
    {
        var harness = await CreateAsync();
        var documentId = Guid.CreateVersion7();

        var result = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), documentId, Now, CancellationToken.None);

        Assert.Equal(documentId, result.DocumentId);
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        var document = await connection.QuerySingleAsync<(Guid VehicleId, string FileName, string ContentType, int SizeBytes, string? SourceKind, Guid? SourceRecordId, string CreatedBy)>(
            "SELECT vehicle_id, file_name, content_type, size_bytes, source_kind, source_record_id, created_by FROM vehicle_documents WHERE id = @documentId",
            new { documentId });
        Assert.Equal(harness.VehicleId, document.VehicleId);
        Assert.Equal("invoice.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(2048, document.SizeBytes);
        Assert.Equal("service_record", document.SourceKind);
        Assert.Equal(result.RecordId, document.SourceRecordId);
        Assert.Equal("owner-a", document.CreatedBy);
    }

    [Fact]
    public async Task Accept_WithoutKeepOriginal_InsertsNoDocument()
    {
        var harness = await CreateAsync();

        await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        Assert.Equal(0, await CountAsync(harness, "vehicle_documents"));
    }

    public static TheoryData<string, AcceptedRecordValues, string> FlatKinds() => new()
    {
        { "fuel", new FuelEnergyValues("fuel", new DateOnly(2026, 10, 1), 3.459m, 12.345m, 42.70m), "fuel_energy_entries" },
        { "energy", new FuelEnergyValues("energy", new DateOnly(2026, 10, 1), 0.1342m, 28.5m, 3.82m), "fuel_energy_entries" },
        { "inspection", new InspectionValues("safety", new DateOnly(2026, 10, 1), 25m, new DateOnly(2027, 10, 1)), "inspections" },
        { "registration", new RegistrationValues("WA", 78.75m, new DateOnly(2027, 5, 1)), "registrations" },
        { "insurance", new InsuranceValues(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "annual", 1240m), "insurance_policies" },
        { "tax", new TaxValues("personal_property", 312.40m, "annual"), "taxes" },
    };

    [Theory]
    [MemberData(nameof(FlatKinds))]
    public async Task Accept_EachOtherKind_CreatesExactlyOneRecordFromEmail(string kind, AcceptedRecordValues values, string table)
    {
        var harness = await CreateAsync(kind: kind);

        var result = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, values, null, Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.Accepted, result.Outcome);
        Assert.Equal(1, await CountAsync(harness, table));
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(Guid Id, bool FromEmail, string CreatedBy, string UpdatedBy, Guid VehicleId)>(
            $"SELECT id, from_email, created_by, updated_by, vehicle_id FROM {table}");
        Assert.Equal(result.RecordId, row.Id);
        Assert.True(row.FromEmail);
        Assert.Equal("owner-a", row.CreatedBy);
        Assert.Equal("owner-a", row.UpdatedBy);
        Assert.Equal(harness.VehicleId, row.VehicleId);
        Assert.Equal(kind, (await ReadDraftAsync(harness)).CreatedRecordKind);
    }

    [Fact]
    public async Task Accept_ASecondTime_ReportsAlreadyDecided_AndCreatesNothing()
    {
        var harness = await CreateAsync();
        await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        var second = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), Guid.CreateVersion7(), Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.AlreadyDecided, second.Outcome);
        Assert.Equal(1, await CountAsync(harness, "service_records"));
        Assert.Equal(0, await CountAsync(harness, "vehicle_documents"));
    }

    [Theory]
    [InlineData("included")]
    [InlineData("discarded")]
    [InlineData("expired")]
    public async Task Accept_ADecidedDraft_ReportsAlreadyDecided(string state)
    {
        var harness = await CreateAsync(state);

        var result = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.AlreadyDecided, result.Outcome);
        Assert.Equal(0, await CountAsync(harness, "service_records"));
    }

    [Fact]
    public async Task Accept_AnotherOwnersDraftOrAnUnknownDraft_ReportsNotFound_AndCreatesNothing()
    {
        var harness = await CreateAsync();

        var otherOwner = await harness.Repository.AcceptAsync("owner-b", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);
        var unknown = await harness.Repository.AcceptAsync("owner-a", Guid.NewGuid(), harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.NotFound, otherOwner.Outcome);
        Assert.Equal(AcceptDraftOutcome.NotFound, unknown.Outcome);
        Assert.Equal(0, await CountAsync(harness, "service_records"));
        Assert.Equal("needs_review", (await ReadDraftAsync(harness)).State);
    }

    [Fact]
    public async Task Accept_AVehicleTheOwnerDoesNotOwn_ReportsVehicleNotFound_AndWritesNothing()
    {
        var harness = await CreateAsync();
        var scripts = new SqlScriptStore();
        var other = TestData.Vehicle("owner-b");
        await new VehicleRepository(harness.DataSource, scripts).InsertAsync(other, CancellationToken.None);

        var foreign = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, other.Id, ServiceValues(), Guid.CreateVersion7(), Now, CancellationToken.None);
        var unknown = await harness.Repository.AcceptAsync("owner-a", harness.DraftId, Guid.NewGuid(), ServiceValues(), null, Now, CancellationToken.None);

        Assert.Equal(AcceptDraftOutcome.VehicleNotFound, foreign.Outcome);
        Assert.Equal(AcceptDraftOutcome.VehicleNotFound, unknown.Outcome);
        Assert.Equal(0, await CountAsync(harness, "service_records"));
        Assert.Equal(0, await CountAsync(harness, "vehicle_documents"));
        var draft = await ReadDraftAsync(harness);
        Assert.Equal("needs_review", draft.State);
        Assert.Null(draft.DecidedAt);
        Assert.Equal(1, await CountAsync(harness, "notifications"));
    }

    [Fact]
    public async Task Accept_WhenALineItemInsertFails_RollsBackTheRecordTheDraftStateAndTheDocument()
    {
        var harness = await CreateAsync();
        var tooLong = new ServiceRecordValues(
            new DateOnly(2026, 10, 20), 1000, null, [new ServiceLineItemInput(new string('x', 201), 10m, null, null, [])]);

        await Assert.ThrowsAnyAsync<PostgresException>(() =>
            harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, tooLong, Guid.CreateVersion7(), Now, CancellationToken.None));

        Assert.Equal(0, await CountAsync(harness, "service_records"));
        Assert.Equal(0, await CountAsync(harness, "vehicle_documents"));
        var draft = await ReadDraftAsync(harness);
        Assert.Equal("needs_review", draft.State);
        Assert.Equal("""{"kind": "service_record"}""", draft.Proposal);
        Assert.Equal(1, await CountAsync(harness, "notifications"));
        Assert.Null((await ReadSubmissionAsync(harness)).ContentPurgedAt);
    }

    [Fact]
    public async Task Accept_AServiceRecord_RaisesTheVehiclesEffectiveMileageLikeAManualRecord()
    {
        var harness = await CreateAsync();

        await harness.Repository.AcceptAsync("owner-a", harness.DraftId, harness.VehicleId, ServiceValues(), null, Now, CancellationToken.None);

        // The garage mileage is derived from the latest service record's odometer (there is no stored column to update).
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        var latest = await connection.ExecuteScalarAsync<int>(
            "SELECT odometer_reading FROM service_records WHERE vehicle_id = @id ORDER BY date_serviced DESC, created_at DESC LIMIT 1", new { id = harness.VehicleId });
        Assert.Equal(45_500, latest);
    }
}

using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Drafts;

[Collection(DatabaseCollection.Name)]
public class MaintenanceDraftRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private async Task<(MaintenanceDraftRepository Drafts, VehicleRepository Vehicles, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (new MaintenanceDraftRepository(dataSource, scripts), new VehicleRepository(dataSource, scripts), dataSource);
    }

    private static NewMaintenanceDraft Draft(string? kind = "service_record", Guid? vehicleId = null, string match = "vin", string proposal = """{"kind":"service_record"}""") =>
        new(Guid.CreateVersion7(), "needs_review", kind, vehicleId, match, 0.85m, proposal, "model note");

    private sealed class DraftRow
    {
        public string OwnerId { get; init; } = string.Empty;

        public Guid SubmissionId { get; init; }

        public string State { get; init; } = string.Empty;

        public string? Kind { get; init; }

        public Guid? VehicleId { get; init; }

        public string VehicleMatch { get; init; } = string.Empty;

        public decimal? Confidence { get; init; }

        public string Proposal { get; init; } = string.Empty;

        public string? Notes { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class NotificationRow
    {
        public string OwnerId { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public Guid? DraftId { get; init; }

        public string Title { get; init; } = string.Empty;

        public DateTimeOffset? ReadAt { get; init; }
    }

    private static async Task<List<DraftRow>> ReadDraftsAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<DraftRow>(
            "SELECT owner_id, submission_id, state, kind, vehicle_id, vehicle_match, confidence, proposal::text AS proposal, notes, created_at, expires_at FROM maintenance_drafts ORDER BY id")).ToList();
    }

    private static async Task<string> SubmissionStatusAsync(NpgsqlDataSource dataSource, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<string>("SELECT status FROM email_submissions WHERE id = @id", new { id });
    }

    [Fact]
    public async Task CreateDrafts_InsertsEveryDraftWithItsNotification_AndMarksTheSubmissionReady()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-a");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");
        var first = Draft("service_record", vehicle.Id);
        var second = Draft("fuel", null, "none", """{"kind":"fuel"}""");

        var created = await repository.CreateDraftsAsync(submission, "owner-a", [first, second], Now, CancellationToken.None);

        Assert.True(created);
        var rows = await ReadDraftsAsync(dataSource);
        Assert.Equal(2, rows.Count);
        var stored = rows.Single(row => row.Kind == "service_record");
        Assert.Equal("owner-a", stored.OwnerId);
        Assert.Equal(submission, stored.SubmissionId);
        Assert.Equal("needs_review", stored.State);
        Assert.Equal(vehicle.Id, stored.VehicleId);
        Assert.Equal("vin", stored.VehicleMatch);
        Assert.Equal(0.85m, stored.Confidence);
        Assert.Contains("service_record", stored.Proposal);
        Assert.Equal("model note", stored.Notes);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(Now.AddDays(30), stored.ExpiresAt);
        Assert.Null(rows.Single(row => row.Kind == "fuel").VehicleId);

        await using var connection = await dataSource.OpenConnectionAsync();
        var notifications = (await connection.QueryAsync<NotificationRow>(
            "SELECT owner_id, kind, draft_id, title, read_at FROM notifications ORDER BY title")).ToList();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, notification =>
        {
            Assert.Equal("owner-a", notification.OwnerId);
            Assert.Equal("draft_ready", notification.Kind);
            Assert.Null(notification.ReadAt);
            Assert.StartsWith("New draft ", notification.Title);
            Assert.EndsWith(" ready to review", notification.Title);
        });
        Assert.Contains(first.Id, notifications.Select(notification => notification.DraftId));
        Assert.Contains(second.Id, notifications.Select(notification => notification.DraftId));
        Assert.Equal("ready", await SubmissionStatusAsync(dataSource, submission));
    }

    [Fact]
    public async Task CreateDrafts_WithAManualEntryDraft_StoresNoKind()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");
        var draft = new NewMaintenanceDraft(Guid.CreateVersion7(), "manual_entry", null, null, "none", null, "{}", null);

        Assert.True(await repository.CreateDraftsAsync(submission, "owner-a", [draft], Now, CancellationToken.None));

        var row = Assert.Single(await ReadDraftsAsync(dataSource));
        Assert.Equal("manual_entry", row.State);
        Assert.Null(row.Kind);
        Assert.Null(row.Confidence);
    }

    [Fact]
    public async Task CreateDrafts_WhenOneDraftFails_WritesNothingAndLeavesTheSubmissionUnchanged()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");

        await Assert.ThrowsAnyAsync<PostgresException>(() =>
            repository.CreateDraftsAsync(submission, "owner-a", [Draft("service_record"), Draft("oil_change")], Now, CancellationToken.None));

        Assert.Empty(await ReadDraftsAsync(dataSource));
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM notifications"));
        Assert.Equal("processing", await SubmissionStatusAsync(dataSource, submission));
    }

    [Fact]
    public async Task CreateDrafts_ForASubmissionOfAnotherOwner_WritesNothing()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing", ownerId: "owner-b");

        var created = await repository.CreateDraftsAsync(submission, "owner-a", [Draft()], Now, CancellationToken.None);

        Assert.False(created);
        Assert.Empty(await ReadDraftsAsync(dataSource));
        Assert.Equal("processing", await SubmissionStatusAsync(dataSource, submission));
    }

    [Fact]
    public async Task CreateDrafts_DoesNotLinkAVehicleTheOwnerDoesNotOwn()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var foreign = TestData.Vehicle("owner-b");
        await vehicles.InsertAsync(foreign, CancellationToken.None);
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");

        await repository.CreateDraftsAsync(submission, "owner-a", [Draft("service_record", foreign.Id)], Now, CancellationToken.None);

        Assert.Null(Assert.Single(await ReadDraftsAsync(dataSource)).VehicleId);
    }

    [Fact]
    public async Task ListOwnerVehiclesForMatching_ReturnsOnlyTheOwnersVehicles()
    {
        var (repository, vehicles, _) = await CreateAsync();
        var mine = TestData.Vehicle("owner-a", model: "Mustang", make: "Ford") with { Year = 2019, Vin = "1FA6P8CF0K5100001" };
        var other = TestData.Vehicle("owner-b", model: "Civic", make: "Honda");
        await vehicles.InsertAsync(mine, CancellationToken.None);
        await vehicles.InsertAsync(TestData.Vehicle("owner-a", model: "F-150"), CancellationToken.None);
        await vehicles.InsertAsync(other, CancellationToken.None);

        var listed = await repository.ListOwnerVehiclesForMatchingAsync("owner-a", CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.DoesNotContain(listed, vehicle => vehicle.Id == other.Id);
        var mustang = Assert.Single(listed, vehicle => vehicle.Id == mine.Id);
        Assert.Equal(2019, mustang.Year);
        Assert.Equal("Ford", mustang.Make);
        Assert.Equal("Mustang", mustang.Model);
        Assert.Equal("1FA6P8CF0K5100001", mustang.Vin);
        Assert.Empty(await repository.ListOwnerVehiclesForMatchingAsync("nobody", CancellationToken.None));
    }

    private sealed class StoredDraft
    {
        public string State { get; init; } = string.Empty;

        public string? Kind { get; init; }

        public string Proposal { get; init; } = string.Empty;

        public DateTimeOffset? DecidedAt { get; init; }
    }

    private static async Task<StoredDraft> ReadDraftAsync(NpgsqlDataSource dataSource, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<StoredDraft>(
            "SELECT state, kind, proposal::text AS proposal, decided_at FROM maintenance_drafts WHERE id = @id", new { id });
    }

    private static async Task<int> CountNotificationsAsync(NpgsqlDataSource dataSource, Guid? draftId = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM notifications WHERE @draftId::uuid IS NULL OR draft_id = @draftId", new { draftId });
    }

    private static async Task<bool> ContentPurgedAsync(NpgsqlDataSource dataSource, Guid submissionId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT content_purged_at IS NOT NULL AND sender IS NULL AND subject IS NULL AND body_text IS NULL FROM email_submissions WHERE id = @submissionId",
            new { submissionId });
    }

    [Fact]
    public async Task ListOpenPage_ReturnsOnlyTheOwnersOpenDrafts_NewestFirst_WithVehicleNameParts()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-a", model: "Mustang", make: "Ford") with { Year = 2019 };
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, arrivedAt: Now.AddHours(-5));
        var otherSubmission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, ownerId: "owner-b");
        var oldest = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, createdAt: Now.AddDays(-3));
        var newest = await EmailIntakeTestData.InsertDraftAsync(
            dataSource, "owner-a", submission, vehicleId: vehicle.Id, createdAt: Now.AddDays(-1), proposalJson: """{"kind":"service_record"}""");
        var manual = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "manual_entry", kind: null, createdAt: Now.AddDays(-2));
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "included", createdAt: Now);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "discarded", createdAt: Now);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-b", otherSubmission, createdAt: Now);

        var page = await repository.ListOpenPageAsync("owner-a", 1, 2, CancellationToken.None);
        var second = await repository.ListOpenPageAsync("owner-a", 2, 2, CancellationToken.None);
        var beyond = await repository.ListOpenPageAsync("owner-a", 3, 2, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal([newest, manual], page.Items.Select(item => item.Id));
        Assert.Equal([oldest], second.Items.Select(item => item.Id));
        Assert.Empty(beyond.Items);
        var first = page.Items[0];
        Assert.Equal("needs_review", first.State);
        Assert.Equal("service_record", first.Kind);
        Assert.Equal(vehicle.Id, first.VehicleId);
        Assert.Equal(2019, first.VehicleYear);
        Assert.Equal("Ford", first.VehicleMake);
        Assert.Equal("Mustang", first.VehicleModel);
        Assert.Contains("service_record", first.Proposal);
        Assert.Equal(Now.AddHours(-5), first.ArrivedAt);
        Assert.Equal(Now.AddDays(-1).AddDays(30), first.ExpiresAt);
        Assert.Null(page.Items[1].VehicleId);
        Assert.Null(page.Items[1].Kind);
        Assert.Empty((await repository.ListOpenPageAsync("owner-c", 1, 10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Get_ReturnsTheDraftWithTheRetainedEmailAndAttachmentDetails_OrNullForAnotherOwner()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, sender: "a@example.com", subject: "Receipt", arrivedAt: Now.AddHours(-2));
        var attachment = await EmailIntakeTestData.InsertAttachmentAsync(
            dataSource, submission, fileName: "receipt.png", contentType: "image/png", sizeBytes: 321, sha256: new string('b', 64));
        var draft = await EmailIntakeTestData.InsertDraftAsync(
            dataSource, "owner-a", submission, attachmentId: attachment, vehicleMatch: "only_vehicle", confidence: 0.55m, notes: "Check the date", proposalJson: """{"kind":"fuel"}""");

        var detail = await repository.GetAsync("owner-a", draft, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(draft, detail.Id);
        Assert.Equal(submission, detail.SubmissionId);
        Assert.Equal(attachment, detail.AttachmentId);
        Assert.Equal("needs_review", detail.State);
        Assert.Equal("only_vehicle", detail.VehicleMatch);
        Assert.Equal(0.55m, detail.Confidence);
        Assert.Equal("Check the date", detail.Notes);
        Assert.Contains("fuel", detail.Proposal);
        Assert.Equal(Now.AddHours(-2), detail.ArrivedAt);
        Assert.Equal("a@example.com", detail.Sender);
        Assert.Equal("Receipt", detail.Subject);
        Assert.Equal("receipt.png", detail.AttachmentFileName);
        Assert.Equal("image/png", detail.AttachmentContentType);
        Assert.Equal(321, detail.AttachmentSizeBytes);
        Assert.Equal(new string('b', 64), detail.AttachmentSha256);
        Assert.Null(await repository.GetAsync("owner-b", draft, CancellationToken.None));
        Assert.Null(await repository.GetAsync("owner-a", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_AndListOpenPage_CarryTheSubmissionsOrigin()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var upload = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, sender: null, subject: null, bodyText: null, origin: "upload");
        var email = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var uploadDraft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", upload, createdAt: Now.AddMinutes(-1));
        var emailDraft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", email, createdAt: Now.AddMinutes(-2));

        Assert.Equal("upload", (await repository.GetAsync("owner-a", uploadDraft, CancellationToken.None))!.Origin);
        Assert.Equal("email", (await repository.GetAsync("owner-a", emailDraft, CancellationToken.None))!.Origin);
        var page = await repository.ListOpenPageAsync("owner-a", 1, 10, CancellationToken.None);
        Assert.Equal("upload", page.Items.Single(item => item.Id == uploadDraft).Origin);
        Assert.Equal("email", page.Items.Single(item => item.Id == emailDraft).Origin);
    }

    [Fact]
    public async Task Get_OfATextDraftWithPurgedContent_HasNoAttachmentAndNoEmailDetails()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, sender: null, subject: null, bodyText: null, contentPurgedAt: Now);
        var draft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "included", decidedAt: Now);

        var detail = await repository.GetAsync("owner-a", draft, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail.AttachmentId);
        Assert.Null(detail.AttachmentFileName);
        Assert.Null(detail.Sender);
        Assert.Null(detail.Subject);
    }

    [Theory]
    [InlineData("needs_review")]
    [InlineData("manual_entry")]
    public async Task ChangeKind_UpdatesTheKindAndProposal_AndMovesManualEntryToNeedsReview(string state)
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var draft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: state, kind: state == "manual_entry" ? null : "service_record");

        var changed = await repository.ChangeKindAsync("owner-a", draft, "tax", """{"kind":"tax"}""", CancellationToken.None);

        Assert.True(changed);
        var stored = await ReadDraftAsync(dataSource, draft);
        Assert.Equal("needs_review", stored.State);
        Assert.Equal("tax", stored.Kind);
        Assert.Contains("tax", stored.Proposal);
    }

    [Fact]
    public async Task ChangeKind_DoesNotTouchADecidedDraftOrAnotherOwnersDraft()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var included = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "included", decidedAt: Now);
        var open = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission);

        Assert.False(await repository.ChangeKindAsync("owner-a", included, "tax", "{}", CancellationToken.None));
        Assert.False(await repository.ChangeKindAsync("owner-b", open, "tax", "{}", CancellationToken.None));

        Assert.Equal("service_record", (await ReadDraftAsync(dataSource, included)).Kind);
        Assert.Equal("service_record", (await ReadDraftAsync(dataSource, open)).Kind);
    }

    [Fact]
    public async Task Discard_MarksTheDraftDiscarded_ClearsItsProposal_RemovesTheNotification_AndPurgesTheEmail()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var attachment = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, submission);
        var draft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, attachmentId: attachment, proposalJson: """{"kind":"service_record"}""");
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", draft);

        var result = await repository.DiscardAsync("owner-a", draft, Now, CancellationToken.None);

        Assert.Equal(DraftDecisionOutcome.Done, result.Outcome);
        Assert.Equal(new DraftAttachmentRef(submission, attachment), result.Attachment);
        var stored = await ReadDraftAsync(dataSource, draft);
        Assert.Equal("discarded", stored.State);
        Assert.Equal(Now, stored.DecidedAt);
        Assert.Equal("{}", stored.Proposal);
        Assert.Equal(0, await CountNotificationsAsync(dataSource));
        Assert.True(await ContentPurgedAsync(dataSource, submission));
    }

    [Fact]
    public async Task Discard_KeepsTheEmailContent_WhileAnotherDraftOfTheSubmissionIsOpen()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var first = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "manual_entry", kind: null);

        var result = await repository.DiscardAsync("owner-a", first, Now, CancellationToken.None);

        Assert.Equal(DraftDecisionOutcome.Done, result.Outcome);
        Assert.Null(result.Attachment);
        Assert.False(await ContentPurgedAsync(dataSource, submission));
    }

    [Fact]
    public async Task Discard_ReportsNotFoundForAnotherOwnersDraft_AndAlreadyDecidedForADecidedOne()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var open = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission);
        var included = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, state: "included", decidedAt: Now);

        Assert.Equal(DraftDecisionOutcome.NotFound, (await repository.DiscardAsync("owner-b", open, Now, CancellationToken.None)).Outcome);
        Assert.Equal(DraftDecisionOutcome.NotFound, (await repository.DiscardAsync("owner-a", Guid.NewGuid(), Now, CancellationToken.None)).Outcome);
        Assert.Equal(DraftDecisionOutcome.AlreadyDecided, (await repository.DiscardAsync("owner-a", included, Now, CancellationToken.None)).Outcome);
        Assert.Equal("needs_review", (await ReadDraftAsync(dataSource, open)).State);
        Assert.Equal("included", (await ReadDraftAsync(dataSource, included)).State);
    }

    [Fact]
    public async Task ExpireDue_ExpiresOnlyDueUndecidedDrafts_OfEveryOwner_AndReturnsTheirAttachments()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var submissionA = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var submissionB = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, ownerId: "owner-b");
        var submissionC = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var attachmentA = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, submissionA);
        var dueA = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, attachmentId: attachmentA, expiresAt: Now.AddMinutes(-1), proposalJson: """{"kind":"fuel"}""");
        var dueB = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-b", submissionB, state: "manual_entry", kind: null, expiresAt: Now);
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", dueA);
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b", dueB);
        var notYet = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionC, expiresAt: Now.AddMinutes(1));
        var decided = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionC, state: "included", expiresAt: Now.AddDays(-5), decidedAt: Now.AddDays(-6));

        var expired = await repository.ExpireDueAsync(Now, CancellationToken.None);

        Assert.Equal(2, expired.Count);
        Assert.Equal([new DraftAttachmentRef(submissionA, attachmentA)], expired.Attachments);
        var storedA = await ReadDraftAsync(dataSource, dueA);
        Assert.Equal("expired", storedA.State);
        Assert.Equal(Now, storedA.DecidedAt);
        Assert.Equal("{}", storedA.Proposal);
        Assert.Equal("expired", (await ReadDraftAsync(dataSource, dueB)).State);
        Assert.Equal("needs_review", (await ReadDraftAsync(dataSource, notYet)).State);
        Assert.Equal("included", (await ReadDraftAsync(dataSource, decided)).State);
        Assert.Equal(0, await CountNotificationsAsync(dataSource));
        Assert.True(await ContentPurgedAsync(dataSource, submissionA));
        Assert.True(await ContentPurgedAsync(dataSource, submissionB));
        Assert.False(await ContentPurgedAsync(dataSource, submissionC));
        Assert.Equal(0, (await repository.ExpireDueAsync(Now, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task HasDraftWithSameAttachment_FindsAnotherNonDiscardedDraftOfTheSameOwner_ByHash()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var hash = new string('c', 64);
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var thisAttachment = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, submission, sha256: hash);
        var thisDraft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission, attachmentId: thisAttachment);

        Assert.False(await repository.HasDraftWithSameAttachmentAsync("owner-a", thisDraft, hash, CancellationToken.None));

        var earlierSubmission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var earlierAttachment = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, earlierSubmission, sha256: hash);
        var discarded = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", earlierSubmission, state: "discarded", attachmentId: earlierAttachment, decidedAt: Now);
        Assert.False(await repository.HasDraftWithSameAttachmentAsync("owner-a", thisDraft, hash, CancellationToken.None));

        var otherOwnerSubmission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, ownerId: "owner-b");
        var otherOwnerAttachment = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, otherOwnerSubmission, ownerId: "owner-b", sha256: hash);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-b", otherOwnerSubmission, attachmentId: otherOwnerAttachment);
        Assert.False(await repository.HasDraftWithSameAttachmentAsync("owner-a", thisDraft, hash, CancellationToken.None));

        var includedSubmission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var includedAttachment = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, includedSubmission, sha256: hash);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", includedSubmission, state: "included", attachmentId: includedAttachment, decidedAt: Now);

        Assert.True(await repository.HasDraftWithSameAttachmentAsync("owner-a", thisDraft, hash, CancellationToken.None));
        Assert.NotEqual(Guid.Empty, discarded);
    }

    [Fact]
    public async Task HasRecordLike_MatchesTheSameDateAndTotalOnTheOwnersVehicleOnly()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var mine = TestData.Vehicle("owner-a");
        var theirs = TestData.Vehicle("owner-b");
        await vehicles.InsertAsync(mine, CancellationToken.None);
        await vehicles.InsertAsync(theirs, CancellationToken.None);
        var day = new DateOnly(2026, 10, 20);
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, mine.Id, day);
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, flatCost: 50m);
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, flatCost: 25.50m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, mine.Id, entryDate: day, totalCost: 42.70m);
        await MaintenanceTestData.InsertInspectionAsync(dataSource, mine.Id, inspectionDate: day, cost: 25m);
        await MaintenanceTestData.InsertRegistrationAsync(dataSource, mine.Id, expirationDate: day, cost: 78.75m);
        await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, mine.Id, startDate: day, cost: 1240m);

        Assert.True(await repository.HasRecordLikeAsync("owner-a", mine.Id, "service_record", day, 75.50m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-a", mine.Id, "service_record", day, 75m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-a", mine.Id, "service_record", day.AddDays(1), 75.50m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-b", mine.Id, "service_record", day, 75.50m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-b", theirs.Id, "service_record", day, 75.50m, CancellationToken.None));
        Assert.True(await repository.HasRecordLikeAsync("owner-a", mine.Id, "fuel", day, 42.70m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-a", mine.Id, "energy", day, 42.70m, CancellationToken.None));
        Assert.True(await repository.HasRecordLikeAsync("owner-a", mine.Id, "inspection", day, 25m, CancellationToken.None));
        Assert.True(await repository.HasRecordLikeAsync("owner-a", mine.Id, "registration", day, 78.75m, CancellationToken.None));
        Assert.True(await repository.HasRecordLikeAsync("owner-a", mine.Id, "insurance", day, 1240m, CancellationToken.None));
        Assert.False(await repository.HasRecordLikeAsync("owner-a", mine.Id, "tax", day, 312.40m, CancellationToken.None));
    }
}

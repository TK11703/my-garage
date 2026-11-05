using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.EmailIntake;

[Collection(DatabaseCollection.Name)]
public class EmailHistoryRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private async Task<(EmailSubmissionRepository Repository, VehicleRepository Vehicles, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (new EmailSubmissionRepository(dataSource, scripts), new VehicleRepository(dataSource, scripts), dataSource);
    }

    // A submission that arrived `minutes` after T0 with the given drafts: (state, kind, vehicle, record id).
    private static async Task<Guid> SubmissionAsync(
        NpgsqlDataSource dataSource,
        int minutes,
        string status = "ready",
        string ownerId = "owner-a",
        params (string State, string? Kind, Guid? VehicleId, Guid? RecordId)[] drafts)
    {
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(
            dataSource, status: status, ownerId: ownerId, arrivedAt: T0.AddMinutes(minutes));
        foreach (var (state, kind, vehicleId, recordId) in drafts)
        {
            var decided = state is "included" or "discarded" or "expired" ? T0.AddMinutes(minutes + 1) : (DateTimeOffset?)null;
            await EmailIntakeTestData.InsertDraftAsync(
                dataSource,
                ownerId,
                submission,
                state: state,
                kind: kind,
                vehicleId: vehicleId,
                decidedAt: decided,
                createdRecordKind: state == "included" ? kind : null,
                createdRecordId: state == "included" ? recordId : null);
        }

        return submission;
    }

    [Fact]
    public async Task ListHistoryPage_ReturnsTheOwnersEmailsNewestFirst_WithTheDerivedOutcomeAndCreatedRecords()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-a");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var otherVehicle = TestData.Vehicle("owner-b");
        await vehicles.InsertAsync(otherVehicle, CancellationToken.None);
        var fuelRecord = Guid.CreateVersion7();
        var serviceRecord = Guid.CreateVersion7();
        var taxRecord = Guid.CreateVersion7();

        var open = await SubmissionAsync(dataSource, 1, drafts: [("needs_review", "service_record", null, null)]);
        var manual = await SubmissionAsync(dataSource, 2, drafts: [("manual_entry", null, null, null)]);
        var included = await SubmissionAsync(dataSource, 3, drafts: [("included", "fuel", vehicle.Id, fuelRecord)]);
        var discarded = await SubmissionAsync(dataSource, 4, drafts: [("discarded", "tax", null, null)]);
        var expired = await SubmissionAsync(dataSource, 5, drafts: [("expired", "tax", null, null), ("expired", null, null, null)]);
        var notApplicable = await SubmissionAsync(dataSource, 6, status: "not_applicable");
        var failed = await SubmissionAsync(dataSource, 7, status: "failed");
        var openAndIncluded = await SubmissionAsync(
            dataSource, 8, drafts: [("included", "service_record", vehicle.Id, serviceRecord), ("needs_review", "tax", null, null)]);
        var includedAndDiscarded = await SubmissionAsync(
            dataSource, 9, drafts: [("discarded", "fuel", null, null), ("included", "tax", vehicle.Id, taxRecord)]);

        // Never listed: still being read, no owner, another owner's, and one with only another owner's draft.
        await SubmissionAsync(dataSource, 10, status: "queued");
        await SubmissionAsync(dataSource, 11, status: "processing");
        await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "not_accepted", ownerId: null, sender: null, subject: null, bodyText: null, arrivedAt: T0.AddMinutes(12));
        await SubmissionAsync(dataSource, 13, ownerId: "owner-b", drafts: [("included", "fuel", otherVehicle.Id, Guid.CreateVersion7())]);

        var page = await repository.ListHistoryPageAsync("owner-a", 1, 20, CancellationToken.None);

        Assert.Equal(9, page.TotalCount);
        Assert.Equal(
            [includedAndDiscarded, openAndIncluded, failed, notApplicable, expired, discarded, included, manual, open],
            page.Items.Select(item => item.SubmissionId));
        Assert.Equal(
            ["included", "awaiting_review", "failed", "not_applicable", "expired", "discarded", "included", "awaiting_review", "awaiting_review"],
            page.Items.Select(item => item.Outcome));
        Assert.Equal(T0.AddMinutes(9), page.Items[0].ArrivedAt);

        var taxOnly = page.Items[0].Records;
        Assert.Equal(new EmailHistoryCreatedRecord("tax", vehicle.Id, taxRecord), Assert.Single(taxOnly));
        Assert.Equal(new EmailHistoryCreatedRecord("service_record", vehicle.Id, serviceRecord), Assert.Single(page.Items[1].Records));
        Assert.Equal(new EmailHistoryCreatedRecord("fuel", vehicle.Id, fuelRecord), Assert.Single(page.Items.Single(item => item.SubmissionId == included).Records));
        Assert.All(
            page.Items.Where(item => item.SubmissionId is var id && id != includedAndDiscarded && id != openAndIncluded && id != included),
            item => Assert.Empty(item.Records));
    }

    [Fact]
    public async Task ListHistoryPage_CarriesTheOriginOfEachSubmission()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var email = await SubmissionAsync(dataSource, 1, drafts: [("needs_review", "service_record", null, null)]);
        var upload = await EmailIntakeTestData.InsertSubmissionAsync(
            dataSource, status: "ready", sender: null, subject: null, bodyText: null, arrivedAt: T0.AddMinutes(2), origin: "upload");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", upload);

        var page = await repository.ListHistoryPageAsync("owner-a", 1, 10, CancellationToken.None);

        Assert.Equal("upload", page.Items.Single(item => item.SubmissionId == upload).Origin);
        Assert.Equal("email", page.Items.Single(item => item.SubmissionId == email).Origin);
    }

    [Fact]
    public async Task ListHistoryPage_NeverShowsAnotherOwnersEmails()
    {
        var (repository, _, dataSource) = await CreateAsync();
        await SubmissionAsync(dataSource, 1, drafts: [("needs_review", "service_record", null, null)]);
        var theirs = await SubmissionAsync(dataSource, 2, ownerId: "owner-b", drafts: [("needs_review", "fuel", null, null)]);

        var mine = await repository.ListHistoryPageAsync("owner-a", 1, 10, CancellationToken.None);
        var nobody = await repository.ListHistoryPageAsync("owner-c", 1, 10, CancellationToken.None);

        Assert.Equal(1, mine.TotalCount);
        Assert.DoesNotContain(mine.Items, item => item.SubmissionId == theirs);
        Assert.Equal(0, nobody.TotalCount);
        Assert.Empty(nobody.Items);
    }

    [Fact]
    public async Task ListHistoryPage_PagesNewestFirst_AndCountsEverything()
    {
        var (repository, _, dataSource) = await CreateAsync();
        var ids = new List<Guid>();
        for (var minute = 1; minute <= 25; minute++)
        {
            ids.Add(await SubmissionAsync(dataSource, minute, status: "not_applicable"));
        }

        var second = await repository.ListHistoryPageAsync("owner-a", 2, 10, CancellationToken.None);
        var last = await repository.ListHistoryPageAsync("owner-a", 3, 10, CancellationToken.None);
        var beyond = await repository.ListHistoryPageAsync("owner-a", 4, 10, CancellationToken.None);

        Assert.Equal(25, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, second.Page);
        Assert.Equal(10, second.PageSize);
        Assert.Equal(Enumerable.Reverse(ids).Skip(10).Take(10), second.Items.Select(item => item.SubmissionId));
        Assert.Equal(Enumerable.Reverse(ids).Skip(20), last.Items.Select(item => item.SubmissionId));
        Assert.Empty(beyond.Items);
        Assert.Equal(25, beyond.TotalCount);
    }

    [Fact]
    public async Task ListHistoryPage_AnIncludedDraftWhoseVehicleWasDeleted_ShowsNoRecordLink()
    {
        var (repository, vehicles, dataSource) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-a");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var submission = await SubmissionAsync(dataSource, 1, drafts: [("included", "fuel", vehicle.Id, Guid.CreateVersion7())]);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @id", new { id = vehicle.Id });
        }

        var page = await repository.ListHistoryPageAsync("owner-a", 1, 10, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(submission, item.SubmissionId);
        Assert.Equal("included", item.Outcome);
        Assert.Empty(item.Records);
    }
}

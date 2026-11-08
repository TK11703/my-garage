using MyGarage.Database.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.Reminders;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Drafts;

[Collection(DatabaseCollection.Name)]
public class ArchivedVehicleDraftsRepositoryTests(PostgresFixture postgres)
{
    private const string Owner = "owner-1";

    [Fact]
    public async Task AnArchivedVehicle_IsNotAMatchCandidate_AndADraftNamingItReadsAsNoVehicle_UntilItIsRestored()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        var drafts = new MaintenanceDraftRepository(dataSource, scripts);
        var vehicles = new VehicleRepository(dataSource, scripts);
        var active = await ReminderTestData.InsertVehicleAsync(dataSource, Owner, make: "Honda", model: "Civic");
        var archived = await ReminderTestData.InsertVehicleAsync(dataSource, Owner, make: "Ford", model: "Mustang");
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: Owner);
        var draftId = await EmailIntakeTestData.InsertDraftAsync(dataSource, Owner, submission, state: "needs_review", vehicleId: archived);
        await vehicles.ArchiveAsync(Owner, archived, DateTimeOffset.UtcNow, CancellationToken.None);

        var candidates = await drafts.ListOwnerVehiclesForMatchingAsync(Owner, CancellationToken.None);
        var page = await drafts.ListOpenPageAsync(Owner, 1, 10, CancellationToken.None);
        var detail = await drafts.GetAsync(Owner, draftId, CancellationToken.None);

        Assert.Equal([active], candidates.Select(candidate => candidate.Id));
        Assert.Null(page.Items.Single().VehicleId);
        Assert.Null(page.Items.Single().VehicleModel);
        Assert.Null(detail!.VehicleId);

        await vehicles.RestoreAsync(Owner, archived, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(archived, (await drafts.GetAsync(Owner, draftId, CancellationToken.None))!.VehicleId);
        Assert.Equal(archived, (await drafts.ListOpenPageAsync(Owner, 1, 10, CancellationToken.None)).Items.Single().VehicleId);
        Assert.Equal(2, (await drafts.ListOwnerVehiclesForMatchingAsync(Owner, CancellationToken.None)).Count);
    }
}

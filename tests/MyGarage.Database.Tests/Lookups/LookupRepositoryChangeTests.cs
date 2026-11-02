using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Lookups;

[Collection(DatabaseCollection.Name)]
public class LookupRepositoryChangeTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private async Task<(LookupRepository Repository, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new LookupRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    [Fact]
    public async Task AddCustom_CreatesAnOwnerScopedShownItem()
    {
        var (repository, dataSource) = await CreateAsync();

        var result = await repository.AddCustomAsync("owner-1", LookupKind.FuelType, "Plug-in Hybrid", Now, CancellationToken.None);

        Assert.Equal(AddLookupOutcome.Created, result.Outcome);
        Assert.NotNull(result.Item);
        Assert.Equal("Plug-in Hybrid", result.Item.Name);
        Assert.Equal(LookupKind.FuelType, result.Item.Kind);
        Assert.False(result.Item.IsDefault);
        Assert.False(result.Item.IsHidden);
        await using var connection = await dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(string OwnerId, string CreatedBy, int SortOrder, DateTime CreatedAt)>(
            "SELECT owner_id AS OwnerId, created_by AS CreatedBy, sort_order AS SortOrder, created_at AS CreatedAt FROM lookup_items WHERE id = @id", new { id = result.Item.Id });
        Assert.Equal("owner-1", row.OwnerId);
        Assert.Equal("owner-1", row.CreatedBy);
        Assert.Equal(0, row.SortOrder);
    }

    [Fact]
    public async Task AddCustom_TrimsTheName()
    {
        var (repository, _) = await CreateAsync();

        var result = await repository.AddCustomAsync("owner-1", LookupKind.Drivetrain, "  Rear-mid  ", Now, CancellationToken.None);

        Assert.Equal("Rear-mid", result.Item!.Name);
    }

    [Theory]
    [InlineData("gasoline")]
    [InlineData("  GASOLINE ")]
    public async Task AddCustom_NameOfADefault_IsADuplicateIgnoringCaseAndSpaces(string name)
    {
        var (repository, _) = await CreateAsync();

        var result = await repository.AddCustomAsync("owner-1", LookupKind.FuelType, name, Now, CancellationToken.None);

        Assert.Equal(AddLookupOutcome.Duplicate, result.Outcome);
        Assert.False(result.ExistingIsHidden);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task AddCustom_NameOfAHiddenDefault_ReportsItIsHidden()
    {
        var (repository, dataSource) = await CreateAsync();
        await LookupTestData.HideAsync(dataSource, "owner-1", await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan"));

        var result = await repository.AddCustomAsync("owner-1", LookupKind.VehicleType, "sedan", Now, CancellationToken.None);
        var otherOwner = await repository.AddCustomAsync("owner-2", LookupKind.VehicleType, "sedan", Now, CancellationToken.None);

        Assert.Equal(AddLookupOutcome.Duplicate, result.Outcome);
        Assert.True(result.ExistingIsHidden);
        Assert.Equal(AddLookupOutcome.Duplicate, otherOwner.Outcome);
        Assert.False(otherOwner.ExistingIsHidden);
    }

    [Fact]
    public async Task AddCustom_SameNameAgainForTheSameOwner_IsADuplicate_ButAnotherOwnerAndAnotherKindMayUseIt()
    {
        var (repository, _) = await CreateAsync();
        await repository.AddCustomAsync("owner-1", LookupKind.Drivetrain, "Rear-mid", Now, CancellationToken.None);

        var again = await repository.AddCustomAsync("owner-1", LookupKind.Drivetrain, "REAR-MID", Now, CancellationToken.None);
        var otherOwner = await repository.AddCustomAsync("owner-2", LookupKind.Drivetrain, "Rear-mid", Now, CancellationToken.None);
        var otherKind = await repository.AddCustomAsync("owner-1", LookupKind.Transmission, "Rear-mid", Now, CancellationToken.None);

        Assert.Equal(AddLookupOutcome.Duplicate, again.Outcome);
        Assert.Equal(AddLookupOutcome.Created, otherOwner.Outcome);
        Assert.Equal(AddLookupOutcome.Created, otherKind.Outcome);
    }

    [Fact]
    public async Task AddCustom_BeyondFiftyItemsOfOneKind_IsRefusedWithoutAffectingOtherKindsOrOwners()
    {
        var (repository, _) = await CreateAsync();
        for (var i = 1; i <= 50; i++)
        {
            Assert.Equal(AddLookupOutcome.Created, (await repository.AddCustomAsync("owner-1", LookupKind.VehicleType, $"Type {i}", Now, CancellationToken.None)).Outcome);
        }

        Assert.Equal(AddLookupOutcome.LimitReached, (await repository.AddCustomAsync("owner-1", LookupKind.VehicleType, "Type 51", Now, CancellationToken.None)).Outcome);
        Assert.Equal(AddLookupOutcome.Created, (await repository.AddCustomAsync("owner-1", LookupKind.FuelType, "Other kind", Now, CancellationToken.None)).Outcome);
        Assert.Equal(AddLookupOutcome.Created, (await repository.AddCustomAsync("owner-2", LookupKind.VehicleType, "Type 51", Now, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task AddCustom_ConcurrentRequests_NeverExceedTheLimit()
    {
        var (repository, dataSource) = await CreateAsync();
        for (var i = 1; i <= 45; i++)
        {
            await LookupTestData.InsertCustomAsync(dataSource, "owner-1", LookupKind.Drivetrain, $"Seed {i}");
        }

        var results = await Task.WhenAll(Enumerable.Range(1, 20)
            .Select(i => repository.AddCustomAsync("owner-1", LookupKind.Drivetrain, $"Racer {i}", Now, CancellationToken.None)));

        Assert.Equal(5, results.Count(result => result.Outcome == AddLookupOutcome.Created));
        Assert.Equal(15, results.Count(result => result.Outcome == AddLookupOutcome.LimitReached));
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(50, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM lookup_items WHERE owner_id = 'owner-1' AND kind = 'drivetrain'"));
    }

    [Fact]
    public async Task HideThenShow_ChangesOnlyTheCallersView_AndIsIdempotent()
    {
        var (repository, dataSource) = await CreateAsync();
        var sedan = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan");

        var hidden = await repository.HideAsync("owner-1", LookupKind.VehicleType, sedan, Now, CancellationToken.None);
        var hiddenAgain = await repository.HideAsync("owner-1", LookupKind.VehicleType, sedan, Now, CancellationToken.None);

        Assert.True(hidden!.IsHidden);
        Assert.True(hiddenAgain!.IsHidden);
        Assert.DoesNotContain(await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None), item => item.Id == sedan);
        Assert.Contains(await repository.ListAsync("owner-2", includeHidden: false, CancellationToken.None), item => item.Id == sedan);

        var shown = await repository.ShowAsync("owner-1", LookupKind.VehicleType, sedan, CancellationToken.None);
        var shownAgain = await repository.ShowAsync("owner-1", LookupKind.VehicleType, sedan, CancellationToken.None);

        Assert.False(shown!.IsHidden);
        Assert.False(shownAgain!.IsHidden);
        Assert.Contains(await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None), item => item.Id == sedan);
    }

    [Fact]
    public async Task Hide_CustomItemOfTheOwner_Works()
    {
        var (repository, dataSource) = await CreateAsync();
        var mine = await LookupTestData.InsertCustomAsync(dataSource, "owner-1", LookupKind.FuelType, "Mine");

        var hidden = await repository.HideAsync("owner-1", LookupKind.FuelType, mine, Now, CancellationToken.None);

        Assert.True(hidden!.IsHidden);
        Assert.False(hidden.IsDefault);
    }

    [Fact]
    public async Task HideAndShow_UnknownWrongKindOrForeignItems_ReturnNullAndChangeNothing()
    {
        var (repository, dataSource) = await CreateAsync();
        var sedan = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan");
        var theirs = await LookupTestData.InsertCustomAsync(dataSource, "owner-2", LookupKind.FuelType, "Theirs");

        Assert.Null(await repository.HideAsync("owner-1", LookupKind.VehicleType, Guid.NewGuid(), Now, CancellationToken.None));
        Assert.Null(await repository.HideAsync("owner-1", LookupKind.FuelType, sedan, Now, CancellationToken.None));
        Assert.Null(await repository.HideAsync("owner-1", LookupKind.FuelType, theirs, Now, CancellationToken.None));
        Assert.Null(await repository.ShowAsync("owner-1", LookupKind.FuelType, theirs, CancellationToken.None));

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM lookup_item_hidden"));
    }

    [Fact]
    public async Task NewDefaultsAddedLater_AppearForEveryoneAndStayShownForTheirOwnersChoices()
    {
        var (repository, dataSource) = await CreateAsync();
        var sedan = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan");
        await repository.HideAsync("owner-1", LookupKind.VehicleType, sedan, Now, CancellationToken.None);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO lookup_items (id, kind, owner_id, name, sort_order, created_at, created_by) VALUES (gen_random_uuid(), 'vehicle_type', NULL, 'Pickup', 12, now(), NULL)");
        }

        var hider = await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None);
        var newcomer = await repository.ListAsync("owner-3", includeHidden: false, CancellationToken.None);

        Assert.Contains(hider, item => item.Name == "Pickup" && !item.IsHidden);
        Assert.DoesNotContain(hider, item => item.Name == "Sedan");
        Assert.Contains(newcomer, item => item.Name == "Pickup" && !item.IsHidden);
        Assert.Equal("Pickup", newcomer.Where(item => item.Kind == LookupKind.VehicleType).Last().Name);
    }
}

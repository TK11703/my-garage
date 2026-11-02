using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Lookups;

[Collection(DatabaseCollection.Name)]
public class LookupRepositoryTests(PostgresFixture postgres)
{
    private async Task<(LookupRepository Repository, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new LookupRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    [Fact]
    public async Task List_NewOwner_SeesAllDefaultsInDefinedOrderAndNoneHidden()
    {
        var (repository, _) = await CreateAsync();

        var items = await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None);

        Assert.Equal(21, items.Count);
        Assert.All(items, item =>
        {
            Assert.True(item.IsDefault);
            Assert.False(item.IsHidden);
        });
        Assert.Equal(
            ["Sedan", "SUV", "Coupe", "Hatchback", "Convertible", "Truck", "Van", "Wagon", "Minivan", "Crossover", "Motorcycle"],
            items.Where(item => item.Kind == LookupKind.VehicleType).Select(item => item.Name));
        Assert.Equal(["Gasoline", "Diesel", "Electric", "Hybrid"], items.Where(item => item.Kind == LookupKind.FuelType).Select(item => item.Name));
        Assert.Equal(["Automatic", "Manual"], items.Where(item => item.Kind == LookupKind.Transmission).Select(item => item.Name));
        Assert.Equal(["FWD", "RWD", "AWD", "4WD"], items.Where(item => item.Kind == LookupKind.Drivetrain).Select(item => item.Name));
    }

    [Fact]
    public async Task List_HiddenItem_IsOmittedOrFlaggedForThatOwnerOnly()
    {
        var (repository, dataSource) = await CreateAsync();
        var sedan = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan");
        await LookupTestData.HideAsync(dataSource, "owner-1", sedan);

        var withoutHidden = await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None);
        var withHidden = await repository.ListAsync("owner-1", includeHidden: true, CancellationToken.None);
        var otherOwner = await repository.ListAsync("owner-2", includeHidden: false, CancellationToken.None);

        Assert.DoesNotContain(withoutHidden, item => item.Id == sedan);
        Assert.Contains(withHidden, item => item.Id == sedan && item.IsHidden);
        Assert.Contains(otherOwner, item => item.Id == sedan && !item.IsHidden);
        Assert.Equal(20, withoutHidden.Count);
        Assert.Equal(21, withHidden.Count);
    }

    [Fact]
    public async Task FindAvailable_ReturnsDefaultsAndOwnItemsOnly()
    {
        var (repository, dataSource) = await CreateAsync();
        var diesel = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.FuelType, "Diesel");
        var mine = await LookupTestData.InsertCustomAsync(dataSource, "owner-1", LookupKind.FuelType, "Mine");
        var theirs = await LookupTestData.InsertCustomAsync(dataSource, "owner-2", LookupKind.FuelType, "Theirs");

        var found = await repository.FindAvailableAsync("owner-1", [diesel, mine, theirs, Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(new[] { diesel, mine }.Order(), found.Select(item => item.Id).Order());
        Assert.All(found, item => Assert.Equal(LookupKind.FuelType, item.Kind));
    }

    [Fact]
    public async Task FindAvailable_ReportsHiddenItemsAndHandlesNoIds()
    {
        var (repository, dataSource) = await CreateAsync();
        var sedan = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Sedan");
        await LookupTestData.HideAsync(dataSource, "owner-1", sedan);

        var hidden = await repository.FindAvailableAsync("owner-1", [sedan], CancellationToken.None);
        var shown = await repository.FindAvailableAsync("owner-2", [sedan], CancellationToken.None);

        Assert.True(Assert.Single(hidden).IsHidden);
        Assert.False(Assert.Single(shown).IsHidden);
        Assert.Empty(await repository.FindAvailableAsync("owner-1", [], CancellationToken.None));
    }

    [Fact]
    public async Task List_CustomItems_FollowDefaultsAlphabeticallyAndStayPrivate()
    {
        var (repository, dataSource) = await CreateAsync();
        await LookupTestData.InsertCustomAsync(dataSource, "owner-1", LookupKind.FuelType, "Plug-in Hybrid");
        await LookupTestData.InsertCustomAsync(dataSource, "owner-1", LookupKind.FuelType, "biodiesel");

        var owner1 = await repository.ListAsync("owner-1", includeHidden: false, CancellationToken.None);
        var owner2 = await repository.ListAsync("owner-2", includeHidden: false, CancellationToken.None);

        var fuel = owner1.Where(item => item.Kind == LookupKind.FuelType).ToList();
        Assert.Equal(["Gasoline", "Diesel", "Electric", "Hybrid", "biodiesel", "Plug-in Hybrid"], fuel.Select(item => item.Name));
        Assert.All(fuel.Skip(4), item => Assert.False(item.IsDefault));
        Assert.DoesNotContain(owner2, item => !item.IsDefault);
    }
}

using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleRepositoryTests(PostgresFixture postgres)
{
    private async Task<VehicleRepository> CreateRepositoryAsync() =>
        new(await postgres.CreateMigratedDatabaseAsync(), new SqlScriptStore());

    [Fact]
    public async Task InsertThenGet_RoundTripsEveryColumn()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1", "Blue Streak", "Subaru", "Outback", "Limited", "Blue", 42_000, 4);

        await repository.InsertAsync(vehicle, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.Equal(vehicle, loaded);
    }

    [Fact]
    public async Task InsertThenGet_RoundTripsNullOptionalColumns()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1");

        await repository.InsertAsync(vehicle, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Trim);
        Assert.Null(loaded.Color);
        Assert.Null(loaded.Mileage);
        Assert.Null(loaded.Doors);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
    }

    [Fact]
    public async Task GetById_AnotherOwner_ReturnsNull()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await repository.InsertAsync(vehicle, CancellationToken.None);

        Assert.Null(await repository.GetByIdAsync("owner-2", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListPage_ReturnsOnlyOwnersVehiclesOrderedByNicknameIgnoringCase()
    {
        var repository = await CreateRepositoryAsync();
        foreach (var nickname in new[] { "charlie", "Alpha", "bravo" })
        {
            await repository.InsertAsync(TestData.Vehicle("owner-1", nickname), CancellationToken.None);
        }

        await repository.InsertAsync(TestData.Vehicle("owner-2", "Aardvark"), CancellationToken.None);

        var page = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 1, 12, CancellationToken.None);

        Assert.Equal(["Alpha", "bravo", "charlie"], page.Items.Select(item => item.Nickname));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ListPage_Paging_ReturnsSlicesAndTotals()
    {
        var repository = await CreateRepositoryAsync();
        for (var i = 1; i <= 25; i++)
        {
            await repository.InsertAsync(TestData.Vehicle("owner-1", $"Car {i:D2}"), CancellationToken.None);
        }

        var first = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 1, 12, CancellationToken.None);
        var second = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 2, 12, CancellationToken.None);
        var third = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 3, 12, CancellationToken.None);

        Assert.Equal(12, first.Items.Count);
        Assert.Equal(12, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal("Car 01", first.Items[0].Nickname);
        Assert.Equal("Car 25", third.Items[0].Nickname);
        Assert.All([first, second, third], page => Assert.Equal(25, page.TotalCount));
        Assert.Equal(3, first.TotalPages);
    }

    [Fact]
    public async Task ListPage_EqualNicknames_KeepAStableOrderAcrossPages()
    {
        var repository = await CreateRepositoryAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var vehicle = TestData.Vehicle("owner-1", "Same");
            ids.Add(vehicle.Id);
            await repository.InsertAsync(vehicle, CancellationToken.None);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            seen.AddRange((await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, page, 2, CancellationToken.None)).Items.Select(item => item.Id));
        }

        Assert.Equal(ids.Order(), seen);
    }

    [Fact]
    public async Task ListPage_BeyondLastPage_ReturnsEmptySliceWithRealTotal()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertAsync(TestData.Vehicle("owner-1"), CancellationToken.None);

        var page = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 5, 12, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }
}

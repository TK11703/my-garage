using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tags;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Tags;

[Collection(DatabaseCollection.Name)]
public class VehicleTagRepositoryTests(PostgresFixture postgres)
{
    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, VehicleTagRepository Tags)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new VehicleTagRepository(dataSource, scripts));
    }

    private static Task<AddTagResult> AddAsync(VehicleTagRepository tags, string ownerId, Guid vehicleId, string name) =>
        tags.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), name, TestData.Created, CancellationToken.None);

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    [Fact]
    public async Task Add_ReturnsTheTagAndListsIt()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var result = await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        Assert.Equal(AddTagStatus.Added, result.Status);
        Assert.NotNull(result.Tag);
        Assert.Equal("Sunroof", result.Tag.Name);
        var listed = await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None);
        Assert.Equal([result.Tag.Id], listed.Select(tag => tag.Id));
    }

    [Theory]
    [InlineData("sunroof")]
    [InlineData("SUNROOF")]
    public async Task Add_SameTagIgnoringCase_IsADuplicate(string second)
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        var result = await AddAsync(tags, "owner-1", vehicle.Id, second);

        Assert.Equal(AddTagStatus.Duplicate, result.Status);
        Assert.Single(await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Add_SameTagOnADifferentVehicle_IsAllowed()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var first = await InsertVehicleAsync(vehicles, "owner-1");
        var second = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", first.Id, "Sunroof");

        Assert.Equal(AddTagStatus.Added, (await AddAsync(tags, "owner-1", second.Id, "Sunroof")).Status);
    }

    [Fact]
    public async Task Add_The21stTag_ReachesTheLimitAndStoresNothing()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var i = 1; i <= 20; i++)
        {
            Assert.Equal(AddTagStatus.Added, (await AddAsync(tags, "owner-1", vehicle.Id, $"tag {i}")).Status);
        }

        var result = await AddAsync(tags, "owner-1", vehicle.Id, "one too many");

        Assert.Equal(AddTagStatus.LimitReached, result.Status);
        Assert.Equal(20, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Add_AtTheLimit_ADuplicateIsReportedAsADuplicate()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var i = 1; i <= 20; i++)
        {
            await AddAsync(tags, "owner-1", vehicle.Id, $"tag {i}");
        }

        Assert.Equal(AddTagStatus.Duplicate, (await AddAsync(tags, "owner-1", vehicle.Id, "TAG 1")).Status);
    }

    [Fact]
    public async Task Add_ConcurrentAdds_CannotExceedTheLimit()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var results = await Task.WhenAll(Enumerable.Range(1, 30).Select(i => AddAsync(tags, "owner-1", vehicle.Id, $"tag {i}")));

        Assert.Equal(20, results.Count(result => result.Status == AddTagStatus.Added));
        Assert.Equal(10, results.Count(result => result.Status == AddTagStatus.LimitReached));
        Assert.Equal(20, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Add_AnotherOwnersVehicle_IsNotFoundAndInsertsNothing()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var result = await AddAsync(tags, "owner-2", vehicle.Id, "Sunroof");

        Assert.Equal(AddTagStatus.VehicleNotFound, result.Status);
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
    }

    [Fact]
    public async Task Add_UnknownVehicle_IsNotFound()
    {
        var (_, _, tags) = await CreateAsync();

        Assert.Equal(AddTagStatus.VehicleNotFound, (await AddAsync(tags, "owner-1", Guid.NewGuid(), "Sunroof")).Status);
    }

    [Fact]
    public async Task Remove_DeletesOnlyThatTag()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = (await AddAsync(tags, "owner-1", vehicle.Id, "keep")).Tag!;
        var drop = (await AddAsync(tags, "owner-1", vehicle.Id, "drop")).Tag!;

        var removed = await tags.RemoveAsync("owner-1", vehicle.Id, drop.Id, CancellationToken.None);

        Assert.True(removed);
        Assert.Equal([keep.Id], (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Select(tag => tag.Id));
    }

    [Fact]
    public async Task Remove_WithAnotherOwnersIds_ReturnsFalseAndKeepsTheTag()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var tag = (await AddAsync(tags, "owner-1", vehicle.Id, "mine")).Tag!;

        Assert.False(await tags.RemoveAsync("owner-2", vehicle.Id, tag.Id, CancellationToken.None));
        Assert.Single(await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_TagFromADifferentVehicle_ReturnsFalse()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var first = await InsertVehicleAsync(vehicles, "owner-1");
        var second = await InsertVehicleAsync(vehicles, "owner-1");
        var tag = (await AddAsync(tags, "owner-1", first.Id, "mine")).Tag!;

        Assert.False(await tags.RemoveAsync("owner-1", second.Id, tag.Id, CancellationToken.None));
        Assert.Single(await tags.ListAsync("owner-1", first.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_ForAnotherOwnersVehicle_IsEmpty()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", vehicle.Id, "mine");

        Assert.Empty(await tags.ListAsync("owner-2", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_IsOrderedByCreationTime()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await tags.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), "second", TestData.Created.AddMinutes(1), CancellationToken.None);
        await tags.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), "first", TestData.Created, CancellationToken.None);

        var listed = await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.Equal(["first", "second"], listed.Select(tag => tag.Name));
    }
}

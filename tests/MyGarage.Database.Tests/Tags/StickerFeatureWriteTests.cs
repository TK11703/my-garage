using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tags;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Tags;

[Collection(DatabaseCollection.Name)]
public class StickerFeatureWriteTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, VehicleTagRepository Tags)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new VehicleTagRepository(dataSource, scripts));
    }

    private static StickerFeatureWrite Feature(string name, params Guid[] replaces) => new(name, replaces);

    private static async Task<Guid> AddTagAsync(VehicleTagRepository tags, string ownerId, Guid vehicleId, string name)
    {
        var added = await tags.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), name, TestData.Created, CancellationToken.None);
        return added.Tag!.Id;
    }

    private static async Task<string[]> NamesAsync(VehicleTagRepository tags, string ownerId, Guid vehicleId) =>
        [.. (await tags.ListAsync(ownerId, vehicleId, CancellationToken.None)).Select(tag => tag.Name).Order()];

    [Fact]
    public async Task Insert_CreatesTheVehicleAndLinksEachFeature_ReusingASharedFeatureAndItsSpelling()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var other = TestData.Vehicle("owner-2");
        await vehicles.InsertAsync(other, CancellationToken.None);
        await AddTagAsync(tags, "owner-2", other.Id, "Heated Seats");
        var vehicle = TestData.Vehicle("owner-1");

        var status = await vehicles.InsertWithFeaturesAsync(vehicle, [Feature("heated seats"), Feature("Panoramic Sunroof")], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.Written, status);
        Assert.NotNull(await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None));
        Assert.Equal(["Heated Seats", "Panoramic Sunroof"], await NamesAsync(tags, "owner-1", vehicle.Id));
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
    }

    [Fact]
    public async Task Update_DeletesOnlyTheListedTagsOfThisVehicle_AndAddsTheNewFeatures()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var sunroof = await AddTagAsync(tags, "owner-1", vehicle.Id, "Sunroof");
        await AddTagAsync(tags, "owner-1", vehicle.Id, "Towing Package");

        var status = await vehicles.UpdateWithFeaturesAsync(vehicle with { Color = "Blue" }, [Feature("Panoramic Sunroof", sunroof)], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.Written, status);
        Assert.Equal(["Panoramic Sunroof", "Towing Package"], await NamesAsync(tags, "owner-1", vehicle.Id));
        Assert.Equal("Blue", (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Color);
    }

    [Fact]
    public async Task Update_ATagIdOfAnotherVehicle_IsIgnoredAndThatTagSurvives()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var mine = TestData.Vehicle("owner-1");
        var theirs = TestData.Vehicle("owner-2");
        await vehicles.InsertAsync(mine, CancellationToken.None);
        await vehicles.InsertAsync(theirs, CancellationToken.None);
        var theirTag = await AddTagAsync(tags, "owner-2", theirs.Id, "Sunroof");

        var status = await vehicles.UpdateWithFeaturesAsync(mine, [Feature("Panoramic Sunroof", theirTag)], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.Written, status);
        Assert.Equal(["Sunroof"], await NamesAsync(tags, "owner-2", theirs.Id));
        Assert.Equal(["Panoramic Sunroof"], await NamesAsync(tags, "owner-1", mine.Id));
    }

    [Fact]
    public async Task Update_AFeatureTheVehicleAlreadyHas_IsNotLinkedTwice()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        await AddTagAsync(tags, "owner-1", vehicle.Id, "Heated Seats");

        var status = await vehicles.UpdateWithFeaturesAsync(vehicle, [Feature("HEATED SEATS")], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.Written, status);
        Assert.Equal(["Heated Seats"], await NamesAsync(tags, "owner-1", vehicle.Id));
    }

    [Fact]
    public async Task Update_ReplacingTheSameFeatureWithItself_KeepsOneLink()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var heated = await AddTagAsync(tags, "owner-1", vehicle.Id, "Heated Seats");

        await vehicles.UpdateWithFeaturesAsync(vehicle, [Feature("Heated Seats", heated)], Now, CancellationToken.None);

        Assert.Equal(["Heated Seats"], await NamesAsync(tags, "owner-1", vehicle.Id));
    }

    [Fact]
    public async Task Update_AResultAboveTheLimit_ReturnsFeatureLimitAndRollsBackTheVehicleWriteToo()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1", make: "Ford");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var existing = VehicleTagRepository.MaxTagsPerVehicle - 1;
        for (var i = 1; i <= existing; i++)
        {
            await AddTagAsync(tags, "owner-1", vehicle.Id, $"tag {i}");
        }

        var status = await vehicles.UpdateWithFeaturesAsync(vehicle with { Make = "Changed" }, [Feature("one"), Feature("two")], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.FeatureLimit, status);
        Assert.Equal("Ford", (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Make);
        Assert.Equal(existing, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Update_ReplacingFreesRoomUnderTheLimit()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var first = await AddTagAsync(tags, "owner-1", vehicle.Id, "tag 1");
        for (var i = 2; i <= VehicleTagRepository.MaxTagsPerVehicle; i++)
        {
            await AddTagAsync(tags, "owner-1", vehicle.Id, $"tag {i}");
        }

        var status = await vehicles.UpdateWithFeaturesAsync(vehicle, [Feature("Replacement", first)], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.Written, status);
        Assert.Equal(VehicleTagRepository.MaxTagsPerVehicle, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Insert_MoreThanTheLimitOfFeatures_ReturnsFeatureLimitAndCreatesNoVehicle()
    {
        var (_, vehicles, _) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");

        var status = await vehicles.InsertWithFeaturesAsync(vehicle, [.. Enumerable.Range(1, VehicleTagRepository.MaxTagsPerVehicle + 1).Select(i => Feature($"feature {i}"))], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.FeatureLimit, status);
        Assert.Null(await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Update_AVehicleThatIsNotTheOwners_IsNotFoundAndChangesNothing()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var sunroof = await AddTagAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        var status = await vehicles.UpdateWithFeaturesAsync(
            vehicle with { OwnerId = "owner-2", Make = "Hacked" }, [Feature("Panoramic Sunroof", sunroof)], Now, CancellationToken.None);

        Assert.Equal(VehicleFeatureWriteStatus.NotFound, status);
        Assert.Equal(["Sunroof"], await NamesAsync(tags, "owner-1", vehicle.Id));
        Assert.Equal("Ford", (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Make);
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
    }

    [Fact]
    public async Task EmptyFeatureList_BehavesLikeThePlainWrites()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");

        Assert.Equal(VehicleFeatureWriteStatus.Written, await vehicles.InsertWithFeaturesAsync(vehicle, [], Now, CancellationToken.None));
        Assert.Equal(VehicleFeatureWriteStatus.Written, await vehicles.UpdateWithFeaturesAsync(vehicle with { Make = "Other" }, [], Now, CancellationToken.None));

        Assert.Empty(await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
        Assert.Equal("Other", (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Make);
    }

    [Fact]
    public async Task AFailurePartWay_RollsBackTheVehicleWriteAndTheFeatureWrites()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        await AddTagAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        // A name over the 30-character column limit fails inside the database after the vehicle row was updated.
        await Assert.ThrowsAnyAsync<PostgresException>(() =>
            vehicles.UpdateWithFeaturesAsync(vehicle with { Make = "Changed" }, [Feature(new string('x', 31))], Now, CancellationToken.None));

        Assert.Equal("Ford", (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Make);
        Assert.Equal(["Sunroof"], await NamesAsync(tags, "owner-1", vehicle.Id));
    }
}

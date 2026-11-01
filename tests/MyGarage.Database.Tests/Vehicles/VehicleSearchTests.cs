using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleSearchTests(PostgresFixture postgres)
{
    private const string Owner = "owner-1";

    private sealed record Garage(
        NpgsqlDataSource DataSource,
        VehicleRepository Repository,
        VehicleRecord Blue,
        VehicleRecord Red,
        VehicleRecord Silver,
        VehicleRecord Mystery);

    // A: Blue Streak (Subaru, 42k, 4 doors, tags sunroof + tow package), B: Red Rocket (Ford, 120k, tow package),
    // C: Garage Queen (Porsche, 5k, 2 doors, convertible), D: Mystery (Ford, no color/mileage/doors).
    private async Task<Garage> CreateGarageAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new VehicleRepository(dataSource, new SqlScriptStore());

        var blue = TestData.Vehicle(Owner, "Blue Streak", "Subaru", "Outback", "Limited", "Blue", 42_000, 4);
        var red = TestData.Vehicle(Owner, "Red Rocket", "Ford", "F-150", "XLT", "Red", 120_000, 4);
        var silver = TestData.Vehicle(Owner, "Garage Queen", "Porsche", "911", "Carrera", "Silver", 5_000, 2);
        var mystery = TestData.Vehicle(Owner, "Mystery", "ford", "Mustang");
        foreach (var vehicle in new[] { blue, red, silver, mystery })
        {
            await repository.InsertAsync(vehicle, CancellationToken.None);
        }

        await AddTagAsync(dataSource, blue.Id, "Sunroof");
        await AddTagAsync(dataSource, blue.Id, "Tow package");
        await AddTagAsync(dataSource, red.Id, "tow package");
        await AddTagAsync(dataSource, silver.Id, "Convertible");

        return new Garage(dataSource, repository, blue, red, silver, mystery);
    }

    private static async Task AddTagAsync(NpgsqlDataSource dataSource, Guid vehicleId, string name)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, @name, now())",
            new { vehicleId, name });
    }

    private static async Task<List<string>> NicknamesAsync(Garage garage, VehicleSearchCriteria criteria, string ownerId = Owner)
    {
        var page = await garage.Repository.ListPageAsync(ownerId, criteria, 1, 50, CancellationToken.None);
        return page.Items.Select(item => item.Nickname).ToList();
    }

    [Theory]
    [InlineData("streak", new[] { "Blue Streak" })]
    [InlineData("OUTBACK", new[] { "Blue Streak" })]
    [InlineData("xlt", new[] { "Red Rocket" })]
    [InlineData("carrera", new[] { "Garage Queen" })]
    [InlineData("silver", new[] { "Garage Queen" })]
    [InlineData("ford", new[] { "Mystery", "Red Rocket" })]
    [InlineData("convert", new[] { "Garage Queen" })]
    [InlineData("TOW", new[] { "Blue Streak", "Red Rocket" })]
    [InlineData("  streak  ", new[] { "Blue Streak" })]
    [InlineData("nothing matches this", new string[0])]
    public async Task Query_MatchesTextFieldsAndTagsIgnoringCase(string query, string[] expected)
    {
        var garage = await CreateGarageAsync();

        var result = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = query });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("%", "100% Legit")]
    [InlineData("_", "snake_case")]
    [InlineData("\\", "back\\slash")]
    public async Task Query_MetacharactersAreMatchedLiterally(string query, string expected)
    {
        var garage = await CreateGarageAsync();
        foreach (var nickname in new[] { "100% Legit", "snake_case", "back\\slash" })
        {
            await garage.Repository.InsertAsync(TestData.Vehicle(Owner, nickname), CancellationToken.None);
        }

        var result = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = query });

        Assert.Equal([expected], result);
    }

    [Fact]
    public async Task WhitespaceOnlyQuery_IsTreatedAsNoSearch()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(4, (await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "   " })).Count);
    }

    [Fact]
    public async Task MakeAndColorFilters_AreCaseInsensitiveExactMatches()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["Mystery", "Red Rocket"], await NicknamesAsync(garage, new VehicleSearchCriteria { Make = "FORD" }));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { Make = "For" }));
        Assert.Equal(["Red Rocket"], await NicknamesAsync(garage, new VehicleSearchCriteria { Color = "red" }));
    }

    [Fact]
    public async Task DoorsFilter_MatchesOnlyThatCount()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["Garage Queen"], await NicknamesAsync(garage, new VehicleSearchCriteria { Doors = 2 }));
        Assert.Equal(["Blue Streak", "Red Rocket"], await NicknamesAsync(garage, new VehicleSearchCriteria { Doors = 4 }));
    }

    [Fact]
    public async Task TagFilter_RequiresThatTagIgnoringCase()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["Blue Streak", "Red Rocket"], await NicknamesAsync(garage, new VehicleSearchCriteria { Tag = "TOW PACKAGE" }));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { Tag = "tow" }));
    }

    [Fact]
    public async Task MileageFilters_AreInclusiveAndNeverMatchVehiclesWithoutMileage()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["Blue Streak", "Red Rocket"], await NicknamesAsync(garage, new VehicleSearchCriteria { MinMileage = 42_000 }));
        Assert.Equal(["Blue Streak", "Garage Queen"], await NicknamesAsync(garage, new VehicleSearchCriteria { MaxMileage = 42_000 }));
        Assert.Equal(["Blue Streak"], await NicknamesAsync(garage, new VehicleSearchCriteria { MinMileage = 10_000, MaxMileage = 50_000 }));
    }

    [Fact]
    public async Task Criteria_AreCombinedWithAnd()
    {
        var garage = await CreateGarageAsync();

        var result = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "ford", Tag = "tow package", MinMileage = 100_000 });

        Assert.Equal(["Red Rocket"], result);
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "ford", Doors = 2 }));
    }

    [Fact]
    public async Task Paging_UsesTheFilteredSetAndTotal()
    {
        var garage = await CreateGarageAsync();
        for (var i = 1; i <= 25; i++)
        {
            await garage.Repository.InsertAsync(TestData.Vehicle(Owner, $"Match {i:D2}", "Tesla", "3"), CancellationToken.None);
        }

        var criteria = new VehicleSearchCriteria { Make = "tesla" };
        var first = await garage.Repository.ListPageAsync(Owner, criteria, 1, 12, CancellationToken.None);
        var third = await garage.Repository.ListPageAsync(Owner, criteria, 3, 12, CancellationToken.None);

        Assert.Equal(25, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(12, first.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal("Match 25", third.Items[0].Nickname);
    }

    [Fact]
    public async Task OtherOwnersVehicles_AreNeverReturned()
    {
        var garage = await CreateGarageAsync();
        await garage.Repository.InsertAsync(TestData.Vehicle("owner-2", "Blue Streak Clone", "Subaru", "Outback"), CancellationToken.None);

        var mine = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "subaru" });
        var theirs = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "subaru" }, "owner-2");

        Assert.Equal(["Blue Streak"], mine);
        Assert.Equal(["Blue Streak Clone"], theirs);
    }

    [Fact]
    public async Task FilterOptions_AreDistinctSortedAndOnlyFromTheCallersGarage()
    {
        var garage = await CreateGarageAsync();
        await AddTagAsync(garage.DataSource, garage.Silver.Id, "SUNROOF");
        var other = TestData.Vehicle("owner-2", "Other", "Zeta", "Z", color: "Violet");
        await garage.Repository.InsertAsync(other, CancellationToken.None);
        await AddTagAsync(garage.DataSource, other.Id, "secret");

        var options = await garage.Repository.GetFilterOptionsAsync(Owner, CancellationToken.None);

        Assert.Equal(["ford", "porsche", "subaru"], options.Makes.Select(make => make.ToLowerInvariant()));
        Assert.Equal(["Blue", "Red", "Silver"], options.Colors);
        Assert.Equal(["convertible", "sunroof", "tow package"], options.Tags.Select(tag => tag.ToLowerInvariant()));
        Assert.DoesNotContain("Zeta", options.Makes);
        Assert.DoesNotContain("secret", options.Tags);
    }

    [Fact]
    public async Task FilterOptions_EmptyGarage_ReturnsEmptyLists()
    {
        var repository = new VehicleRepository(await postgres.CreateMigratedDatabaseAsync(), new SqlScriptStore());

        var options = await repository.GetFilterOptionsAsync(Owner, CancellationToken.None);

        Assert.Empty(options.Makes);
        Assert.Empty(options.Colors);
        Assert.Empty(options.Tags);
    }
}

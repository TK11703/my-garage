using MyGarage.Database.Common;
using MyGarage.Database.Tags;
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

    // A: Outback (Subaru, 42k, 4 doors, features sunroof + tow package), B: F-150 (Ford, 120k, tow package),
    // C: 911 (Porsche, 5k, 2 doors, convertible), D: Mustang (ford, no color/mileage/doors).
    private async Task<Garage> CreateGarageAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new VehicleRepository(dataSource, new SqlScriptStore());

        var blue = TestData.Vehicle(Owner, "Outback", "Subaru", "Limited", "Blue", 42_000, 4);
        var red = TestData.Vehicle(Owner, "F-150", "Ford", "XLT", "Red", 120_000, 4);
        var silver = TestData.Vehicle(Owner, "911", "Porsche", "Carrera", "Silver", 5_000, 2);
        var mystery = TestData.Vehicle(Owner, "Mustang", "ford");
        foreach (var vehicle in new[] { blue, red, silver, mystery })
        {
            await repository.InsertAsync(vehicle, CancellationToken.None);
        }

        await TestData.LinkFeatureAsync(dataSource, blue.Id, "Sunroof");
        await TestData.LinkFeatureAsync(dataSource, blue.Id, "Tow package");
        await TestData.LinkFeatureAsync(dataSource, red.Id, "tow package");
        await TestData.LinkFeatureAsync(dataSource, silver.Id, "Convertible");

        return new Garage(dataSource, repository, blue, red, silver, mystery);
    }

    private static async Task<List<string>> ModelsAsync(Garage garage, VehicleSearchCriteria criteria, string ownerId = Owner)
    {
        var page = await garage.Repository.ListPageAsync(ownerId, criteria, 1, 50, CancellationToken.None);
        return page.Items.Select(item => item.Model).ToList();
    }

    [Theory]
    [InlineData("limited", new[] { "Outback" })]
    [InlineData("OUTBACK", new[] { "Outback" })]
    [InlineData("xlt", new[] { "F-150" })]
    [InlineData("carrera", new[] { "911" })]
    [InlineData("silver", new[] { "911" })]
    [InlineData("ford", new[] { "F-150", "Mustang" })]
    [InlineData("convert", new[] { "911" })]
    [InlineData("TOW", new[] { "F-150", "Outback" })]
    [InlineData("  outback  ", new[] { "Outback" })]
    [InlineData("nothing matches this", new string[0])]
    public async Task Query_MatchesTextFieldsAndFeaturesIgnoringCase(string query, string[] expected)
    {
        var garage = await CreateGarageAsync();

        var result = await ModelsAsync(garage, new VehicleSearchCriteria { Query = query });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("%", "100% Legit")]
    [InlineData("_", "snake_case")]
    [InlineData("\\", "back\\slash")]
    public async Task Query_MetacharactersAreMatchedLiterally(string query, string expected)
    {
        var garage = await CreateGarageAsync();
        foreach (var model in new[] { "100% Legit", "snake_case", "back\\slash" })
        {
            await garage.Repository.InsertAsync(TestData.Vehicle(Owner, model), CancellationToken.None);
        }

        var result = await ModelsAsync(garage, new VehicleSearchCriteria { Query = query });

        Assert.Equal([expected], result);
    }

    [Fact]
    public async Task WhitespaceOnlyQuery_IsTreatedAsNoSearch()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(4, (await ModelsAsync(garage, new VehicleSearchCriteria { Query = "   " })).Count);
    }

    [Fact]
    public async Task MakeAndColorFilters_AreCaseInsensitiveExactMatches()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["F-150", "Mustang"], await ModelsAsync(garage, new VehicleSearchCriteria { Make = "FORD" }));
        Assert.Empty(await ModelsAsync(garage, new VehicleSearchCriteria { Make = "For" }));
        Assert.Equal(["F-150"], await ModelsAsync(garage, new VehicleSearchCriteria { Color = "red" }));
    }

    [Fact]
    public async Task DoorsFilter_MatchesOnlyThatCount()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["911"], await ModelsAsync(garage, new VehicleSearchCriteria { Doors = 2 }));
        Assert.Equal(["F-150", "Outback"], await ModelsAsync(garage, new VehicleSearchCriteria { Doors = 4 }));
    }

    [Fact]
    public async Task TagFilter_RequiresThatTagIgnoringCase()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["F-150", "Outback"], await ModelsAsync(garage, new VehicleSearchCriteria { Tag = "TOW PACKAGE" }));
        Assert.Empty(await ModelsAsync(garage, new VehicleSearchCriteria { Tag = "tow" }));
    }

    [Fact]
    public async Task MileageFilters_AreInclusiveAndNeverMatchVehiclesWithoutMileage()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["F-150", "Outback"], await ModelsAsync(garage, new VehicleSearchCriteria { MinMileage = 42_000 }));
        Assert.Equal(["911", "Outback"], await ModelsAsync(garage, new VehicleSearchCriteria { MaxMileage = 42_000 }));
        Assert.Equal(["Outback"], await ModelsAsync(garage, new VehicleSearchCriteria { MinMileage = 10_000, MaxMileage = 50_000 }));
    }

    [Fact]
    public async Task Criteria_AreCombinedWithAnd()
    {
        var garage = await CreateGarageAsync();

        var result = await ModelsAsync(garage, new VehicleSearchCriteria { Query = "ford", Tag = "tow package", MinMileage = 100_000 });

        Assert.Equal(["F-150"], result);
        Assert.Empty(await ModelsAsync(garage, new VehicleSearchCriteria { Query = "ford", Doors = 2 }));
    }

    [Fact]
    public async Task Paging_UsesTheFilteredSetAndTotal()
    {
        var garage = await CreateGarageAsync();
        for (var i = 1; i <= 25; i++)
        {
            await garage.Repository.InsertAsync(TestData.Vehicle(Owner, $"Match {i:D2}", "Tesla"), CancellationToken.None);
        }

        var criteria = new VehicleSearchCriteria { Make = "tesla" };
        var first = await garage.Repository.ListPageAsync(Owner, criteria, 1, 12, CancellationToken.None);
        var third = await garage.Repository.ListPageAsync(Owner, criteria, 3, 12, CancellationToken.None);

        Assert.Equal(25, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(12, first.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal("Match 25", third.Items[0].Model);
    }

    [Fact]
    public async Task OtherOwnersVehicles_AreNeverReturned()
    {
        var garage = await CreateGarageAsync();
        await garage.Repository.InsertAsync(TestData.Vehicle("owner-2", "Forester", "Subaru"), CancellationToken.None);

        var mine = await ModelsAsync(garage, new VehicleSearchCriteria { Query = "subaru" });
        var theirs = await ModelsAsync(garage, new VehicleSearchCriteria { Query = "subaru" }, "owner-2");

        Assert.Equal(["Outback"], mine);
        Assert.Equal(["Forester"], theirs);
    }

    [Fact]
    public async Task FilterOptions_AreDistinctSortedAndOnlyFromTheCallersGarage()
    {
        var garage = await CreateGarageAsync();
        await TestData.LinkFeatureAsync(garage.DataSource, garage.Silver.Id, "SUNROOF");
        var other = TestData.Vehicle("owner-2", "Z", "Zeta", color: "Violet");
        await garage.Repository.InsertAsync(other, CancellationToken.None);
        await TestData.LinkFeatureAsync(garage.DataSource, other.Id, "secret");

        var options = await garage.Repository.GetFilterOptionsAsync(Owner, CancellationToken.None);

        Assert.Equal(["ford", "porsche", "subaru"], options.Makes.Select(make => make.ToLowerInvariant()));
        Assert.Equal(["Blue", "Red", "Silver"], options.Colors);
        Assert.Equal(["convertible", "sunroof", "tow package"], options.Tags.Select(tag => tag.ToLowerInvariant()));
        Assert.DoesNotContain("Zeta", options.Makes);
        Assert.DoesNotContain("secret", options.Tags);
    }

    [Fact]
    public async Task Query_MatchesYearAndMakeTogether()
    {
        var garage = await CreateGarageAsync();
        await garage.Repository.InsertAsync(TestData.Vehicle(Owner, "Grand Cherokee", "Jeep") with { Year = 2012 }, CancellationToken.None);
        await garage.Repository.InsertAsync(TestData.Vehicle(Owner, "Wrangler", "Jeep") with { Year = 2015 }, CancellationToken.None);
        await garage.Repository.InsertAsync(TestData.Vehicle(Owner, "Fusion", "Ford") with { Year = 2012 }, CancellationToken.None);

        Assert.Equal(["Grand Cherokee"], await ModelsAsync(garage, new VehicleSearchCriteria { Query = "2012 jeep" }));
        Assert.Equal(["Grand Cherokee"], await ModelsAsync(garage, new VehicleSearchCriteria { Query = "2012 Jeep Grand" }));
        Assert.Equal(["Grand Cherokee", "Wrangler"], await ModelsAsync(garage, new VehicleSearchCriteria { Query = "jeep" }));
        Assert.Empty(await ModelsAsync(garage, new VehicleSearchCriteria { Query = "2015 ford" }));
    }

    [Fact]
    public async Task Query_MatchesAFeatureNameFromTheSharedCatalog_ForEachOwnersOwnVehiclesOnly()
    {
        var garage = await CreateGarageAsync();
        var tags = new VehicleTagRepository(garage.DataSource, new SqlScriptStore());
        var theirs = TestData.Vehicle("owner-2", "Theirs", "Honda");
        await garage.Repository.InsertAsync(theirs, CancellationToken.None);
        await tags.AddAsync(Owner, garage.Mystery.Id, Guid.CreateVersion7(), "Panoramic roof", TestData.Created, CancellationToken.None);
        await tags.AddAsync("owner-2", theirs.Id, Guid.CreateVersion7(), "PANORAMIC ROOF", TestData.Created, CancellationToken.None);

        Assert.Equal(["Mustang"], await ModelsAsync(garage, new VehicleSearchCriteria { Query = "panoramic" }));
        Assert.Equal(["Theirs"], await ModelsAsync(garage, new VehicleSearchCriteria { Query = "ROOF" }, "owner-2"));
        Assert.Equal(["Mustang"], await ModelsAsync(garage, new VehicleSearchCriteria { Tag = "panoramic roof" }));
    }

    [Fact]
    public async Task ListOrder_IsMakeThenModelThenYearWithMissingYearsLast()
    {
        var repository = new VehicleRepository(await postgres.CreateMigratedDatabaseAsync(), new SqlScriptStore());
        var vehicles = new[]
        {
            TestData.Vehicle(Owner, "F-150", "Ford") with { Year = null },
            TestData.Vehicle(Owner, "F-150", "Ford") with { Year = 2015 },
            TestData.Vehicle(Owner, "Bronco", "ford") with { Year = 2020 },
            TestData.Vehicle(Owner, "f-150", "FORD") with { Year = 2010 },
            TestData.Vehicle(Owner, "A4", "Audi") with { Year = 2018 },
        };
        foreach (var vehicle in vehicles)
        {
            await repository.InsertAsync(vehicle, CancellationToken.None);
        }

        var page = await repository.ListPageAsync(Owner, VehicleSearchCriteria.None, 1, 50, CancellationToken.None);

        Assert.Equal(
            [("A4", (int?)2018), ("Bronco", 2020), ("f-150", 2010), ("F-150", 2015), ("F-150", null)],
            page.Items.Select(item => (item.Model, item.Year)));
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

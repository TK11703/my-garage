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
    public async Task Add_OneTagOverTheLimit_ReachesTheLimitAndStoresNothing()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var i = 1; i <= VehicleTagRepository.MaxTagsPerVehicle; i++)
        {
            Assert.Equal(AddTagStatus.Added, (await AddAsync(tags, "owner-1", vehicle.Id, $"tag {i}")).Status);
        }

        var result = await AddAsync(tags, "owner-1", vehicle.Id, "one too many");

        Assert.Equal(AddTagStatus.LimitReached, result.Status);
        Assert.Equal(VehicleTagRepository.MaxTagsPerVehicle, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Add_AtTheLimit_ADuplicateIsReportedAsADuplicate()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var i = 1; i <= VehicleTagRepository.MaxTagsPerVehicle; i++)
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

        var limit = VehicleTagRepository.MaxTagsPerVehicle;

        var results = await Task.WhenAll(Enumerable.Range(1, limit + 10).Select(i => AddAsync(tags, "owner-1", vehicle.Id, $"tag {i}")));

        Assert.Equal(limit, results.Count(result => result.Status == AddTagStatus.Added));
        Assert.Equal(10, results.Count(result => result.Status == AddTagStatus.LimitReached));
        Assert.Equal(limit, (await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
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

    [Fact]
    public async Task Add_SameNameDifferentCaseForTwoOwners_ReusesOneSharedFeature()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var first = await InsertVehicleAsync(vehicles, "owner-1");
        var second = await InsertVehicleAsync(vehicles, "owner-2");

        var created = await AddAsync(tags, "owner-1", first.Id, "Sunroof");
        var reused = await AddAsync(tags, "owner-2", second.Id, "SUNROOF");

        Assert.Equal(AddTagStatus.Added, created.Status);
        Assert.Equal(AddTagStatus.Added, reused.Status);
        Assert.Equal("Sunroof", created.Tag!.Name);
        Assert.Equal("Sunroof", reused.Tag!.Name);
        Assert.Equal(["Sunroof"], (await tags.ListAsync("owner-1", first.Id, CancellationToken.None)).Select(tag => tag.Name));
        Assert.Equal(["Sunroof"], (await tags.ListAsync("owner-2", second.Id, CancellationToken.None)).Select(tag => tag.Name));
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
        Assert.Equal("owner-1", await connection.ExecuteScalarAsync<string>("SELECT created_by FROM features"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
    }

    [Fact]
    public async Task Add_FeatureAlreadyOnTheVehicle_CreatesNothingNew()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        Assert.Equal(AddTagStatus.Duplicate, (await AddAsync(tags, "owner-1", vehicle.Id, "sUnRoOf")).Status);

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
    }

    [Fact]
    public async Task Add_AnotherOwnersVehicle_DoesNotCreateAFeature()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        await AddAsync(tags, "owner-2", vehicle.Id, "Sunroof");

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
    }

    [Fact]
    public async Task DeletingAVehicle_RemovesItsLinksButKeepsTheSharedFeature()
    {
        var (dataSource, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        Assert.True(await vehicles.DeleteAsync("owner-1", vehicle.Id, CancellationToken.None));

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(["Sunroof"], await connection.QueryAsync<string>("SELECT name FROM features"));
    }

    private async Task<(VehicleTagRepository Tags, VehicleRecord First, VehicleRecord Second, VehicleRecord Third)> CreateSuggestionGarageAsync()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var first = await InsertVehicleAsync(vehicles, "owner-1");
        var second = await InsertVehicleAsync(vehicles, "owner-2");
        var third = await InsertVehicleAsync(vehicles, "owner-1");
        foreach (var (owner, vehicle, name) in new[]
        {
            ("owner-1", first, "Heated seats"),
            ("owner-2", second, "Heated seats"),
            ("owner-1", third, "Heated seats"),
            ("owner-1", first, "Heated steering wheel"),
            ("owner-2", second, "Heated steering wheel"),
            ("owner-1", third, "Heated mirrors"),
            ("owner-1", first, "Apple CarPlay"),
            ("owner-1", first, "Android Auto"),
        })
        {
            Assert.Equal(AddTagStatus.Added, (await AddAsync(tags, owner, vehicle.Id, name)).Status);
        }

        return (tags, first, second, third);
    }

    [Fact]
    public async Task Suggest_MatchesContainedTextIgnoringCase_MostUsedFirstThenByName()
    {
        var (tags, _, _, _) = await CreateSuggestionGarageAsync();

        Assert.Equal(["Heated seats", "Heated steering wheel", "Heated mirrors"], await tags.SuggestAsync("heated", 10, CancellationToken.None));
        Assert.Equal(["Heated seats", "Heated steering wheel", "Heated mirrors"], await tags.SuggestAsync("HEATED", 10, CancellationToken.None));
        Assert.Equal(["Heated steering wheel"], await tags.SuggestAsync("ing WHE", 10, CancellationToken.None));
        Assert.Equal(
            ["Heated seats", "Heated steering wheel", "Android Auto", "Apple CarPlay", "Heated mirrors"],
            await tags.SuggestAsync("a", 10, CancellationToken.None));
        Assert.Empty(await tags.SuggestAsync("nothing like this", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Suggest_RespectsTheLimit()
    {
        var (tags, _, _, _) = await CreateSuggestionGarageAsync();

        Assert.Equal(["Heated seats", "Heated steering wheel"], await tags.SuggestAsync("heated", 2, CancellationToken.None));
        Assert.Equal(["Heated seats"], await tags.SuggestAsync("heated", 1, CancellationToken.None));
    }

    [Theory]
    [InlineData("%", "100% cotton")]
    [InlineData("_", "a_b")]
    [InlineData("\\", "a\\b")]
    public async Task Suggest_TreatsLikeWildcardsLiterally(string text, string expected)
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        foreach (var name in new[] { "100% cotton", "a_b", "a\\b", "axb", "ab" })
        {
            await AddAsync(tags, "owner-1", vehicle.Id, name);
        }

        Assert.Equal([expected], await tags.SuggestAsync(text, 10, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Suggest_BlankText_ReturnsNothing(string? text)
    {
        var (tags, _, _, _) = await CreateSuggestionGarageAsync();

        Assert.Empty(await tags.SuggestAsync(text, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Suggest_OmitsFeaturesNoVehicleUsesAnymore()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var kept = await InsertVehicleAsync(vehicles, "owner-1");
        var doomed = await InsertVehicleAsync(vehicles, "owner-2");
        var removedTag = (await AddAsync(tags, "owner-1", kept.Id, "Removed feature")).Tag!;
        await AddAsync(tags, "owner-1", kept.Id, "Kept feature");
        await AddAsync(tags, "owner-2", doomed.Id, "Orphaned by delete");
        await AddAsync(tags, "owner-2", doomed.Id, "Kept feature");

        Assert.True(await tags.RemoveAsync("owner-1", kept.Id, removedTag.Id, CancellationToken.None));
        Assert.True(await vehicles.DeleteAsync("owner-2", doomed.Id, CancellationToken.None));

        Assert.Empty(await tags.SuggestAsync("removed", 10, CancellationToken.None));
        Assert.Empty(await tags.SuggestAsync("orphaned", 10, CancellationToken.None));
        Assert.Equal(["Kept feature"], await tags.SuggestAsync("feature", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Suggest_CountsUsageAcrossAllOwnersAndKeepsTheCanonicalCasing()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var one = await InsertVehicleAsync(vehicles, "owner-1");
        var two = await InsertVehicleAsync(vehicles, "owner-2");
        await AddAsync(tags, "owner-1", one.Id, "Roof rack");
        await AddAsync(tags, "owner-1", one.Id, "Alloy wheels");
        await AddAsync(tags, "owner-2", two.Id, "ROOF RACK");

        Assert.Equal(["Roof rack", "Alloy wheels"], await tags.SuggestAsync("o", 10, CancellationToken.None));
    }

    [Fact]
    public async Task ANewFeature_HasNoCategoryUntilOneIsSet()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var added = await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");

        Assert.Null(added.Tag!.Category);
        Assert.Null(Assert.Single(await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Category);
        var pending = Assert.Single(await tags.ListUncategorizedAsync(10, CancellationToken.None));
        Assert.Equal("Sunroof", pending.Name);
    }

    [Fact]
    public async Task SettingACategory_ShowsItOnEveryVehicleThatHasTheFeature_AndRemovesItFromThePendingList()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var one = await InsertVehicleAsync(vehicles, "owner-1");
        var two = await InsertVehicleAsync(vehicles, "owner-2");
        await AddAsync(tags, "owner-1", one.Id, "Sunroof");
        await AddAsync(tags, "owner-2", two.Id, "sunroof");
        var pending = Assert.Single(await tags.ListUncategorizedAsync(10, CancellationToken.None));

        await tags.SetCategoriesAsync(new Dictionary<Guid, string> { [pending.Id] = "Exterior" }, CancellationToken.None);

        Assert.Equal("Exterior", Assert.Single(await tags.ListAsync("owner-1", one.Id, CancellationToken.None)).Category);
        Assert.Equal("Exterior", Assert.Single(await tags.ListAsync("owner-2", two.Id, CancellationToken.None)).Category);
        Assert.Empty(await tags.ListUncategorizedAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task AddingAFeatureThatIsAlreadyCategorized_ReturnsItsCategory()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var one = await InsertVehicleAsync(vehicles, "owner-1");
        var two = await InsertVehicleAsync(vehicles, "owner-2");
        await AddAsync(tags, "owner-1", one.Id, "Sunroof");
        var pending = Assert.Single(await tags.ListUncategorizedAsync(10, CancellationToken.None));
        await tags.SetCategoriesAsync(new Dictionary<Guid, string> { [pending.Id] = "Exterior" }, CancellationToken.None);

        var result = await AddAsync(tags, "owner-2", two.Id, "SUNROOF");

        Assert.Equal("Exterior", result.Tag!.Category);
    }

    [Fact]
    public async Task ACategoryIsSetOnce_ALaterWriteDoesNotOverwriteIt()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(tags, "owner-1", vehicle.Id, "Sunroof");
        var pending = Assert.Single(await tags.ListUncategorizedAsync(10, CancellationToken.None));
        await tags.SetCategoriesAsync(new Dictionary<Guid, string> { [pending.Id] = "Exterior" }, CancellationToken.None);

        await tags.SetCategoriesAsync(new Dictionary<Guid, string> { [pending.Id] = "Safety" }, CancellationToken.None);

        Assert.Equal("Exterior", Assert.Single(await tags.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Category);
    }

    [Fact]
    public async Task ListUncategorized_ReturnsTheOldestFirstUpToTheLimit()
    {
        var (_, vehicles, tags) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await tags.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), "Second", TestData.Created.AddMinutes(1), CancellationToken.None);
        await tags.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), "First", TestData.Created, CancellationToken.None);
        await tags.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), "Third", TestData.Created.AddMinutes(2), CancellationToken.None);

        var pending = await tags.ListUncategorizedAsync(2, CancellationToken.None);

        Assert.Equal(["First", "Second"], pending.Select(feature => feature.Name));
    }

    [Fact]
    public async Task SettingNoCategories_ChangesNothing()
    {
        var (_, _, tags) = await CreateAsync();

        await tags.SetCategoriesAsync(new Dictionary<Guid, string>(), CancellationToken.None);

        Assert.Empty(await tags.ListUncategorizedAsync(10, CancellationToken.None));
    }
}

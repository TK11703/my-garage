using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.JournalNotes;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.JournalNotes;

[Collection(DatabaseCollection.Name)]
public class JournalNoteRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, JournalNoteRepository Notes)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new JournalNoteRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<JournalNoteRecord?> AddAsync(
        JournalNoteRepository notes, string ownerId, Guid vehicleId, string body, string? category = null, DateTimeOffset? at = null) =>
        notes.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), category, body, at ?? T0, CancellationToken.None);

    [Fact]
    public async Task Add_StoresTheNoteWithItsFreeTextCategoryAndAudit()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var note = await AddAsync(notes, "owner-1", vehicle.Id, "Changed the oil", "Oil Change (5W-30)");

        Assert.NotNull(note);
        Assert.Equal("Oil Change (5W-30)", note.Category);
        Assert.Equal("Changed the oil", note.Body);
        Assert.Equal(T0, note.CreatedAt);
        Assert.Equal(T0, note.UpdatedAt);
        Assert.Equal("owner-1", note.CreatedBy);
        Assert.Equal("owner-1", note.UpdatedBy);
    }

    [Fact]
    public async Task Add_WithoutCategory_StoresNull()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var note = await AddAsync(notes, "owner-1", vehicle.Id, "No category");

        Assert.NotNull(note);
        Assert.Null(note.Category);
    }

    [Fact]
    public async Task Add_KeepsCategoryCaseAsEntered()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var note = await AddAsync(notes, "owner-1", vehicle.Id, "x", "Road TRIP");

        Assert.Equal("Road TRIP", note!.Category);
    }

    [Fact]
    public async Task Add_AnotherOwnersVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var note = await AddAsync(notes, "owner-2", vehicle.Id, "sneaky");

        Assert.Null(note);
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM journal_notes"));
    }

    [Fact]
    public async Task ListPage_IsNewestFirstWithPagingAndTotals()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var i = 1; i <= 25; i++)
        {
            await AddAsync(notes, "owner-1", vehicle.Id, $"note {i:D2}", at: T0.AddMinutes(i));
        }

        var first = await notes.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        var third = await notes.ListPageAsync("owner-1", vehicle.Id, 3, 10, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal("note 25", first.Items[0].Body);
        Assert.Equal("note 16", first.Items[9].Body);
        Assert.NotNull(third);
        Assert.Equal(["note 05", "note 04", "note 03", "note 02", "note 01"], third.Items.Select(note => note.Body));
    }

    [Fact]
    public async Task ListPage_EqualTimes_AreOrderedByIdDescendingForStability()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add((await AddAsync(notes, "owner-1", vehicle.Id, $"n{i}"))!.Id);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 2; page++)
        {
            seen.AddRange((await notes.ListPageAsync("owner-1", vehicle.Id, page, 2, CancellationToken.None))!.Items.Select(note => note.Id));
        }

        Assert.Equal(ids.OrderDescending(), seen);
    }

    [Fact]
    public async Task ListPage_BeyondTheLastPage_IsEmptyWithTheRealTotal()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(notes, "owner-1", vehicle.Id, "only");

        var page = await notes.ListPageAsync("owner-1", vehicle.Id, 9, 10, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListPage_AnotherOwnersOrUnknownVehicle_ReturnsNull()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(notes, "owner-1", vehicle.Id, "mine");

        Assert.Null(await notes.ListPageAsync("owner-2", vehicle.Id, 1, 10, CancellationToken.None));
        Assert.Null(await notes.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None));
    }

    [Fact]
    public async Task ListPage_NeverMixesNotesOfDifferentVehicles()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var first = await InsertVehicleAsync(vehicles, "owner-1");
        var second = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(notes, "owner-1", first.Id, "for first");
        await AddAsync(notes, "owner-1", second.Id, "for second");

        var page = await notes.ListPageAsync("owner-1", first.Id, 1, 10, CancellationToken.None);

        Assert.Equal(["for first"], page!.Items.Select(note => note.Body));
    }

    [Fact]
    public async Task Update_ChangesBodyAndCategoryAndStampsTheUpdateOnly()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var original = (await AddAsync(notes, "owner-1", vehicle.Id, "before", "Old"))!;

        var updated = await notes.UpdateAsync("owner-1", vehicle.Id, original.Id, "New", "after", T0.AddHours(1), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("after", updated.Body);
        Assert.Equal("New", updated.Category);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal(T0.AddHours(1), updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
    }

    [Fact]
    public async Task Update_CanClearTheCategory()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var original = (await AddAsync(notes, "owner-1", vehicle.Id, "x", "Old"))!;

        var updated = await notes.UpdateAsync("owner-1", vehicle.Id, original.Id, null, "x", T0, CancellationToken.None);

        Assert.Null(updated!.Category);
    }

    [Fact]
    public async Task Update_AnotherOwnersNoteOrWrongVehicle_ReturnsNullAndChangesNothing()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var other = await InsertVehicleAsync(vehicles, "owner-1");
        var note = (await AddAsync(notes, "owner-1", vehicle.Id, "keep"))!;

        Assert.Null(await notes.UpdateAsync("owner-2", vehicle.Id, note.Id, null, "hijack", T0, CancellationToken.None));
        Assert.Null(await notes.UpdateAsync("owner-1", other.Id, note.Id, null, "wrong vehicle", T0, CancellationToken.None));
        Assert.Equal("keep", (await notes.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None))!.Items.Single().Body);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatNote()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = (await AddAsync(notes, "owner-1", vehicle.Id, "keep"))!;
        var drop = (await AddAsync(notes, "owner-1", vehicle.Id, "drop"))!;

        Assert.True(await notes.DeleteAsync("owner-1", vehicle.Id, drop.Id, CancellationToken.None));

        Assert.Equal([keep.Id], (await notes.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None))!.Items.Select(note => note.Id));
    }

    [Fact]
    public async Task Delete_AnotherOwnersNoteOrUnknownNote_ReturnsFalse()
    {
        var (_, vehicles, notes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var note = (await AddAsync(notes, "owner-1", vehicle.Id, "mine"))!;

        Assert.False(await notes.DeleteAsync("owner-2", vehicle.Id, note.Id, CancellationToken.None));
        Assert.False(await notes.DeleteAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Single((await notes.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None))!.Items);
    }
}

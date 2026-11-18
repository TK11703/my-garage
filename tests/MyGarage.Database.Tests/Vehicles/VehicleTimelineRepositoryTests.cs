using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleTimelineRepositoryTests(PostgresFixture postgres)
{
    private const string Owner = "owner-a";
    private static readonly DateTimeOffset Created = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Repository)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new VehicleRepository(dataSource, new SqlScriptStore()));
    }

    private static async Task<VehicleRecord> AddVehicleAsync(VehicleRepository repository, string owner)
    {
        var vehicle = TestData.Vehicle(owner, "F-150");
        await repository.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static async Task InsertNoteAsync(NpgsqlDataSource dataSource, Guid vehicleId, string? category, string body, DateTimeOffset at)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO journal_notes (id, vehicle_id, category, body, created_at, updated_at, created_by, updated_by) VALUES (@id, @vehicleId, @category, @body, @at, @at, 'seed', 'seed')",
            new { id = Guid.CreateVersion7(), vehicleId, category, body, at });
    }

    private static async Task<IReadOnlyList<VehicleTimelineRecord>> AllAsync(VehicleRepository repository, Guid vehicleId, string? kind = null) =>
        (await repository.GetTimelinePageAsync(Owner, vehicleId, kind, 1, 50, CancellationToken.None))!.Items;

    [Fact]
    public async Task Timeline_MergesEveryKindNewestFirst()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicle.Id, new DateOnly(2026, 3, 1), odometer: 45_000, shop: "Corner Garage");
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, "Oil change", 80m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, "fuel", new DateOnly(2026, 4, 1), totalCost: 45m);
        await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, "safety", new DateOnly(2026, 5, 1), cost: 25m);
        await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, createdAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, startDate: new DateOnly(2026, 1, 15), endDate: new DateOnly(2027, 1, 15));
        await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
        await InsertNoteAsync(dataSource, vehicle.Id, "Trip", "Drove to the coast.", new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero));

        var items = await AllAsync(repository, vehicle.Id);

        Assert.Equal(["journal", "inspection", "fuel-energy", "service", "registration", "insurance", "tax"], items.Select(item => item.Kind));
        Assert.Equal(items.OrderByDescending(item => item.OccurredOn).Select(item => item.OccurredOn), items.Select(item => item.OccurredOn));
    }

    [Fact]
    public async Task Timeline_CarriesTheHeadlineAmountAndOdometerOfEachEntry()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicle.Id, new DateOnly(2026, 3, 1), odometer: 45_000, shop: "Corner Garage");
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, "Oil change", 80m);
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, "Rotation", 20m);

        var service = Assert.Single(await AllAsync(repository, vehicle.Id, "service"));

        Assert.Equal(record, service.Id);
        Assert.Equal(new DateOnly(2026, 3, 1), service.OccurredOn);
        Assert.Equal("Oil change, Rotation", service.Title);
        Assert.Equal("Corner Garage", service.Detail);
        Assert.Equal(100m, service.Amount);
        Assert.Equal(45_000, service.OdometerReading);
        Assert.False(service.FromEmail);
    }

    [Fact]
    public async Task Timeline_FiltersByKind()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, "energy");
        await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id);

        Assert.Equal(2, (await AllAsync(repository, vehicle.Id, "fuel-energy")).Count);
        Assert.Single(await AllAsync(repository, vehicle.Id, "inspection"));
        Assert.Empty(await AllAsync(repository, vehicle.Id, "tax"));
        Assert.Equal(3, (await AllAsync(repository, vehicle.Id)).Count);
    }

    [Fact]
    public async Task Timeline_PagesAndReportsTheFilteredTotal()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        for (var day = 1; day <= 5; day++)
        {
            await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 4, day));
        }

        await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id);

        var second = (await repository.GetTimelinePageAsync(Owner, vehicle.Id, "fuel-energy", 2, 2, CancellationToken.None))!;

        Assert.Equal(5, second.TotalCount);
        Assert.Equal([new DateOnly(2026, 4, 3), new DateOnly(2026, 4, 2)], second.Items.Select(item => item.OccurredOn));
        Assert.Equal(6, (await repository.GetTimelinePageAsync(Owner, vehicle.Id, null, 1, 2, CancellationToken.None))!.TotalCount);
    }

    [Fact]
    public async Task Timeline_FlagsEntriesCreatedFromAnEmail()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        var fuel = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE fuel_energy_entries SET from_email = true WHERE id = @fuel", new { fuel });
        }

        Assert.True(Assert.Single(await AllAsync(repository, vehicle.Id)).FromEmail);
    }

    [Fact]
    public async Task Timeline_ForAnotherOwnersOrUnknownVehicle_IsNull_AndNeverShowsTheirEntries()
    {
        var (dataSource, repository) = await CreateAsync();
        var theirs = await AddVehicleAsync(repository, "owner-b");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, theirs.Id);

        Assert.Null(await repository.GetTimelinePageAsync(Owner, theirs.Id, null, 1, 20, CancellationToken.None));
        Assert.Null(await repository.GetTimelinePageAsync(Owner, Guid.NewGuid(), null, 1, 20, CancellationToken.None));
    }

    [Fact]
    public async Task Timeline_OfAnArchivedVehicle_IsStillReadable()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);
        await repository.ArchiveAsync(Owner, vehicle.Id, Created, CancellationToken.None);

        Assert.Single(await AllAsync(repository, vehicle.Id));
    }
}

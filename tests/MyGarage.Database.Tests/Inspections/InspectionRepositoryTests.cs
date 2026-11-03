using MyGarage.Database.Common;
using MyGarage.Database.Inspections;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Inspections;

[Collection(DatabaseCollection.Name)]
public class InspectionRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, InspectionRepository Inspections)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new InspectionRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<InspectionRecord?> AddAsync(
        InspectionRepository inspections, string ownerId, Guid vehicleId, DateOnly date, string type = "safety", DateTimeOffset? at = null) =>
        inspections.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), type, date, 25.50m, null, at ?? T0, CancellationToken.None);

    [Theory]
    [InlineData("safety", null)]
    [InlineData("emissions", "2027-05-01")]
    public async Task Add_StoresTheValuesWithAudit(string type, string? nextDue)
    {
        var (_, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        DateOnly? nextDueDate = nextDue is null ? null : DateOnly.Parse(nextDue);

        var inspection = await inspections.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), type, new DateOnly(2026, 5, 1), 25.5m, nextDueDate, T0, CancellationToken.None);

        Assert.NotNull(inspection);
        Assert.Equal(vehicle.Id, inspection.VehicleId);
        Assert.Equal(type, inspection.InspectionType);
        Assert.Equal(new DateOnly(2026, 5, 1), inspection.InspectionDate);
        Assert.Equal(25.5m, inspection.Cost);
        Assert.Equal(nextDueDate, inspection.NextDueDate);
        Assert.Equal(T0, inspection.CreatedAt);
        Assert.Equal(T0, inspection.UpdatedAt);
        Assert.Equal("owner-1", inspection.CreatedBy);
        Assert.Equal("owner-1", inspection.UpdatedBy);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.Null(await AddAsync(inspections, "owner-2", vehicle.Id, new DateOnly(2026, 5, 1)));
        Assert.Null(await AddAsync(inspections, "owner-1", Guid.NewGuid(), new DateOnly(2026, 5, 1)));

        Assert.Equal(0, await MaintenanceTestData.CountAsync(dataSource, "inspections"));
    }

    [Fact]
    public async Task List_OrdersByInspectionDateThenCreatedDescending_AndPages()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var older = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var newerFirst = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 3, 1), createdAt: T0);
        var newerSecond = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 3, 1), createdAt: T0.AddMinutes(1));

        var page1 = await inspections.ListPageAsync("owner-1", vehicle.Id, 1, 2, CancellationToken.None);
        var page2 = await inspections.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await inspections.ListPageAsync("owner-1", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([newerSecond, newerFirst], page1!.Items.Select(item => item.Id));
        Assert.Equal([older], page2!.Items.Select(item => item.Id));
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Empty(beyond!.Items);
    }

    [Fact]
    public async Task List_NeverMixesVehiclesOrOwners()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicleA.Id);

        var forB = await inspections.ListPageAsync("owner-1", vehicleB.Id, 1, 10, CancellationToken.None);
        var otherOwner = await inspections.ListPageAsync("owner-2", vehicleA.Id, 1, 10, CancellationToken.None);
        var unknown = await inspections.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None);

        Assert.Empty(forB!.Items);
        Assert.Null(otherOwner);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Get_ReturnsTheInspectionOnlyThroughItsOwnVehicleAndOwner()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicleA.Id);

        Assert.NotNull(await inspections.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await inspections.GetAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.Null(await inspections.GetAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await inspections.GetAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesTheValuesKeepsCreationAuditAndMovesTheInspectionInTheList()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var second = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 2, 1), createdAt: T0);
        var later = T0.AddDays(1);

        var updated = await inspections.UpdateAsync("owner-1", vehicle.Id, first, "emissions", new DateOnly(2026, 6, 1), 40m, new DateOnly(2027, 6, 1), later, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("emissions", updated.InspectionType);
        Assert.Equal(new DateOnly(2026, 6, 1), updated.InspectionDate);
        Assert.Equal(40m, updated.Cost);
        Assert.Equal(new DateOnly(2027, 6, 1), updated.NextDueDate);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal("seed", updated.CreatedBy);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
        var list = await inspections.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([first, second], list!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Update_CanClearTheNextDueDate()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, nextDueDate: new DateOnly(2027, 5, 1));

        var updated = await inspections.UpdateAsync("owner-1", vehicle.Id, id, "safety", new DateOnly(2026, 5, 1), 25m, null, T0, CancellationToken.None);

        Assert.Null(updated!.NextDueDate);
        Assert.Null((await inspections.GetAsync("owner-1", vehicle.Id, id, CancellationToken.None))!.NextDueDate);
    }

    [Fact]
    public async Task UpdateAndDelete_ChangeNothingThroughTheWrongVehicleOrOwner()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicleA.Id, cost: 10m);

        Assert.Null(await inspections.UpdateAsync("owner-1", vehicleB.Id, id, "safety", new DateOnly(2026, 6, 1), 99m, null, T0, CancellationToken.None));
        Assert.Null(await inspections.UpdateAsync("owner-2", vehicleA.Id, id, "safety", new DateOnly(2026, 6, 1), 99m, null, T0, CancellationToken.None));
        Assert.Null(await inspections.UpdateAsync("owner-1", vehicleA.Id, Guid.NewGuid(), "safety", new DateOnly(2026, 6, 1), 99m, null, T0, CancellationToken.None));
        Assert.False(await inspections.DeleteAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.False(await inspections.DeleteAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.False(await inspections.DeleteAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));

        var stored = await inspections.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);
        Assert.Equal(10m, stored!.Cost);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatInspection()
    {
        var (dataSource, vehicles, inspections) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id);
        var remove = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id);

        Assert.True(await inspections.DeleteAsync("owner-1", vehicle.Id, remove, CancellationToken.None));

        Assert.Null(await inspections.GetAsync("owner-1", vehicle.Id, remove, CancellationToken.None));
        Assert.NotNull(await inspections.GetAsync("owner-1", vehicle.Id, keep, CancellationToken.None));
    }
}

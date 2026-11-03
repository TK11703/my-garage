using MyGarage.Database.Common;
using MyGarage.Database.Registrations;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Registrations;

[Collection(DatabaseCollection.Name)]
public class RegistrationRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, RegistrationRepository Registrations)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new RegistrationRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<RegistrationRecord?> AddAsync(
        RegistrationRepository registrations, string ownerId, Guid vehicleId, DateOnly expiration, string state = "WA", DateTimeOffset? at = null) =>
        registrations.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), state, 78.75m, expiration, at ?? T0, CancellationToken.None);

    [Theory]
    [InlineData("WA", "2027-05-01")]
    [InlineData("DC", "2020-01-31")]
    public async Task Add_StoresTheValuesWithAudit(string state, string expiration)
    {
        var (_, vehicles, registrations) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var expirationDate = DateOnly.Parse(expiration);

        var registration = await registrations.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), state, 78.75m, expirationDate, T0, CancellationToken.None);

        Assert.NotNull(registration);
        Assert.Equal(vehicle.Id, registration.VehicleId);
        Assert.Equal(state, registration.State);
        Assert.Equal(2, registration.State.Length);
        Assert.Equal(78.75m, registration.Cost);
        Assert.Equal(expirationDate, registration.ExpirationDate);
        Assert.Equal(T0, registration.CreatedAt);
        Assert.Equal(T0, registration.UpdatedAt);
        Assert.Equal("owner-1", registration.CreatedBy);
        Assert.Equal("owner-1", registration.UpdatedBy);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.Null(await AddAsync(registrations, "owner-2", vehicle.Id, new DateOnly(2027, 5, 1)));
        Assert.Null(await AddAsync(registrations, "owner-1", Guid.NewGuid(), new DateOnly(2027, 5, 1)));

        Assert.Equal(0, await MaintenanceTestData.CountAsync(dataSource, "registrations"));
    }

    [Fact]
    public async Task List_OrdersByExpirationDateThenCreatedDescending_AndPages()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var older = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, expirationDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var newerFirst = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, expirationDate: new DateOnly(2027, 3, 1), createdAt: T0);
        var newerSecond = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, expirationDate: new DateOnly(2027, 3, 1), createdAt: T0.AddMinutes(1));

        var page1 = await registrations.ListPageAsync("owner-1", vehicle.Id, 1, 2, CancellationToken.None);
        var page2 = await registrations.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await registrations.ListPageAsync("owner-1", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([newerSecond, newerFirst], page1!.Items.Select(item => item.Id));
        Assert.Equal([older], page2!.Items.Select(item => item.Id));
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Empty(beyond!.Items);
    }

    [Fact]
    public async Task List_NeverMixesVehiclesOrOwners()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicleA.Id);

        var forB = await registrations.ListPageAsync("owner-1", vehicleB.Id, 1, 10, CancellationToken.None);
        var otherOwner = await registrations.ListPageAsync("owner-2", vehicleA.Id, 1, 10, CancellationToken.None);
        var unknown = await registrations.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None);

        Assert.Empty(forB!.Items);
        Assert.Null(otherOwner);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Get_ReturnsTheRegistrationOnlyThroughItsOwnVehicleAndOwner()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicleA.Id);

        var found = await registrations.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("WA", found.State);
        Assert.Null(await registrations.GetAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.Null(await registrations.GetAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await registrations.GetAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesTheValuesKeepsCreationAuditAndMovesTheRegistrationInTheList()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, expirationDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var second = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id, expirationDate: new DateOnly(2026, 2, 1), createdAt: T0);
        var later = T0.AddDays(1);

        var updated = await registrations.UpdateAsync("owner-1", vehicle.Id, first, "OR", 40m, new DateOnly(2028, 6, 1), later, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("OR", updated.State);
        Assert.Equal(40m, updated.Cost);
        Assert.Equal(new DateOnly(2028, 6, 1), updated.ExpirationDate);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal("seed", updated.CreatedBy);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
        var list = await registrations.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([first, second], list!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task UpdateAndDelete_ChangeNothingThroughTheWrongVehicleOrOwner()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicleA.Id, cost: 10m);

        Assert.Null(await registrations.UpdateAsync("owner-1", vehicleB.Id, id, "OR", 99m, new DateOnly(2028, 6, 1), T0, CancellationToken.None));
        Assert.Null(await registrations.UpdateAsync("owner-2", vehicleA.Id, id, "OR", 99m, new DateOnly(2028, 6, 1), T0, CancellationToken.None));
        Assert.Null(await registrations.UpdateAsync("owner-1", vehicleA.Id, Guid.NewGuid(), "OR", 99m, new DateOnly(2028, 6, 1), T0, CancellationToken.None));
        Assert.False(await registrations.DeleteAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.False(await registrations.DeleteAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.False(await registrations.DeleteAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));

        var stored = await registrations.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);
        Assert.Equal(10m, stored!.Cost);
        Assert.Equal("WA", stored.State);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatRegistration()
    {
        var (dataSource, vehicles, registrations) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id);
        var remove = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicle.Id);

        Assert.True(await registrations.DeleteAsync("owner-1", vehicle.Id, remove, CancellationToken.None));

        Assert.Null(await registrations.GetAsync("owner-1", vehicle.Id, remove, CancellationToken.None));
        Assert.NotNull(await registrations.GetAsync("owner-1", vehicle.Id, keep, CancellationToken.None));
    }
}

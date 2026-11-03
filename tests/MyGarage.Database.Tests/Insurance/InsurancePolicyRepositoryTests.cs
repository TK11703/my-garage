using MyGarage.Database.Common;
using MyGarage.Database.Insurance;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Insurance;

[Collection(DatabaseCollection.Name)]
public class InsurancePolicyRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, InsurancePolicyRepository Policies)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new InsurancePolicyRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<InsurancePolicyRecord?> AddAsync(
        InsurancePolicyRepository policies, string ownerId, Guid vehicleId, DateOnly start, DateOnly end, string duration = "annual", DateTimeOffset? at = null) =>
        policies.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), start, end, duration, 120m, at ?? T0, CancellationToken.None);

    [Theory]
    [InlineData("monthly", "120.00")]
    [InlineData("bi_annual", "640.50")]
    [InlineData("annual", "9999999.99")]
    public async Task Add_StoresTheValuesWithAudit(string duration, string cost)
    {
        var (_, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var costValue = decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture);
        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2027, 5, 1);

        var policy = await policies.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), start, end, duration, costValue, T0, CancellationToken.None);

        Assert.NotNull(policy);
        Assert.Equal(vehicle.Id, policy.VehicleId);
        Assert.Equal(start, policy.StartDate);
        Assert.Equal(end, policy.EndDate);
        Assert.Equal(duration, policy.Duration);
        Assert.Equal(costValue, policy.Cost);
        Assert.Equal(T0, policy.CreatedAt);
        Assert.Equal(T0, policy.UpdatedAt);
        Assert.Equal("owner-1", policy.CreatedBy);
        Assert.Equal("owner-1", policy.UpdatedBy);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.Null(await AddAsync(policies, "owner-2", vehicle.Id, new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1)));
        Assert.Null(await AddAsync(policies, "owner-1", Guid.NewGuid(), new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1)));

        Assert.Equal(0, await MaintenanceTestData.CountAsync(dataSource, "insurance_policies"));
    }

    [Fact]
    public async Task List_OrdersByEndDateThenCreatedDescending_AndPages()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var older = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), createdAt: T0);
        var newerFirst = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, new DateOnly(2026, 3, 1), new DateOnly(2027, 3, 1), createdAt: T0);
        var newerSecond = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 1), createdAt: T0.AddMinutes(1));

        var page1 = await policies.ListPageAsync("owner-1", vehicle.Id, 1, 2, CancellationToken.None);
        var page2 = await policies.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await policies.ListPageAsync("owner-1", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([newerSecond, newerFirst], page1!.Items.Select(item => item.Id));
        Assert.Equal([older], page2!.Items.Select(item => item.Id));
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Empty(beyond!.Items);
    }

    [Fact]
    public async Task List_NeverMixesVehiclesOrOwners()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicleA.Id);

        var forB = await policies.ListPageAsync("owner-1", vehicleB.Id, 1, 10, CancellationToken.None);
        var otherOwner = await policies.ListPageAsync("owner-2", vehicleA.Id, 1, 10, CancellationToken.None);
        var unknown = await policies.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None);

        Assert.Empty(forB!.Items);
        Assert.Null(otherOwner);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Get_ReturnsThePolicyOnlyThroughItsOwnVehicleAndOwner()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicleA.Id);

        var found = await policies.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("annual", found.Duration);
        Assert.Null(await policies.GetAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.Null(await policies.GetAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await policies.GetAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesTheValuesKeepsCreationAuditAndMovesThePolicyInTheList()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), createdAt: T0);
        var second = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id, new DateOnly(2025, 2, 1), new DateOnly(2026, 2, 1), createdAt: T0);
        var later = T0.AddDays(1);

        var updated = await policies.UpdateAsync(
            "owner-1", vehicle.Id, first, new DateOnly(2027, 6, 1), new DateOnly(2028, 6, 1), "monthly", 40m, later, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2027, 6, 1), updated.StartDate);
        Assert.Equal(new DateOnly(2028, 6, 1), updated.EndDate);
        Assert.Equal("monthly", updated.Duration);
        Assert.Equal(40m, updated.Cost);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal("seed", updated.CreatedBy);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
        var list = await policies.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([first, second], list!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task UpdateAndDelete_ChangeNothingThroughTheWrongVehicleOrOwner()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicleA.Id, cost: 10m);
        var start = new DateOnly(2027, 6, 1);
        var end = new DateOnly(2028, 6, 1);

        Assert.Null(await policies.UpdateAsync("owner-1", vehicleB.Id, id, start, end, "monthly", 99m, T0, CancellationToken.None));
        Assert.Null(await policies.UpdateAsync("owner-2", vehicleA.Id, id, start, end, "monthly", 99m, T0, CancellationToken.None));
        Assert.Null(await policies.UpdateAsync("owner-1", vehicleA.Id, Guid.NewGuid(), start, end, "monthly", 99m, T0, CancellationToken.None));
        Assert.False(await policies.DeleteAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.False(await policies.DeleteAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.False(await policies.DeleteAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));

        var stored = await policies.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);
        Assert.Equal(10m, stored!.Cost);
        Assert.Equal("annual", stored.Duration);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatPolicy()
    {
        var (dataSource, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id);
        var remove = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicle.Id);

        Assert.True(await policies.DeleteAsync("owner-1", vehicle.Id, remove, CancellationToken.None));

        Assert.Null(await policies.GetAsync("owner-1", vehicle.Id, remove, CancellationToken.None));
        Assert.NotNull(await policies.GetAsync("owner-1", vehicle.Id, keep, CancellationToken.None));
    }

    [Fact]
    public async Task Add_OverlappingAndGappedPoliciesOnOneVehicle_AreAllKept()
    {
        var (_, vehicles, policies) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.NotNull(await AddAsync(policies, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
        Assert.NotNull(await AddAsync(policies, "owner-1", vehicle.Id, new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1)));
        Assert.NotNull(await AddAsync(policies, "owner-1", vehicle.Id, new DateOnly(2029, 1, 1), new DateOnly(2030, 1, 1), "monthly"));

        var list = await policies.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal(3, list!.TotalCount);
    }
}

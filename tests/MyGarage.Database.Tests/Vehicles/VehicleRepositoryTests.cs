using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.Lookups;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleRepositoryTests(PostgresFixture postgres)
{
    private async Task<VehicleRepository> CreateRepositoryAsync() =>
        new(await postgres.CreateMigratedDatabaseAsync(), new SqlScriptStore());

    private async Task<(VehicleRepository Repository, NpgsqlDataSource DataSource)> CreateWithDataSourceAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new VehicleRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    private static VehicleRecord FullVehicle(string ownerId, Guid vehicleType, Guid fuel, Guid transmission, Guid drivetrain) =>
        TestData.Vehicle(ownerId, "Blue Streak", "Subaru", "Outback", "Limited", "Blue", 42_000, 4) with
        {
            Vin = "1HGCM82633A004352",
            Year = 2019,
            VehicleTypeId = vehicleType,
            Ownership = "leased",
            PurchasedOn = new DateOnly(2024, 3, 15),
            LeaseEndsOn = new DateOnly(2027, 3, 14),
            Engine = "2.5L flat-4",
            Horsepower = 182,
            FuelTypeId = fuel,
            TransmissionId = transmission,
            Seats = 5,
            DrivetrainId = drivetrain,
            WheelSize = 17.5m,
            TireSize = "225/60R17",
        };

    [Fact]
    public async Task InsertThenGet_RoundTripsEveryNewColumnAndReturnsLookupNames()
    {
        var (repository, dataSource) = await CreateWithDataSourceAsync();
        var vehicle = FullVehicle(
            "owner-1",
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Wagon"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.FuelType, "Gasoline"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.Transmission, "Automatic"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.Drivetrain, "AWD"));

        await repository.InsertAsync(vehicle, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.Equal(
            vehicle with { VehicleTypeName = "Wagon", FuelTypeName = "Gasoline", TransmissionName = "Automatic", DrivetrainName = "AWD" },
            loaded);
    }

    [Fact]
    public async Task Update_ReplacesAndClearsTheNewColumns()
    {
        var (repository, dataSource) = await CreateWithDataSourceAsync();
        var vehicle = FullVehicle(
            "owner-1",
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.VehicleType, "Wagon"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.FuelType, "Gasoline"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.Transmission, "Automatic"),
            await LookupTestData.DefaultIdAsync(dataSource, LookupKind.Drivetrain, "AWD"));
        await repository.InsertAsync(vehicle, CancellationToken.None);
        var diesel = await LookupTestData.DefaultIdAsync(dataSource, LookupKind.FuelType, "Diesel");

        var changed = TestData.Vehicle("owner-1", id: vehicle.Id) with { Year = 2020, FuelTypeId = diesel, Horsepower = 200 };
        Assert.True(await repository.UpdateAsync(changed, CancellationToken.None));
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(2020, loaded.Year);
        Assert.Equal(diesel, loaded.FuelTypeId);
        Assert.Equal("Diesel", loaded.FuelTypeName);
        Assert.Equal(200, loaded.Horsepower);
        Assert.Null(loaded.Vin);
        Assert.Null(loaded.VehicleTypeId);
        Assert.Null(loaded.VehicleTypeName);
        Assert.Null(loaded.Ownership);
        Assert.Null(loaded.PurchasedOn);
        Assert.Null(loaded.LeaseEndsOn);
        Assert.Null(loaded.Engine);
        Assert.Null(loaded.TransmissionId);
        Assert.Null(loaded.Seats);
        Assert.Null(loaded.DrivetrainId);
        Assert.Null(loaded.WheelSize);
        Assert.Null(loaded.TireSize);
    }

    [Fact]
    public async Task Insert_UnknownLookupId_IsRejectedByTheForeignKey()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1") with { VehicleTypeId = Guid.NewGuid() };

        var exception = await Assert.ThrowsAsync<PostgresException>(() => repository.InsertAsync(vehicle, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    public static TheoryData<string, Func<VehicleRecord, VehicleRecord>> InvalidDetails => new()
    {
        { "vin of 16 characters", v => v with { Vin = "1HGCM82633A00435" } },
        { "lower-case vin", v => v with { Vin = "1hgcm82633a004352" } },
        { "vin with I", v => v with { Vin = "1HGCM82633A00435I" } },
        { "year 1899", v => v with { Year = 1899 } },
        { "year 2201", v => v with { Year = 2201 } },
        { "ownership rented", v => v with { Ownership = "rented" } },
        { "lease end without leased", v => v with { Ownership = "purchased", LeaseEndsOn = new DateOnly(2030, 1, 1) } },
        { "lease end without ownership", v => v with { LeaseEndsOn = new DateOnly(2030, 1, 1) } },
        { "lease end before purchase", v => v with { Ownership = "leased", PurchasedOn = new DateOnly(2026, 5, 1), LeaseEndsOn = new DateOnly(2026, 4, 30) } },
        { "horsepower 0", v => v with { Horsepower = 0 } },
        { "horsepower 3001", v => v with { Horsepower = 3001 } },
        { "seats 0", v => v with { Seats = 0 } },
        { "seats 21", v => v with { Seats = 21 } },
        { "wheel 9.9", v => v with { WheelSize = 9.9m } },
        { "wheel 30.1", v => v with { WheelSize = 30.1m } },
    };

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public async Task Insert_ValueOutsideTheDatabaseChecks_IsRejected(string description, Func<VehicleRecord, VehicleRecord> invalidate)
    {
        Assert.NotEmpty(description);
        var repository = await CreateRepositoryAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => repository.InsertAsync(invalidate(TestData.Vehicle("owner-1")), CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Update_AnotherOwner_ChangesNothing()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await repository.InsertAsync(vehicle, CancellationToken.None);

        Assert.False(await repository.UpdateAsync((vehicle with { OwnerId = "owner-2" }) with { Year = 2020 }, CancellationToken.None));
        Assert.Null((await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.Year);
    }

    [Fact]
    public async Task InsertThenGet_RoundTripsEveryColumn()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1", "Blue Streak", "Subaru", "Outback", "Limited", "Blue", 42_000, 4);

        await repository.InsertAsync(vehicle, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.Equal(vehicle, loaded);
    }

    [Fact]
    public async Task InsertThenGet_RoundTripsNullOptionalColumns()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1");

        await repository.InsertAsync(vehicle, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Trim);
        Assert.Null(loaded.Color);
        Assert.Null(loaded.Mileage);
        Assert.Null(loaded.Doors);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
    }

    [Fact]
    public async Task GetById_AnotherOwner_ReturnsNull()
    {
        var repository = await CreateRepositoryAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await repository.InsertAsync(vehicle, CancellationToken.None);

        Assert.Null(await repository.GetByIdAsync("owner-2", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListPage_ReturnsOnlyOwnersVehiclesOrderedByNicknameIgnoringCase()
    {
        var repository = await CreateRepositoryAsync();
        foreach (var nickname in new[] { "charlie", "Alpha", "bravo" })
        {
            await repository.InsertAsync(TestData.Vehicle("owner-1", nickname), CancellationToken.None);
        }

        await repository.InsertAsync(TestData.Vehicle("owner-2", "Aardvark"), CancellationToken.None);

        var page = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 1, 12, CancellationToken.None);

        Assert.Equal(["Alpha", "bravo", "charlie"], page.Items.Select(item => item.Nickname));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ListPage_Paging_ReturnsSlicesAndTotals()
    {
        var repository = await CreateRepositoryAsync();
        for (var i = 1; i <= 25; i++)
        {
            await repository.InsertAsync(TestData.Vehicle("owner-1", $"Car {i:D2}"), CancellationToken.None);
        }

        var first = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 1, 12, CancellationToken.None);
        var second = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 2, 12, CancellationToken.None);
        var third = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 3, 12, CancellationToken.None);

        Assert.Equal(12, first.Items.Count);
        Assert.Equal(12, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal("Car 01", first.Items[0].Nickname);
        Assert.Equal("Car 25", third.Items[0].Nickname);
        Assert.All([first, second, third], page => Assert.Equal(25, page.TotalCount));
        Assert.Equal(3, first.TotalPages);
    }

    [Fact]
    public async Task ListPage_EqualNicknames_KeepAStableOrderAcrossPages()
    {
        var repository = await CreateRepositoryAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var vehicle = TestData.Vehicle("owner-1", "Same");
            ids.Add(vehicle.Id);
            await repository.InsertAsync(vehicle, CancellationToken.None);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            seen.AddRange((await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, page, 2, CancellationToken.None)).Items.Select(item => item.Id));
        }

        Assert.Equal(ids.Order(), seen);
    }

    [Fact]
    public async Task ListPage_BeyondLastPage_ReturnsEmptySliceWithRealTotal()
    {
        var repository = await CreateRepositoryAsync();
        await repository.InsertAsync(TestData.Vehicle("owner-1"), CancellationToken.None);

        var page = await repository.ListPageAsync("owner-1", VehicleSearchCriteria.None, 5, 12, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }
}

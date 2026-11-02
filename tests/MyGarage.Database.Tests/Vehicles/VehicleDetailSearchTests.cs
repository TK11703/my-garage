using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.Lookups;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleDetailSearchTests(PostgresFixture postgres)
{
    private const string Owner = "owner-1";

    private sealed record Garage(
        NpgsqlDataSource DataSource,
        VehicleRepository Repository,
        Guid Truck,
        Guid Convertible,
        Guid Gasoline,
        Guid Electric,
        Guid Manual,
        Guid Automatic,
        Guid FourWheelDrive,
        Guid AllWheelDrive,
        Guid RearWheelDrive);

    // Tacoma: 2018, 3.5L V6, truck, gasoline, automatic, 4WD.  R1T: 2022, quad-motor electric, truck, electric, automatic, AWD.
    // Miata: 1999, 1.8L I4, convertible, gasoline, manual, RWD.  Legacy: no year or details.
    private async Task<Garage> CreateGarageAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new VehicleRepository(dataSource, new SqlScriptStore());

        async Task<Guid> Id(string kind, string name) => await LookupTestData.DefaultIdAsync(dataSource, kind, name);
        var garage = new Garage(
            dataSource,
            repository,
            await Id(LookupKind.VehicleType, "Truck"),
            await Id(LookupKind.VehicleType, "Convertible"),
            await Id(LookupKind.FuelType, "Gasoline"),
            await Id(LookupKind.FuelType, "Electric"),
            await Id(LookupKind.Transmission, "Manual"),
            await Id(LookupKind.Transmission, "Automatic"),
            await Id(LookupKind.Drivetrain, "4WD"),
            await Id(LookupKind.Drivetrain, "AWD"),
            await Id(LookupKind.Drivetrain, "RWD"));

        await repository.InsertAsync(TestData.Vehicle(Owner, "Tacoma") with
        {
            Make = "Toyota", Model = "Tacoma", Year = 2018, Vin = "1HGCM82633A004352", Engine = "3.5L V6",
            VehicleTypeId = garage.Truck, FuelTypeId = garage.Gasoline, TransmissionId = garage.Automatic, DrivetrainId = garage.FourWheelDrive,
        }, CancellationToken.None);
        await repository.InsertAsync(TestData.Vehicle(Owner, "R1T") with
        {
            Make = "Rivian", Model = "R1T", Year = 2022, Vin = "5YJ3E1EA7KF317000", Engine = "Quad-motor electric",
            VehicleTypeId = garage.Truck, FuelTypeId = garage.Electric, TransmissionId = garage.Automatic, DrivetrainId = garage.AllWheelDrive,
        }, CancellationToken.None);
        await repository.InsertAsync(TestData.Vehicle(Owner, "Miata") with
        {
            Make = "Mazda", Model = "MX-5", Year = 1999, Engine = "1.8L I4",
            VehicleTypeId = garage.Convertible, FuelTypeId = garage.Gasoline, TransmissionId = garage.Manual, DrivetrainId = garage.RearWheelDrive,
        }, CancellationToken.None);
        await repository.InsertAsync(TestData.Vehicle(Owner, "Legacy", "Ford", "F-150"), CancellationToken.None);

        return garage;
    }

    private static async Task<List<string>> NicknamesAsync(Garage garage, VehicleSearchCriteria criteria, string ownerId = Owner)
    {
        var page = await garage.Repository.ListPageAsync(ownerId, criteria, 1, 50, CancellationToken.None);
        return page.Items.Select(item => item.Nickname).ToList();
    }

    [Theory]
    [InlineData("2018", new[] { "Tacoma" })]
    [InlineData("1999", new[] { "Miata" })]
    [InlineData("317000", new[] { "R1T" })]
    [InlineData("1hgcm", new[] { "Tacoma" })]
    [InlineData("quad", new[] { "R1T" })]
    [InlineData("V6", new[] { "Tacoma" })]
    [InlineData("1.8L", new[] { "Miata" })]
    [InlineData("20", new[] { "R1T", "Tacoma" })]
    public async Task Search_AlsoMatchesYearVinAndEngine(string text, string[] expected)
    {
        var garage = await CreateGarageAsync();

        var names = await NicknamesAsync(garage, new VehicleSearchCriteria { Query = text });

        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), names);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Search_PercentAndUnderscoreInVinOrEngineStayLiteral(string text)
    {
        var garage = await CreateGarageAsync();

        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { Query = text }));
    }

    [Fact]
    public async Task YearBounds_AreInclusiveAndVehiclesWithoutAYearNeverMatch()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["R1T", "Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { MinYear = 2018 }));
        Assert.Equal(["Miata", "Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { MaxYear = 2018 }));
        Assert.Equal(["Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { MinYear = 2018, MaxYear = 2018 }));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { MinYear = 2023 }));
        Assert.Equal(["Legacy", "Miata", "R1T", "Tacoma"], await NicknamesAsync(garage, VehicleSearchCriteria.None));
    }

    [Fact]
    public async Task LookupFilters_MatchByItemId_AndCombineWithEachOtherAndExistingFilters()
    {
        var garage = await CreateGarageAsync();

        Assert.Equal(["R1T", "Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { VehicleTypeId = garage.Truck }));
        Assert.Equal(["Miata", "Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { FuelTypeId = garage.Gasoline }));
        Assert.Equal(["Miata"], await NicknamesAsync(garage, new VehicleSearchCriteria { TransmissionId = garage.Manual }));
        Assert.Equal(["R1T"], await NicknamesAsync(garage, new VehicleSearchCriteria { DrivetrainId = garage.AllWheelDrive }));
        Assert.Equal(["Tacoma"], await NicknamesAsync(garage, new VehicleSearchCriteria { VehicleTypeId = garage.Truck, FuelTypeId = garage.Gasoline }));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { VehicleTypeId = garage.Convertible, FuelTypeId = garage.Electric }));
        Assert.Equal(
            ["Tacoma"],
            await NicknamesAsync(garage, new VehicleSearchCriteria { Make = "toyota", MinYear = 2000, DrivetrainId = garage.FourWheelDrive, Query = "tacoma" }));
    }

    [Fact]
    public async Task Totals_AndPaging_ReflectTheFilteredSet_AndSummariesCarryTheYear()
    {
        var garage = await CreateGarageAsync();

        var page = await garage.Repository.ListPageAsync(Owner, new VehicleSearchCriteria { FuelTypeId = garage.Gasoline }, 1, 1, CancellationToken.None);
        var second = await garage.Repository.ListPageAsync(Owner, new VehicleSearchCriteria { FuelTypeId = garage.Gasoline }, 2, 1, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(["Miata"], page.Items.Select(item => item.Nickname));
        Assert.Equal(1999, page.Items[0].Year);
        Assert.Equal(["Tacoma"], second.Items.Select(item => item.Nickname));
        Assert.Equal(2018, second.Items[0].Year);
        var legacy = (await garage.Repository.ListPageAsync(Owner, new VehicleSearchCriteria { Query = "legacy" }, 1, 12, CancellationToken.None)).Items.Single();
        Assert.Null(legacy.Year);
    }

    [Fact]
    public async Task AnotherOwner_NeverSeesTheseVehiclesOrMatchesTheirIds()
    {
        var garage = await CreateGarageAsync();

        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { VehicleTypeId = garage.Truck }, "owner-2"));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { Query = "2018" }, "owner-2"));
        Assert.Empty(await NicknamesAsync(garage, new VehicleSearchCriteria { MinYear = 1900 }, "owner-2"));
    }

    [Fact]
    public async Task FilterOptions_ListTheLookupItemsUsedByTheOwnersVehicles_SortedAndIncludingHiddenOnes()
    {
        var garage = await CreateGarageAsync();
        await LookupTestData.HideAsync(garage.DataSource, Owner, garage.Truck);

        var options = await garage.Repository.GetFilterOptionsAsync(Owner, CancellationToken.None);

        Assert.Equal(["Convertible", "Truck"], options.VehicleTypes.Select(option => option.Name));
        Assert.Equal(garage.Truck, options.VehicleTypes.Single(option => option.Name == "Truck").Id);
        Assert.Equal(["Electric", "Gasoline"], options.FuelTypes.Select(option => option.Name));
        Assert.Equal(["Automatic", "Manual"], options.Transmissions.Select(option => option.Name));
        Assert.Equal(["4WD", "AWD", "RWD"], options.Drivetrains.Select(option => option.Name));
    }

    [Fact]
    public async Task FilterOptions_OnlyCoverTheCallersOwnGarage()
    {
        var garage = await CreateGarageAsync();
        var custom = await LookupTestData.InsertCustomAsync(garage.DataSource, "owner-2", LookupKind.FuelType, "Hydrogen");
        await garage.Repository.InsertAsync(TestData.Vehicle("owner-2", "Fuel cell") with { FuelTypeId = custom, Year = 2024 }, CancellationToken.None);

        var mine = await garage.Repository.GetFilterOptionsAsync(Owner, CancellationToken.None);
        var theirs = await garage.Repository.GetFilterOptionsAsync("owner-2", CancellationToken.None);

        Assert.DoesNotContain(mine.FuelTypes, option => option.Name == "Hydrogen");
        Assert.Equal(["Hydrogen"], theirs.FuelTypes.Select(option => option.Name));
        Assert.Empty(theirs.VehicleTypes);
        Assert.Empty((await garage.Repository.GetFilterOptionsAsync("owner-3", CancellationToken.None)).FuelTypes);
    }
}

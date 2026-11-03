using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Database.Inspections;
using MyGarage.Database.Insurance;
using MyGarage.Database.Registrations;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Taxes;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

// from_email is false for every record created by hand and is returned by the read statements of all six record kinds.
[Collection(DatabaseCollection.Name)]
public class FromEmailColumnTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;
    private static readonly DateOnly Day = new(2026, 5, 1);

    private async Task<(NpgsqlDataSource DataSource, SqlScriptStore Scripts, Guid VehicleId)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        var vehicle = TestData.Vehicle("owner-1");
        await new VehicleRepository(dataSource, scripts).InsertAsync(vehicle, CancellationToken.None);
        return (dataSource, scripts, vehicle.Id);
    }

    private static async Task MarkFromEmailAsync(NpgsqlDataSource dataSource, string table, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync($"UPDATE {table} SET from_email = true WHERE id = @id", new { id });
    }

    [Fact]
    public async Task ServiceRecords_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new ServiceRecordRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), Day, 1000, null, T0, CancellationToken.None);
        Assert.False(manual!.Record.FromEmail);

        var emailed = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, Day.AddDays(1));
        await MarkFromEmailAsync(dataSource, "service_records", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.Record.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Record.Id).FromEmail);
    }

    [Fact]
    public async Task FuelEnergyEntries_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new FuelEnergyEntryRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), "fuel", Day, 3m, 10m, 30m, T0, CancellationToken.None);
        Assert.False(manual!.FromEmail);

        var emailed = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicleId, entryDate: Day.AddDays(1));
        await MarkFromEmailAsync(dataSource, "fuel_energy_entries", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Id).FromEmail);
    }

    [Fact]
    public async Task Inspections_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new InspectionRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), "safety", Day, 25m, null, T0, CancellationToken.None);
        Assert.False(manual!.FromEmail);

        var emailed = await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicleId, inspectionDate: Day.AddDays(1));
        await MarkFromEmailAsync(dataSource, "inspections", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Id).FromEmail);
    }

    [Fact]
    public async Task Registrations_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new RegistrationRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), "WA", 78.75m, Day.AddYears(1), T0, CancellationToken.None);
        Assert.False(manual!.FromEmail);

        var emailed = await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicleId, expirationDate: Day.AddYears(2));
        await MarkFromEmailAsync(dataSource, "registrations", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Id).FromEmail);
    }

    [Fact]
    public async Task InsurancePolicies_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new InsurancePolicyRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), Day, Day.AddYears(1), "annual", 1240m, T0, CancellationToken.None);
        Assert.False(manual!.FromEmail);

        var emailed = await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicleId, startDate: Day.AddYears(1), endDate: Day.AddYears(2));
        await MarkFromEmailAsync(dataSource, "insurance_policies", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Id).FromEmail);
    }

    [Fact]
    public async Task Taxes_AreManualByDefault_AndReportFromEmailWhenSet()
    {
        var (dataSource, scripts, vehicleId) = await CreateAsync();
        var repository = new TaxRepository(dataSource, scripts);

        var manual = await repository.AddAsync("owner-1", vehicleId, Guid.CreateVersion7(), "lease", 150m, "monthly", T0, CancellationToken.None);
        Assert.False(manual!.FromEmail);

        var emailed = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicleId);
        await MarkFromEmailAsync(dataSource, "taxes", emailed);

        Assert.True((await repository.GetAsync("owner-1", vehicleId, emailed, CancellationToken.None))!.FromEmail);
        var page = await repository.ListPageAsync("owner-1", vehicleId, 1, 10, CancellationToken.None);
        Assert.True(page!.Items.Single(item => item.Id == emailed).FromEmail);
        Assert.False(page.Items.Single(item => item.Id == manual.Id).FromEmail);
    }
}

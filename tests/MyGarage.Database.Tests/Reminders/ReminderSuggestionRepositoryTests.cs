using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderSuggestionRepositoryTests(PostgresFixture postgres)
{
    private const string Owner = "owner-1";

    private async Task<(NpgsqlDataSource DataSource, ReminderRepository Reminders)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new ReminderRepository(dataSource, new SqlScriptStore()));
    }

    private static async Task ServiceAsync(NpgsqlDataSource dataSource, Guid vehicleId, DateOnly on, int odometer, params string[] descriptions)
    {
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, on, odometer);
        foreach (var description in descriptions)
        {
            await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, description);
        }
    }

    private static Task<IReadOnlyList<ReminderSuggestionRecord>?> SuggestAsync(ReminderRepository reminders, Guid vehicleId, string owner = Owner) =>
        reminders.ListSuggestionsAsync(owner, vehicleId, CancellationToken.None);

    [Fact]
    public async Task ARepeatedService_ProposesTheMedianGapAsTheInterval()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Oil change");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1), 15_000, "Oil change");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2026, 1, 1), 20_100, "Oil change");

        var suggestion = Assert.Single((await SuggestAsync(reminders, vehicleId))!);

        Assert.Equal("Oil change", suggestion.Name);
        Assert.Equal(5_000, suggestion.IntervalMiles);
        Assert.Equal(6, suggestion.IntervalMonths);
        Assert.Equal(new DateOnly(2026, 1, 1), suggestion.LastDoneOn);
        Assert.Equal(20_100, suggestion.LastDoneMileage);
        Assert.Equal(3, suggestion.Times);
    }

    [Fact]
    public async Task AServiceDoneOnce_IsNotSuggested()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Brake pads");

        Assert.Empty((await SuggestAsync(reminders, vehicleId))!);
    }

    [Fact]
    public async Task DescriptionsMatchIgnoringCaseAndSpaces_AndTheNewestSpellingIsUsed()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "oil change ");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1), 15_000, "Oil Change");

        var suggestion = Assert.Single((await SuggestAsync(reminders, vehicleId))!);

        Assert.Equal("Oil Change", suggestion.Name);
    }

    [Fact]
    public async Task AServiceThatAlreadyHasAReminderOfTheSameName_IsLeftOut()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Oil change", "Rotation");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1), 15_000, "Oil change", "Rotation");
        await reminders.AddAsync(Owner, vehicleId, Guid.CreateVersion7(), new ReminderValues(" OIL CHANGE", 5_000, null, new DateOnly(2025, 7, 1), 15_000), TestData.Created, CancellationToken.None);

        var suggestion = Assert.Single((await SuggestAsync(reminders, vehicleId))!);

        Assert.Equal("Rotation", suggestion.Name);
    }

    [Fact]
    public async Task ARemindersOnAnotherVehicle_DoesNotHideTheSuggestion()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var otherId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Oil change");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1), 15_000, "Oil change");
        await reminders.AddAsync(Owner, otherId, Guid.CreateVersion7(), new ReminderValues("Oil change", 5_000, null, new DateOnly(2025, 7, 1), 15_000), TestData.Created, CancellationToken.None);

        Assert.Single((await SuggestAsync(reminders, vehicleId))!);
    }

    [Fact]
    public async Task AtMostFiveAreReturned_MostRecentlyDoneFirst()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        for (var i = 0; i < 6; i++)
        {
            await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1).AddDays(i), 10_000 + i, $"Service {i}");
            await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1).AddDays(i), 15_000 + i, $"Service {i}");
        }

        var suggestions = (await SuggestAsync(reminders, vehicleId))!;

        Assert.Equal(["Service 5", "Service 4", "Service 3", "Service 2", "Service 1"], suggestions.Select(s => s.Name));
    }

    [Fact]
    public async Task WithoutADistanceGap_OnlyTheTimeIntervalIsProposed_AndWithoutAnyGapNothingIs()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Wipers");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2026, 1, 1), 10_000, "Wipers");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 6, 1), 12_000, "Same day");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 6, 1), 12_000, "Same day");

        var suggestions = (await SuggestAsync(reminders, vehicleId))!;

        var wipers = Assert.Single(suggestions);
        Assert.Equal("Wipers", wipers.Name);
        Assert.Null(wipers.IntervalMiles);
        Assert.Equal(12, wipers.IntervalMonths);
    }

    [Fact]
    public async Task AnArchivedVehicle_SuggestsNothing_AndAnotherOwnersOrUnknownVehicleIsNull()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 1, 1), 10_000, "Oil change");
        await ServiceAsync(dataSource, vehicleId, new DateOnly(2025, 7, 1), 15_000, "Oil change");
        Assert.Single((await SuggestAsync(reminders, vehicleId))!);

        await new VehicleRepository(dataSource, new SqlScriptStore()).ArchiveAsync(Owner, vehicleId, TestData.Created, CancellationToken.None);

        Assert.Empty((await SuggestAsync(reminders, vehicleId))!);
        Assert.Null(await SuggestAsync(reminders, vehicleId, "someone-else"));
        Assert.Null(await SuggestAsync(reminders, Guid.NewGuid()));
    }
}

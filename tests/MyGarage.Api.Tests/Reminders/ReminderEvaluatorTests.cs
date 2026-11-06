using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using Npgsql;

namespace MyGarage.Api.Tests.Reminders;

// Seeds vehicles and reminders straight into the database so a test controls exactly when the notifier runs.
public static class ReminderSeed
{
    public static async Task<Guid> InsertVehicleAsync(
        this ApiFactory factory, string ownerId, int? mileage = null, int? latestOdometer = null, int? year = 2020, string make = "Ford", string model = "F-150")
    {
        var id = Guid.CreateVersion7();
        await factory.ExecuteScalarAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, year, mileage, latest_odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, @make, @model, @year, @mileage, @latest, now(), now(), @ownerId, @ownerId)",
            ("id", id), ("ownerId", ownerId), ("make", make), ("model", model), ("year", year), ("mileage", mileage), ("latest", latestOdometer));
        return id;
    }

    public static async Task<Guid> InsertReminderAsync(
        this ApiFactory factory,
        Guid vehicleId,
        string name = "Oil change",
        string appliesTo = "vehicle",
        string? subject = null,
        int? intervalMiles = 5_000,
        int? intervalMonths = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000,
        bool paused = false,
        string? notifiedState = null)
    {
        var id = Guid.CreateVersion7();
        await factory.ExecuteScalarAsync(
            "INSERT INTO reminders (id, vehicle_id, name, applies_to, subject, interval_miles, interval_months, last_done_on, last_done_mileage, is_paused, notified_state, " +
            "created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @name, @appliesTo, @subject, @miles, @months, @lastOn, @lastMileage, @paused, @notified, now(), now(), 'seed', 'seed')",
            ("id", id), ("vehicleId", vehicleId), ("name", name), ("appliesTo", appliesTo), ("subject", subject), ("miles", intervalMiles),
            ("months", intervalMonths), ("lastOn", lastDoneOn ?? new DateOnly(2026, 5, 1)), ("lastMileage", lastDoneMileage), ("paused", paused), ("notified", notifiedState));
        return id;
    }

    public static async Task SetLatestOdometerAsync(this ApiFactory factory, Guid vehicleId, int? odometer) =>
        await factory.ExecuteScalarAsync("UPDATE vehicles SET latest_odometer_reading = @odometer WHERE id = @vehicleId", ("odometer", odometer), ("vehicleId", vehicleId));

    public static async Task<string?> NotifiedStateAsync(this ApiFactory factory, Guid reminderId) =>
        (string?)await factory.ExecuteScalarAsync("SELECT notified_state FROM reminders WHERE id = @reminderId", ("reminderId", reminderId));

    public static async Task<List<string>> NotificationKindsAsync(this ApiFactory factory, string ownerId, Guid? reminderId = null)
    {
        var joined = (string?)await factory.ExecuteScalarAsync(
            "SELECT string_agg(kind, ',' ORDER BY created_at, id) FROM notifications WHERE owner_id = @ownerId AND (@reminderId::uuid IS NULL OR reminder_id = @reminderId)",
            ("ownerId", ownerId), ("reminderId", reminderId));
        return joined is null ? [] : [.. joined.Split(',')];
    }
    public static async Task<string?> NotificationTitleAsync(this ApiFactory factory, Guid reminderId) =>
        (string?)await factory.ExecuteScalarAsync("SELECT title FROM notifications WHERE reminder_id = @reminderId LIMIT 1", ("reminderId", reminderId));
}

// Collects log messages so a test can check what the notifier writes.
public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}

public class ReminderEvaluatorTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-eval-oid";
    private const string Bob = "bob-eval-oid";

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ReminderEvaluator NewEvaluator(TestTimeProvider? clock = null, ReminderRepository? repository = null, ListLogger<ReminderEvaluator>? logger = null) =>
        new(
            repository ?? factory.Services.GetRequiredService<ReminderRepository>(),
            clock ?? new TestTimeProvider(Now),
            logger ?? new ListLogger<ReminderEvaluator>());

    // Oil change every 5,000 miles from 40,000 on a vehicle at the given mileage.
    private async Task<(Guid Vehicle, Guid Reminder)> SeedOilAsync(string ownerId, int vehicleMileage, string? notified = null)
    {
        var vehicle = await factory.InsertVehicleAsync(ownerId, latestOdometer: vehicleMileage);
        return (vehicle, await factory.InsertReminderAsync(vehicle, notifiedState: notified));
    }

    [Fact]
    public async Task ANewlyDueSoonReminder_RaisesOneNotification_AndRemembersTheState()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 44_800);

        var raised = await NewEvaluator().EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        Assert.Equal(1, raised);
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Alice));
        Assert.Equal("due_soon", await factory.NotifiedStateAsync(reminder));
    }

    [Fact]
    public async Task ANewlyOverdueReminder_RaisesOneOverdueNotification()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 45_300);

        await NewEvaluator().EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice));
        Assert.Equal("overdue", await factory.NotifiedStateAsync(reminder));
    }

    [Fact]
    public async Task DueSoonThenOverdue_IsExactlyTwoNotifications_AndRepeatsAddNone()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 44_800);
        var clock = new TestTimeProvider(Now);
        var evaluator = NewEvaluator(clock);

        await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);
        await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        await factory.SetLatestOdometerAsync(vehicle, 45_100);
        await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);
        await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        Assert.Equal(["reminder_due_soon", "reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder));
    }

    [Fact]
    public async Task AnEasedState_StoresTheLowerState_WithoutNotifying_AndLaterNotifiesAgain()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 45_300, notified: "overdue");
        var evaluator = NewEvaluator();

        await factory.SetLatestOdometerAsync(vehicle, 44_800);
        Assert.Equal(0, await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None));
        Assert.Equal("due_soon", await factory.NotifiedStateAsync(reminder));
        Assert.Empty(await factory.NotificationKindsAsync(Alice));

        await factory.SetLatestOdometerAsync(vehicle, 45_300);
        Assert.Equal(1, await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None));
        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task UpcomingAndMileageNeeded_RaiseNothing_AndClearTheRememberedState()
    {
        var (vehicle, upcoming) = await SeedOilAsync(Alice, 41_000, notified: "due_soon");
        var noMileageVehicle = await factory.InsertVehicleAsync(Alice);
        var needs = await factory.InsertReminderAsync(noMileageVehicle, notifiedState: "overdue");

        var raised = await NewEvaluator().EvaluateOwnerAsync(Alice, CancellationToken.None);

        Assert.Equal(0, raised);
        Assert.Null(await factory.NotifiedStateAsync(upcoming));
        Assert.Null(await factory.NotifiedStateAsync(needs));
        Assert.NotEqual(Guid.Empty, vehicle);
    }

    [Fact]
    public async Task PausedReminders_AreSkipped()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        var reminder = await factory.InsertReminderAsync(vehicle, paused: true);

        await NewEvaluator().EvaluateAllAsync(CancellationToken.None);

        Assert.Empty(await factory.NotificationKindsAsync(Alice));
        Assert.Null(await factory.NotifiedStateAsync(reminder));
    }

    [Fact]
    public async Task TheTitle_NamesTheReminderTheEquipmentOrLocationAndTheVehicle()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        var plain = await factory.InsertReminderAsync(vehicle, "Oil change");
        var equipment = await factory.InsertReminderAsync(vehicle, "Hitch check", "equipment", "Trailer hitch");

        await NewEvaluator().EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        Assert.Equal("Oil change on 2020 Ford F-150 is overdue", await factory.NotificationTitleAsync(plain));
        Assert.Equal("Hitch check (Trailer hitch) on 2020 Ford F-150 is overdue", await factory.NotificationTitleAsync(equipment));
    }

    [Fact]
    public async Task ADueSoonTitle_SaysDueSoon()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 44_800);

        await NewEvaluator().EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        Assert.Equal("Oil change on 2020 Ford F-150 is due soon", await factory.NotificationTitleAsync(reminder));
    }

    [Fact]
    public async Task ALongTitle_IsTruncatedTo120Characters()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        var reminder = await factory.InsertReminderAsync(vehicle, new string('n', 100), "location", new string('l', 100));

        await NewEvaluator().EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);

        var title = await factory.NotificationTitleAsync(reminder);
        Assert.Equal(120, title!.Length);
    }

    [Fact]
    public async Task EvaluatingOneVehicle_TouchesOnlyThatVehiclesReminders()
    {
        var (one, first) = await SeedOilAsync(Alice, 50_000);
        var (_, second) = await SeedOilAsync(Alice, 50_000);

        await NewEvaluator().EvaluateVehicleAsync(Alice, one, CancellationToken.None);

        Assert.Equal("overdue", await factory.NotifiedStateAsync(first));
        Assert.Null(await factory.NotifiedStateAsync(second));
    }

    [Fact]
    public async Task EvaluatingOneVehicle_NeverTouchesAnotherOwnersReminders()
    {
        var (bobsVehicle, bobs) = await SeedOilAsync(Bob, 50_000);

        await NewEvaluator().EvaluateVehicleAsync(Alice, bobsVehicle, CancellationToken.None);

        Assert.Null(await factory.NotifiedStateAsync(bobs));
        Assert.Empty(await factory.NotificationKindsAsync(Bob));
    }

    [Fact]
    public async Task EvaluateAll_CoversEveryOwner_AndReturnsHowManyNotificationsWereRaised()
    {
        await SeedOilAsync(Alice, 50_000);
        await SeedOilAsync(Bob, 44_800);
        await SeedOilAsync(Bob, 41_000);

        var raised = await NewEvaluator().EvaluateAllAsync(CancellationToken.None);

        Assert.Equal(2, raised);
        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice));
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Bob));
    }

    [Fact]
    public async Task ADateBasedReminder_NotifiesWhenTheClockPassesTheThreshold()
    {
        var clock = new TestTimeProvider(Now);
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var reminder = await factory.InsertReminderAsync(vehicle, intervalMiles: null, intervalMonths: 12, lastDoneOn: new DateOnly(2025, 12, 31), lastDoneMileage: null);
        var evaluator = NewEvaluator(clock);

        Assert.Equal(0, await evaluator.EvaluateAllAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromDays(52));
        Assert.Equal(1, await evaluator.EvaluateAllAsync(CancellationToken.None));
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Alice, reminder));

        clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(1, await evaluator.EvaluateAllAsync(CancellationToken.None));
        Assert.Equal(["reminder_due_soon", "reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder));
    }

    [Fact]
    public async Task ConcurrentEvaluators_RaiseOneNotification()
    {
        var (vehicle, reminder) = await SeedOilAsync(Alice, 50_000);
        var evaluators = Enumerable.Range(0, 6).Select(_ => NewEvaluator()).ToList();

        await Task.WhenAll(evaluators.Select(evaluator => evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None)));

        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder));
    }

    [Fact]
    public async Task TryEvaluate_LogsAFailureByTypeOnly_AndNeverThrows()
    {
        var logger = new ListLogger<ReminderEvaluator>();
        var broken = new ReminderRepository(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=1;Command Timeout=1"), new SqlScriptStore());
        var evaluator = NewEvaluator(repository: broken, logger: logger);

        await evaluator.TryEvaluateVehicleAsync("owner", Guid.NewGuid(), CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("Exception", message);
        Assert.DoesNotContain("owner", message);
    }
}

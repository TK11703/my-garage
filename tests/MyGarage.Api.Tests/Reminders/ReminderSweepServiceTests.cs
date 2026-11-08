using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderSweepServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-sweep-oid";

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ReminderSweepService NewService(TestTimeProvider clock, ReminderSweepOptions? options = null, ReminderRepository? repository = null, ListLogger<ReminderSweepService>? logger = null)
    {
        var evaluator = new ReminderEvaluator(
            repository ?? factory.Services.GetRequiredService<ReminderRepository>(), clock, NullLogger<ReminderEvaluator>.Instance);
        return new ReminderSweepService(evaluator, Options.Create(options ?? new ReminderSweepOptions()), logger ?? new ListLogger<ReminderSweepService>());
    }

    [Fact]
    public async Task RunOnce_RaisesTheDueSoonNotificationWhenTheClockPassesTheThreshold_AndNothingOnASecondRun()
    {
        var clock = new TestTimeProvider(Now);
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var reminder = await factory.InsertReminderAsync(vehicle, intervalMiles: null, intervalMonths: 12, lastDoneOn: new DateOnly(2025, 12, 31), lastDoneMileage: null);
        var service = NewService(clock);

        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromDays(52));
        Assert.Equal(1, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal(["reminder_due_soon"], await factory.NotificationKindsAsync(Alice, reminder));
    }

    [Fact]
    public async Task RunOnce_RaisesTheOverdueNotificationWhenTheDueDateArrives()
    {
        var clock = new TestTimeProvider(Now);
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var reminder = await factory.InsertReminderAsync(vehicle, intervalMiles: null, intervalMonths: 12, lastDoneOn: new DateOnly(2025, 12, 31), lastDoneMileage: null);
        var service = NewService(clock);

        clock.Advance(TimeSpan.FromDays(52));
        await service.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(1, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal(["reminder_due_soon", "reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder));
    }

    [Fact]
    public async Task ADisabledSweep_DoesNothing()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        await factory.InsertReminderAsync(vehicle);
        var service = NewService(new TestTimeProvider(Now), new ReminderSweepOptions { Enabled = false });

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task AnEnabledSweep_RunsAtOnceWhenStarted()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        await factory.InsertReminderAsync(vehicle);
        var service = NewService(new TestTimeProvider(Now));

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(async () => (await factory.NotificationKindsAsync(Alice)).Count == 1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice));
    }

    [Fact]
    public async Task AFailingRun_IsLoggedByTypeOnly_AndTheLoopKeepsGoing()
    {
        var logger = new ListLogger<ReminderSweepService>();
        var broken = new ReminderRepository(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=1;Command Timeout=1"), new SqlScriptStore());
        var service = NewService(new TestTimeProvider(Now), repository: broken, logger: logger);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(() => Task.FromResult(logger.Messages.Count >= 1));
            Assert.False(service.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Contains("Exception", logger.Messages[0]);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    public void TheInterval_IsAtLeastOneMinute(int configured, int expectedMinutes) =>
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ReminderSweepService.IntervalOf(new ReminderSweepOptions { IntervalMinutes = configured }));

    [Fact]
    public void TheDefaults_AreEnabledAndHourly()
    {
        var options = new ReminderSweepOptions();

        Assert.True(options.Enabled);
        Assert.Equal(60, options.IntervalMinutes);
        Assert.Equal("Reminders:Sweep", ReminderSweepOptions.SectionName);
    }

    [Fact]
    public async Task TheApiFactory_DisablesTheBackgroundSweep()
    {
        _ = factory.Services;
        var options = factory.Services.GetRequiredService<IOptions<ReminderSweepOptions>>().Value;

        Assert.False(options.Enabled);
        await Task.CompletedTask;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The condition was not met in time.");
    }
}

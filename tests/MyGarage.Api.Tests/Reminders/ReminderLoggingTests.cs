using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;

namespace MyGarage.Api.Tests.Reminders;

// FR-021: logs never carry reminder names, vehicle names, or mileages.
// Shares a collection with the other logging tests because hosts replace the static Serilog logger and must not overlap.
[Collection("Logging")]
public class ReminderLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Owner = "log-reminder-user";

    // Distinctive values that cannot appear in a log by accident.
    private static readonly string[] Markers = ["Zebra Oil Marker", "Quokka Hitch Marker", "Pangolin", "7654321", "6543219"];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            Entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task EveryFlow_KeepsReminderValuesOutOfTheLogs()
    {
        var provider = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var client = logged.CreateClient();
        // Proves the host writes to the capturing provider, so an empty result below could not hide a leak.
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReminderLoggingTests").LogInformation("capture check");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = Owner }));

        var vehicle = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Pangolin", Model = "F-150", Year = 2020, Mileage = 7_654_321 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        // Create (already overdue, so the notifier raises), update, complete, pause, resume, an invalid create, and delete.
        var created = (await (await client.PostAsJsonAsync(Url(vehicle.Id), Request("Zebra Oil Marker", miles: 5_000, lastDoneMileage: 1_000)))
            .Content.ReadFromJsonAsync<ReminderDto>())!;
        await client.PostAsJsonAsync(Url(vehicle.Id), Request("Quokka Hitch Marker", miles: 3_000, lastDoneMileage: 6_543_219));
        await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request("Zebra Oil Marker", miles: 5_000, lastDoneMileage: 6_543_219));
        await client.PostAsJsonAsync(Url(vehicle.Id, created.Id, "complete"), new CompleteReminderRequest { DoneOn = Today, Mileage = 7_654_321, UpdateVehicleMileage = true });
        await client.PostAsync(Url(vehicle.Id, created.Id, "pause"), null);
        await client.PostAsync(Url(vehicle.Id, created.Id, "resume"), null);
        await client.PostAsJsonAsync(Url(vehicle.Id), new ReminderRequest { Name = "Zebra Oil Marker", IntervalMiles = 0 });
        await client.GetAsync(Url(vehicle.Id));
        await client.GetAsync("/api/reminders");
        await client.GetAsync("/api/dashboard");

        // The sweep and a failing check write to the log as well.
        await logged.Services.GetRequiredService<ReminderSweepService>().RunOnceAsync(CancellationToken.None);
        await client.DeleteAsync(Url(vehicle.Id, created.Id));

        var log = string.Join('\n', provider.Entries);
        Assert.Contains("capture check", log);
        foreach (var marker in Markers)
        {
            Assert.DoesNotContain(marker, log);
        }
    }
}

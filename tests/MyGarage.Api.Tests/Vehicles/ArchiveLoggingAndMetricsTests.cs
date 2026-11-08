using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

// Archiving and restoring log ids and outcomes only, and count successful changes (specs/014-archive-vehicle FR-012).
[Collection("Logging")]
public class ArchiveLoggingAndMetricsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly string[] Markers = ["QZMAKE", "QZMODEL", "QZTRIM", "QZCOLOR", "WZW9WZW9WZW9WZW9W", "QZNOTE"];

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
    public async Task ArchiveAndRestore_LogTheVehicleId_ButNeverItsDetails()
    {
        var provider = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var client = logged.CreateClient();
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ArchiveLogging").LogInformation("capture check");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = "archive-log-user" }));
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest
        {
            Make = "QZMAKE", Model = "QZMODEL", Trim = "QZTRIM", Color = "QZCOLOR", Year = 2020, Vin = "WZW9WZW9WZW9WZW9W",
        })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/notes", new JournalNoteRequest { Body = "QZNOTE" });

        await client.PostAsync($"/api/vehicles/{created.Id}/archive", null);
        await client.PostAsync($"/api/vehicles/{created.Id}/archive", null);
        await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/notes", new JournalNoteRequest { Body = "QZNOTE" });
        await client.PostAsync($"/api/vehicles/{created.Id}/restore", null);

        var entries = provider.Entries.ToList();
        Assert.Contains(entries, entry => entry.Contains("capture check", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains($"Vehicle {created.Id} was archived", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains($"Vehicle {created.Id} was restored", StringComparison.Ordinal));
        Assert.All(entries, entry => Assert.DoesNotContain(Markers, marker => entry.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheCounters_RiseOncePerSuccessfulChange_AndNotOnARefusedOne()
    {
        long archived = 0, restored = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "MyGarage.Vehicles")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "vehicles_archived")
            {
                Interlocked.Add(ref archived, measurement);
            }
            else if (instrument.Name == "vehicles_restored")
            {
                Interlocked.Add(ref restored, measurement);
            }
        });
        listener.Start();
        var client = factory.CreateClientForUser("archive-metrics-user");
        var vehicle = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/restore", null)).StatusCode);
        Assert.Equal((0L, 0L), (Interlocked.Read(ref archived), Interlocked.Read(ref restored)));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/vehicles/{Guid.NewGuid()}/archive", null)).StatusCode);
        Assert.Equal((1L, 0L), (Interlocked.Read(ref archived), Interlocked.Read(ref restored)));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/vehicles/{vehicle.Id}/restore", null)).StatusCode);
        Assert.Equal((1L, 1L), (Interlocked.Read(ref archived), Interlocked.Read(ref restored)));
    }
}

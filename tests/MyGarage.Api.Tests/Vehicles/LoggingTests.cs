using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

// FR-018: logs never carry vehicle details, search text, tags, journal text, captions, file names, or image bytes.
public class LoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly string[] Markers =
    [
        "QZMAKE", "QZMODEL", "QZTRIM", "QZCOLOR", "QZSEARCH", "QZTAG", "QZNOTE", "QZCAT", "QZCAPTION", "QZFILE", "QZBYTES",
        "WZW9WZW9WZW9WZW9W", "QZENGINE", "QZTIRE", "QZITEM",
    ];

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

    private static byte[] PngWithMarker() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "QZBYTES"u8.ToArray(), .. new byte[16]];

    [Fact]
    public async Task EveryFlow_KeepsUserEnteredValuesOutOfTheLogs()
    {
        var provider = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var client = logged.CreateClient();
        // Proves the host writes to the capturing provider, so an empty result below could not hide a leak.
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LoggingTests").LogInformation("capture check");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = "log-user" }));

        var created = await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest
        {
            Make = "QZMAKE", Model = "QZMODEL", Trim = "QZTRIM", Color = "QZCOLOR", Mileage = 1, Doors = 4, Year = 2020,
            Vin = "WZW9WZW9WZW9WZW9W", Engine = "QZENGINE", TireSize = "QZTIRE",
        })).Content.ReadFromJsonAsync<VehicleDetailDto>();
        await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "QZMAKE", Model = "QZMODEL", Mileage = -1, Year = 2020, Vin = "WZW9WZW9WZW9WZW9W", Horsepower = 0 });
        await client.GetAsync("/api/vehicles?q=QZSEARCH&make=QZMAKE&color=QZCOLOR&tag=QZTAG");
        await client.GetAsync("/api/vehicles?q=WZW9WZW9WZW9WZW9W");
        await client.GetAsync("/api/vehicles/filter-options");
        await client.PutAsJsonAsync($"/api/vehicles/{created!.Id}", new VehicleRequest
        {
            Make = "QZMAKE", Model = "QZMODEL", Year = 2020, Vin = "WZW9WZW9WZW9WZW9W", Engine = "QZENGINE", TireSize = "QZTIRE",
        });

        var item = await (await client.PostAsJsonAsync("/api/lookups/fuel-types", new AddLookupItemRequest { Name = "QZITEM" })).Content.ReadFromJsonAsync<LookupItemDto>();
        await client.PostAsJsonAsync("/api/lookups/fuel-types", new AddLookupItemRequest { Name = "QZITEM" });
        await client.PutAsJsonAsync($"/api/lookups/fuel-types/{item!.Id}/visibility", new SetLookupVisibilityRequest { IsHidden = true });
        await client.GetAsync("/api/lookups?includeHidden=true");

        await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/tags", new AddVehicleTagRequest { Name = "QZTAG" });
        await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/tags", new AddVehicleTagRequest { Name = "QZTAG" });
        await client.GetAsync("/api/features?query=QZTAG");

        var note = await (await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/notes", new JournalNoteRequest { Body = "QZNOTE", Category = "QZCAT" }))
            .Content.ReadFromJsonAsync<JournalNoteDto>();
        await client.PutAsJsonAsync($"/api/vehicles/{created.Id}/notes/{note!.Id}", new JournalNoteRequest { Body = "QZNOTE edited", Category = "QZCAT" });
        await client.PostAsJsonAsync($"/api/vehicles/{created.Id}/notes", new JournalNoteRequest { Body = "QZNOTE" + new string('x', 4100) });

        var upload = new MultipartFormDataContent
        {
            { new ByteArrayContent(PngWithMarker()), "file", "QZFILE.png" },
            { new StringContent("QZCAPTION"), "caption" },
        };
        var image = await (await client.PostAsync($"/api/vehicles/{created.Id}/images", upload)).Content.ReadFromJsonAsync<VehicleImageDto>();
        var spoofed = new MultipartFormDataContent { { new ByteArrayContent("QZBYTES not an image"u8.ToArray()), "file", "QZFILE.jpg" } };
        await client.PostAsync($"/api/vehicles/{created.Id}/images", spoofed);
        await client.GetAsync($"/api/vehicles/{created.Id}/images/{image!.Id}/content");
        await client.PutAsync($"/api/vehicles/{created.Id}/images/{image.Id}/main", null);
        await client.DeleteAsync($"/api/vehicles/{created.Id}/images/{image.Id}");
        await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}");
        await client.DeleteAsync($"/api/vehicles/{created.Id}");

        var logs = string.Join('\n', provider.Entries);
        Assert.Contains("capture check", logs);
        foreach (var marker in Markers)
        {
            Assert.DoesNotContain(marker, logs);
        }
    }
}

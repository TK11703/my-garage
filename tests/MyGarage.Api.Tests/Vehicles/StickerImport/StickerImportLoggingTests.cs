using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

// FR-015: reading a sticker never writes the VIN, the features, the values, the file name, or the file's bytes to logs or metric tags.
// Shares the Logging collection because hosts replace the static Serilog logger and must not overlap.
[Collection("Logging")]
public class StickerImportLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "qz-sticker-oid";
    private const string Url = "/api/vehicles/sticker-extractions";

    private static readonly string[] Markers =
    [
        "1FTFW1E50NFA00001", "QZMAKE", "QZMODEL", "QZTRIM", "QZENGINE", "QZCOLOR", "QZTIRE", "QZFEATURE", "QZBODY", "QZFILE", "QZBYTES", "QZLEAK",
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

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("tok", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static string Answer() =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["isWindowSticker"] = true,
            ["vehiclesDetected"] = 1,
            ["year"] = 2024,
            ["make"] = "QZMAKE",
            ["model"] = "QZMODEL",
            ["trim"] = "QZTRIM",
            ["vin"] = "1FTFW1E50NFA00001",
            ["bodyStyle"] = "QZBODY",
            ["engine"] = "QZENGINE",
            ["horsepower"] = 400,
            ["fuelType"] = null,
            ["transmission"] = null,
            ["drivetrain"] = null,
            ["exteriorColor"] = "QZCOLOR",
            ["seats"] = 5,
            ["doors"] = 4,
            ["wheelSizeInches"] = 20,
            ["tireSize"] = "QZTIRE",
            ["features"] = new object[] { new { name = "QZFEATURE", sameAs = Array.Empty<int>() } },
        });

    private static string Envelope(string answer) =>
        JsonSerializer.Serialize(new { output = new object[] { new { type = "message", content = new[] { new { type = "output_text", text = answer } } } } });

    // The real extractor and endpoint, with only the model's HTTP answer faked.
    private (HttpClient Client, CapturingLoggerProvider Logs, IDisposable Host) CreateHost(HttpStatusCode status, string body)
    {
        var provider = new CapturingLoggerProvider();
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(provider);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStickerExtractor>();
                services.AddSingleton<IStickerExtractor>(sp => new StickerExtractor(
                    new HttpClient(new StubHandler(status, body)),
                    Options.Create(new ExtractionOptions { Endpoint = "https://aoai.example.test/", Deployment = "gpt-test" }),
                    Options.Create(new StickerImportOptions()),
                    new FakeCredential(),
                    sp.GetRequiredService<ILogger<StickerExtractor>>()));
            });
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = Alice }));
        return (client, provider, host);
    }

    private static MultipartFormDataContent Form()
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. Encoding.ASCII.GetBytes("%PDF-1.7 "), .. Encoding.ASCII.GetBytes("QZBYTES QZLEAK")]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "QZFILE-sticker.pdf");
        return content;
    }

    private static void AssertClean(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var marker in Markers)
            {
                Assert.DoesNotContain(marker, entry, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task ASuccessfulRead_LogsOnlyTheOutcomeTypeSizeAndTime()
    {
        var (client, logs, host) = CreateHost(HttpStatusCode.OK, Envelope(Answer()));
        using var _ = host;

        var response = await client.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.Equal("QZMAKE", result.Make);
        AssertClean(logs.Entries);
        Assert.Contains(logs.Entries, entry => entry.Contains("Sticker import finished", StringComparison.Ordinal) && entry.Contains("filled", StringComparison.Ordinal) && entry.Contains("application/pdf", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Contains("Sticker read in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedRead_LogsOnlyTheReasonCode_NeverTheServicesBody()
    {
        var (client, logs, host) = CreateHost(HttpStatusCode.InternalServerError, "QZLEAK the service echoed the document QZBYTES");
        using var _ = host;

        var response = await client.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertClean(logs.Entries);
        Assert.Contains(logs.Entries, entry => entry.Contains("Sticker reading failed with", StringComparison.Ordinal) && entry.Contains("service_error", StringComparison.Ordinal) && entry.Contains("500", StringComparison.Ordinal));
        AssertClean([await response.Content.ReadAsStringAsync()]);
    }

    [Fact]
    public async Task ARefusedFile_IsLoggedAsRefused_WithNoFileName()
    {
        var (client, logs, host) = CreateHost(HttpStatusCode.OK, Envelope(Answer()));
        using var _ = host;
        var content = new MultipartFormDataContent { { new ByteArrayContent("QZBYTES not a pdf"u8.ToArray()), "file", "QZFILE-evil.pdf" } };

        var response = await client.PostAsync(Url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertClean(logs.Entries);
        Assert.Contains(logs.Entries, entry => entry.Contains("Sticker import finished", StringComparison.Ordinal) && entry.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheMetricsCarryOnlyTheOutcome()
    {
        var (client, _, host) = CreateHost(HttpStatusCode.OK, Envelope(Answer()));
        using var _h = host;
        var measurements = new ConcurrentQueue<(string Instrument, double Value, string Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == StickerImportMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}")))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}")))));
        listener.Start();

        await client.PostAsync(Url, Form());

        Assert.Contains(measurements, measurement => measurement is { Instrument: "sticker_extractions", Value: 1, Tags: "outcome=filled" });
        Assert.Contains(measurements, measurement => measurement is { Instrument: "sticker_extraction_duration", Tags: "outcome=filled" });
        Assert.All(measurements, measurement => Assert.StartsWith("outcome=", measurement.Tags));
        Assert.Equal("MyGarage.StickerImport", StickerImportMetrics.MeterName);
    }
}

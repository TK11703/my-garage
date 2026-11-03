using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Drafts;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.Tests.EmailIntake;

// FR-029 across the whole feature: intake, processing, accept, discard, and expiry never write the sender, subject, text, file names,
// attachment bytes, extracted values, or exception messages to logs, metric tags, or the stored failure reason.
// Shares the Logging collection because hosts replace the static Serilog logger and must not overlap.
[Collection("Logging")]
public class LogContentTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Owner = "qz-owner-oid";
    private const string Sender = "qzsender@contoso.test";

    // Distinctive text and numbers that cannot appear in a log by accident.
    private static readonly string[] Markers =
    [
        "QZSENDER", "QZSUBJECT", "QZBODY", "QZFILE", "QZBYTES", "QZSHOP", "QZDESC", "QZPART", "QZNOTES", "QZMAKE", "QZMODEL",
        "QZLEAK", "QZMSGID", "8123.45", "7234.56",
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

    private static ExtractionResult ServiceRecord(string marker) =>
        new()
        {
            Kind = ExtractionKind.ServiceRecord,
            Confidence = 0.9m,
            Notes = $"QZNOTES {marker}",
            Date = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd"),
            OdometerReading = 48210,
            ShopOrPerson = $"QZSHOP {marker}",
            LineItems =
            [
                new ExtractedLineItem($"QZDESC {marker}", null, 1m, 7234.56m, [new ExtractedPart($"QZPART {marker}", 1m, 8123.45m)]),
            ],
        };

    private static IntakeEmailRequest Email(string messageId, bool withFile)
    {
        var email = IntakeClient.NewEmail(Sender.ToUpperInvariant(), $"<QZMSGID-{messageId}@mail.test>", "QZSUBJECT Fwd: invoice", "QZBODY invoice text");
        return withFile
            ? email
                .AddAttachment("QZFILE-invoice.pdf", "application/pdf", [.. IntakeClient.PdfBytes, .. "QZBYTES"u8.ToArray()])
                .AddAttachment("QZFILE-fake.pdf", "application/pdf", "QZBYTES not a pdf"u8.ToArray())
            : email;
    }

    private async Task<Guid> DraftOfAsync(Guid submissionId) =>
        (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM maintenance_drafts WHERE submission_id = @id", ("id", submissionId)))!;

    private async Task<Guid> SubmissionOfAsync(string messageId) =>
        (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM email_submissions WHERE message_id = @id", ("id", $"<QZMSGID-{messageId}@mail.test>")))!;

    [Fact]
    public async Task IntakeProcessingAcceptDiscardAndExpiry_KeepEmailContentOutOfLogsMetricsAndFailureReasons()
    {
        await factory.InsertUserProfileAsync(Owner, Sender);
        await factory.InsertVehicleAsync(Owner, "QZMAKE", "QZMODEL", 2020);
        var provider = new CapturingLoggerProvider();
        var tags = new ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == EmailIntakeMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, measurementTags, _) => RecordTags(tags, measurementTags));
        listener.SetMeasurementEventCallback<double>((_, _, measurementTags, _) => RecordTags(tags, measurementTags));
        listener.Start();

        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var workflow = logged.CreateWorkflowClient();
        var user = logged.CreateUserClient(Owner, Sender);
        var processor = logged.Services.GetRequiredService<SubmissionProcessor>();
        var expiry = logged.Services.GetRequiredService<DraftExpiryService>();
        // Proves the host writes to the capturing provider, so an empty result below could not hide a leak.
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LogContentTests").LogInformation("capture check");

        // Intake, then processing, of four emails: one to accept, one to discard, one to expire, and one whose extraction throws.
        foreach (var id in new[] { "accept", "discard", "expire" })
        {
            Assert.Equal(HttpStatusCode.Accepted, (await IntakeClient.PostAsync(workflow, Email(id, withFile: true))).StatusCode);
            factory.Extractor.Enqueue(ServiceRecord(id));
            Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        }

        // A repeat, an unknown sender, and a message that is not a valid address are answered without leaking either.
        await IntakeClient.PostAsync(workflow, Email("accept", withFile: true));
        await IntakeClient.PostAsync(workflow, IntakeClient.NewEmail("qzstranger@nowhere.test", "<QZMSGID-stranger@mail.test>", "QZSUBJECT", "QZBODY"));
        await IntakeClient.PostAsync(workflow, new IntakeEmailRequest { MessageId = "<QZMSGID-bad@mail.test>", From = "QZSENDER-not-an-address", Subject = "QZSUBJECT", BodyText = "QZBODY" });

        await IntakeClient.PostAsync(workflow, Email("throws", withFile: false));
        factory.Extractor.EnqueueFailure(new InvalidOperationException("QZLEAK QZBODY QZSUBJECT QZSENDER QZSHOP 8123.45"));
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        var failureReason = (string?)await factory.ExecuteScalarAsync("SELECT failure_reason FROM email_submissions WHERE message_id = @id", ("id", "<QZMSGID-throws@mail.test>"));
        Assert.Equal("service_error", failureReason);

        // Accept the first draft (keeping the original), read the others, then discard one.
        var acceptDraft = await DraftOfAsync(await SubmissionOfAsync("accept"));
        var discardDraft = await DraftOfAsync(await SubmissionOfAsync("discard"));
        var expireSubmission = await SubmissionOfAsync("expire");
        var expireDraft = await DraftOfAsync(expireSubmission);

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/emails")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/notifications")).StatusCode);
        var draft = (await (await user.GetAsync($"/api/drafts/{acceptDraft}")).Content.ReadFromJsonAsync<MaintenanceDraftDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/drafts/{acceptDraft}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsJsonAsync($"/api/drafts/{acceptDraft}/compatibility", draft.Proposal)).StatusCode);

        var accepted = await user.PostAsJsonAsync(
            $"/api/drafts/{acceptDraft}/accept",
            new AcceptDraftRequest { VehicleId = draft.Vehicle.VehicleId, Proposal = draft.Proposal, KeepOriginal = true });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync($"/api/drafts/{discardDraft}/discard", null)).StatusCode);

        // Expire the last one by moving its deadline into the past.
        await factory.ExecuteScalarAsync("UPDATE maintenance_drafts SET created_at = now() - interval '40 days', expires_at = now() - interval '1 day' WHERE id = @id", ("id", expireDraft));
        Assert.True(await expiry.RunOnceAsync(CancellationToken.None) >= 1);

        var entries = provider.Entries.ToArray();
        Assert.Contains(entries, entry => entry.Contains("capture check", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("accepted as", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("discarded", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("Expired", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("finished as", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("threw", StringComparison.Ordinal) && entry.Contains("ExceptionType=InvalidOperationException", StringComparison.Ordinal));
        Assert.NotEmpty(tags);

        foreach (var marker in Markers)
        {
            Assert.DoesNotContain(entries, entry => entry.Contains(marker, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(tags, tag => tag.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(entries, entry => entry.Contains(Sender, StringComparison.OrdinalIgnoreCase));
    }

    private static void RecordTags(ConcurrentQueue<string> tags, ReadOnlySpan<KeyValuePair<string, object?>> measurementTags)
    {
        foreach (var tag in measurementTags)
        {
            tags.Enqueue($"{tag.Key}={tag.Value}");
        }
    }
}

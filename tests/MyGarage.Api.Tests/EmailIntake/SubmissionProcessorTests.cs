using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.EmailIntake;

public class SubmissionProcessorTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ExtractionResult ServiceRecord(VehicleHint? hint = null) =>
        new()
        {
            Kind = ExtractionKind.ServiceRecord,
            Confidence = 0.87m,
            Notes = "Oil change invoice",
            Date = "2026-10-12",
            OdometerReading = 48210,
            ShopOrPerson = "Main St Garage",
            VehicleHint = hint,
            LineItems = [new ExtractedLineItem("Oil change", 59.99m, null, null, null)],
        };

    private async Task<string?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters) =>
        (await factory.ExecuteScalarAsync(sql, parameters))?.ToString();

    private sealed class CapturingLogger : ILogger<SubmissionProcessor>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            Entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }
    }

    [Fact]
    public async Task ATextOnlyServiceRecord_CreatesOneNeedsReviewDraft_WithAMatchedVehicleAndANotification_AndNoRecord()
    {
        const string vin = "1FTEW1EP0JKD00001";
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, vin);
        var submission = await factory.InsertSubmissionAsync(Alice, subject: "Fwd: Main St Garage invoice", bodyText: "Oil change, 48210 miles");
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint(vin, 2018, "Ford", "F-150", 48210)));
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);

        var processed = await factory.CreateProcessor(clock).ProcessOnceAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(
            $"{Alice}|needs_review|service_record|{vehicle}|vin|0.87|{submission}",
            await ScalarAsync("SELECT owner_id || '|' || state || '|' || kind || '|' || vehicle_id || '|' || vehicle_match || '|' || confidence || '|' || submission_id FROM maintenance_drafts"));
        Assert.Null(await ScalarAsync("SELECT attachment_id FROM maintenance_drafts"));
        using var proposal = JsonDocument.Parse((await ScalarAsync("SELECT proposal::text FROM maintenance_drafts"))!);
        Assert.Equal("service_record", proposal.RootElement.GetProperty("kind").GetString());
        var section = proposal.RootElement.GetProperty("serviceRecord");
        Assert.Equal("2026-10-12", section.GetProperty("dateServiced").GetString());
        Assert.Equal(48210, section.GetProperty("odometerReading").GetInt32());
        Assert.Equal(59.99m, section.GetProperty("lineItems")[0].GetProperty("flatCost").GetDecimal());
        Assert.Equal("Oil change invoice", await ScalarAsync("SELECT notes FROM maintenance_drafts"));
        Assert.Equal(clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds(), new DateTimeOffset((DateTime)(await factory.ExecuteScalarAsync("SELECT expires_at FROM maintenance_drafts"))!).ToUnixTimeSeconds());

        Assert.Equal($"{Alice}|draft_ready|New draft service record ready to review", await ScalarAsync("SELECT owner_id || '|' || kind || '|' || title FROM notifications"));
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);

        // An email never creates a vehicle or any record.
        Assert.Equal(1L, await factory.CountRowsAsync("vehicles"));
        foreach (var table in new[] { "service_records", "service_line_items", "fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes" })
        {
            Assert.Equal(0L, await factory.CountRowsAsync(table));
        }

        var call = Assert.Single(factory.Extractor.Calls);
        Assert.Equal("Fwd: Main St Garage invoice", call.Subject);
        Assert.Equal("Oil change, 48210 miles", call.Text);
        Assert.Null(call.Document);
    }

    [Fact]
    public async Task ANoneResult_MakesTheSubmissionNotApplicable_WithNoDraftOrNotification()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.95m });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(0L, await factory.CountRowsAsync("notifications"));
        Assert.Null(await ScalarAsync("SELECT body_text FROM email_submissions"));
    }

    [Fact]
    public async Task WithNothingDue_ProcessOnceReturnsFalse_AndNeverCallsTheExtractor()
    {
        await factory.InsertSubmissionAsync(Alice, nextAttemptAt: DateTimeOffset.UtcNow.AddHours(1));
        await factory.InsertSubmissionAsync(Alice, status: "ready");

        Assert.False(await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None));
        Assert.Empty(factory.Extractor.Calls);
    }

    [Fact]
    public async Task AFailingExtraction_IsRetriedAfterOneThenFiveMinutes_AndThenFails()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        factory.Extractor.EnqueueFailure();
        factory.Extractor.Enqueue(ExtractionResult.Failed("rate_limited"));
        factory.Extractor.Enqueue(ExtractionResult.Failed("invalid_output"));
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var processor = factory.CreateProcessor(clock);

        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        var first = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("queued", first.Status);
        Assert.Equal(1, first.Attempts);
        Assert.Equal("service_error", first.FailureReason);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds(), first.NextAttemptAt.ToUnixTimeSeconds());
        Assert.False(await processor.ProcessOnceAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        var second = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("queued", second.Status);
        Assert.Equal(2, second.Attempts);
        Assert.Equal("rate_limited", second.FailureReason);
        Assert.Equal(clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds(), second.NextAttemptAt.ToUnixTimeSeconds());

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        var third = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("failed", third.Status);
        Assert.Equal(3, third.Attempts);
        Assert.Equal("invalid_output", third.FailureReason);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(3, factory.Extractor.Calls.Count);
        Assert.False(await processor.ProcessOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AProcessingSubmissionWhoseLeaseExpired_IsPickedUpAgain()
    {
        var submission = await factory.InsertSubmissionAsync(
            Alice, status: "processing", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1), attemptCount: 1, nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-15));
        factory.Extractor.Enqueue(ServiceRecord());

        Assert.True(await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None));

        var state = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("ready", state.Status);
        Assert.Equal(2, state.Attempts);
        Assert.Equal(1L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task AProcessingSubmissionWithALiveLease_IsLeftAlone()
    {
        await factory.InsertSubmissionAsync(Alice, status: "processing", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5), attemptCount: 1);

        Assert.False(await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None));
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task WithExtractionDisabledAndNoAttachments_TheSubmissionIsNotApplicable_WithoutCallingTheExtractor()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        factory.Extractor.IsEnabled = false;

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.Empty(factory.Extractor.Calls);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task Drafts_AndNotifications_GoOnlyToTheSubmissionsOwner_AndNeverLinkAnotherUsersVehicle()
    {
        const string bobsVin = "2HGFC2F69LH500002";
        var bobsVehicle = await factory.InsertVehicleAsync(Bob, "Honda", "Civic", 2020, bobsVin);
        var aliceSubmission = await factory.InsertSubmissionAsync(Alice);
        var bobSubmission = await factory.InsertSubmissionAsync(Bob);
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint(bobsVin, 2020, "Honda", "Civic", null)));
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint(bobsVin, 2020, "Honda", "Civic", null)));
        var processor = factory.CreateProcessor();

        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(
            "none|",
            await ScalarAsync("SELECT vehicle_match || '|' || coalesce(vehicle_id::text, '') FROM maintenance_drafts WHERE owner_id = @owner", ("owner", Alice)));
        Assert.Equal(
            $"vin|{bobsVehicle}",
            await ScalarAsync("SELECT vehicle_match || '|' || vehicle_id FROM maintenance_drafts WHERE owner_id = @owner", ("owner", Bob)));
        Assert.Equal(aliceSubmission.ToString(), await ScalarAsync("SELECT submission_id FROM maintenance_drafts WHERE owner_id = @owner", ("owner", Alice)));
        Assert.Equal(bobSubmission.ToString(), await ScalarAsync("SELECT submission_id FROM maintenance_drafts WHERE owner_id = @owner", ("owner", Bob)));
        Assert.Equal(1L, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM notifications WHERE owner_id = @owner", ("owner", Alice)))!);
        Assert.Equal(1L, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM notifications WHERE owner_id = @owner", ("owner", Bob)))!);
    }

    [Fact]
    public async Task Logging_CarriesIdsCountsAndStatusesOnly()
    {
        await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, "1FTEW1EP0JKD00009");
        await factory.InsertSubmissionAsync(Alice, subject: "QZSUBJECT", bodyText: "QZBODY");
        await factory.InsertSubmissionAsync(Alice, subject: "QZSUBJECT2", bodyText: "QZBODY2");
        await factory.InsertSubmissionAsync(Alice, subject: "QZSUBJECT3", bodyText: "QZBODY3");
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint("1FTEW1EP0JKD00009", null, "QZMAKE", null, null)) with { ShopOrPerson = "QZSHOP", Notes = "QZNOTES" });
        factory.Extractor.EnqueueFailure(new InvalidOperationException("QZEXCEPTION with QZBODY2"));
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Notes = "QZNONE" });
        var logger = new CapturingLogger();
        var processor = factory.CreateProcessor(logger: logger);

        while (await processor.ProcessOnceAsync(CancellationToken.None))
        {
        }

        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("QZ", StringComparison.Ordinal) || entry.Contains("alice", StringComparison.OrdinalIgnoreCase) || entry.Contains("contoso", StringComparison.OrdinalIgnoreCase));
    }

    // Attachments -------------------------------------------------------------------------------------------------

    private const string SkippedOne = "[{\"fileName\":\"archive.zip\",\"reason\":\"unsupported_type\"}]";
    private const string SkippedTwo = "[{\"fileName\":\"archive.zip\",\"reason\":\"unsupported_type\"},{\"fileName\":\"notes.txt\",\"reason\":\"unsupported_type\"}]";

    private static readonly byte[] Pdf = IntakeClient.PdfBytes;
    private static readonly byte[] Png = IntakeClient.PngBytes;

    // Draft states and kinds in attachment order, e.g. "needs_review:service_record,manual_entry:null".
    private Task<string?> DraftSummaryAsync() =>
        ScalarAsync("SELECT string_agg(state || ':' || coalesce(kind, 'null'), ',' ORDER BY attachment_id) FROM maintenance_drafts");

    private async Task<(Guid Submission, Guid Attachment)> SeedPdfAsync(string ownerId = Alice, string? bodyText = "Oil change today", int attemptCount = 0, string skipped = "[]")
    {
        var submission = await factory.InsertSubmissionAsync(ownerId, bodyText: bodyText, attemptCount: attemptCount, skippedAttachmentsJson: skipped);
        var attachment = await factory.InsertAttachmentAsync(submission, ownerId, "invoice.pdf", "application/pdf", Pdf);
        return (submission, attachment);
    }

    [Fact]
    public async Task AStoredPdf_IsSentToTheExtractor_AndMakesOneDraftLinkedToThatFile_WithTheTextAsContextOnly()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        var (submission, attachment) = await SeedPdfAsync(bodyText: "Forwarding this invoice");
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint("1FTEW1EP0JKD00001", null, null, null, null)));

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        var call = Assert.Single(factory.Extractor.Calls);
        Assert.Equal("Forwarding this invoice", call.Text);
        var document = Assert.Single(factory.Extractor.Documents);
        Assert.Equal(("application/pdf", "invoice.pdf"), (document.ContentType, document.FileName));
        Assert.Equal(Pdf, document.Content);

        Assert.Equal(1L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(
            $"needs_review|service_record|{attachment}|{vehicle}|vin",
            await ScalarAsync("SELECT state || '|' || kind || '|' || attachment_id || '|' || vehicle_id || '|' || vehicle_match FROM maintenance_drafts"));
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.True(await factory.IntakeBlobExistsAsync(submission, attachment));
    }

    // An upload as the upload endpoint queues it: no sender, subject, or text, one stored file, and optionally the vehicle chosen by the owner.
    private async Task<(Guid Submission, Guid Attachment)> SeedUploadAsync(Guid? preselectedVehicleId, string ownerId = Alice)
    {
        var submission = await factory.InsertSubmissionAsync(ownerId, subject: null, bodyText: null, origin: "upload", preselectedVehicleId: preselectedVehicleId);
        var attachment = await factory.InsertAttachmentAsync(submission, ownerId, "invoice.pdf", "application/pdf", Pdf);
        return (submission, attachment);
    }

    [Fact]
    public async Task AnUploadWithAPreselectedVehicle_MakesADraftForThatVehicle_EvenWhenTheFileNamesAnother()
    {
        var chosen = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2020, "2HGFC2F69LH500002");
        var (submission, attachment) = await SeedUploadAsync(chosen);
        factory.Extractor.Enqueue(ServiceRecord(new VehicleHint("2HGFC2F69LH500002", null, null, null, null)));

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(
            $"needs_review|service_record|{attachment}|{chosen}|selected",
            await ScalarAsync("SELECT state || '|' || kind || '|' || attachment_id || '|' || vehicle_id || '|' || vehicle_match FROM maintenance_drafts"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Null(factory.Extractor.Calls.Single().Text);
    }

    [Theory]
    [InlineData("content_filter")]
    [InlineData("low_confidence")]
    public async Task AnUploadedFileThatCannotBeRead_BecomesAManualEntryDraft_WithTheOriginalKept(string scenario)
    {
        var (submission, attachment) = await SeedUploadAsync(null);
        factory.Extractor.Enqueue(scenario == "content_filter"
            ? ExtractionResult.Failed("content_filter")
            : new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.1m });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("manual_entry:null", await DraftSummaryAsync());
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.True(await factory.IntakeBlobExistsAsync(submission, attachment));
    }

    [Fact]
    public async Task AnUploadWithAPreselectedVehicle_KeepsItOnAManualEntryDraftToo()
    {
        var chosen = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018);
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2020);
        await SeedUploadAsync(chosen);
        factory.Extractor.IsEnabled = false;

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("manual_entry:null", await DraftSummaryAsync());
        Assert.Equal($"{chosen}|selected", await ScalarAsync("SELECT vehicle_id || '|' || vehicle_match FROM maintenance_drafts"));
    }

    [Fact]
    public async Task AnUploadWhosePreselectedVehicleWasDeleted_FallsBackToTheNormalMatching()
    {
        var gone = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018);
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2020);
        await factory.InsertVehicleAsync(Alice, "Subaru", "Outback", 2019);
        await SeedUploadAsync(gone);
        await factory.ExecuteScalarAsync("DELETE FROM vehicles WHERE id = @id", ("id", gone));
        factory.Extractor.Enqueue(ServiceRecord());

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("none", await ScalarAsync("SELECT vehicle_match FROM maintenance_drafts"));
        Assert.Null(await ScalarAsync("SELECT vehicle_id FROM maintenance_drafts"));
    }

    [Fact]
    public async Task TwoAttachments_MakeTwoDrafts_AndTheTextIsNeverAThirdSource()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, bodyText: "Two invoices attached");
        var first = await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        var second = await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.Enqueue(ServiceRecord());
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.Fuel, Confidence = 0.7m, Date = "2026-10-01", TotalCost = 40m });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2, factory.Extractor.Calls.Count);
        Assert.Equal(["one.pdf", "two.png"], factory.Extractor.Documents.Select(document => document.FileName));
        Assert.Equal(2L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal("needs_review:service_record,needs_review:fuel", await DraftSummaryAsync());
        Assert.Equal($"{first},{second}", await ScalarAsync("SELECT string_agg(attachment_id::text, ',' ORDER BY attachment_id) FROM maintenance_drafts"));
        Assert.Equal(2L, await factory.CountRowsAsync("notifications"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("0.95")]
    public async Task ANoneAnswerWithConfidenceAtOrAboveTheFloor_MakesNoDraftAndNoNotification_AndDropsTheFile(string confidence)
    {
        var (submission, attachment) = await SeedPdfAsync();
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture) });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(0L, await factory.CountRowsAsync("notifications"));
        Assert.False(await factory.IntakeBlobExistsAsync(submission, attachment));
    }

    [Fact]
    public async Task OnlyTheAttachmentThatIsAReceipt_MakesADraft_AndTheOthersFileIsDropped()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, bodyText: "Receipt and a photo");
        var receipt = await factory.InsertAttachmentAsync(submission, Alice, "receipt.pdf", "application/pdf", Pdf);
        var photo = await factory.InsertAttachmentAsync(submission, Alice, "photo.png", "image/png", Png);
        factory.Extractor.Enqueue(ServiceRecord());
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.9m });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("needs_review:service_record", await DraftSummaryAsync());
        Assert.Equal(receipt.ToString(), await ScalarAsync("SELECT attachment_id FROM maintenance_drafts"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.True(await factory.IntakeBlobExistsAsync(submission, receipt));
        Assert.False(await factory.IntakeBlobExistsAsync(submission, photo));
    }

    public static TheoryData<string> ManualEntryCases => new() { "low_confidence", "no_confidence", "content_filter", "disabled", "failed_third_attempt" };

    [Theory]
    [MemberData(nameof(ManualEntryCases))]
    public async Task WhenTheFileCannotBeRead_TheOwnerStillGetsAManualEntryDraft_WithTheOriginalKept(string scenario)
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        var (submission, attachment) = await SeedPdfAsync(attemptCount: scenario == "failed_third_attempt" ? 2 : 0);
        switch (scenario)
        {
            case "low_confidence":
                factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.1m });
                break;
            case "no_confidence":
                factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None });
                break;
            case "content_filter":
                factory.Extractor.Enqueue(ExtractionResult.Failed("content_filter"));
                break;
            case "disabled":
                factory.Extractor.IsEnabled = false;
                break;
            default:
                factory.Extractor.Enqueue(ExtractionResult.Failed("rate_limited"));
                break;
        }

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(scenario == "disabled" ? 0 : 1, factory.Extractor.Calls.Count);
        Assert.Equal("manual_entry:null", await DraftSummaryAsync());
        Assert.Equal(
            $"{attachment}|only_vehicle|{vehicle}|{{}}",
            await ScalarAsync("SELECT attachment_id || '|' || vehicle_match || '|' || vehicle_id || '|' || proposal::text FROM maintenance_drafts"));
        Assert.Equal("New draft maintenance item ready to review", await ScalarAsync("SELECT title FROM notifications"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.True(await factory.IntakeBlobExistsAsync(submission, attachment));
    }

    [Fact]
    public async Task AManualEntryDraft_UsesAVehicleHintFromALowConfidenceAnswer()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2020, "2HGFC2F69LH500002");
        var (_, _) = await SeedPdfAsync();
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.1m, VehicleHint = new VehicleHint("1FTEW1EP0JKD00001", null, null, null, null) });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal($"{vehicle}|vin", await ScalarAsync("SELECT vehicle_id || '|' || vehicle_match FROM maintenance_drafts"));
    }

    [Fact]
    public async Task WhenEveryAttachmentFailsTransiently_TheSubmissionIsRetried_WithNoDraftsAndTheFilesKept()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        var first = await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        var second = await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.EnqueueFailure();
        factory.Extractor.Enqueue(ExtractionResult.Failed("rate_limited"));
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);

        await factory.CreateProcessor(clock).ProcessOnceAsync(CancellationToken.None);

        var state = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("queued", state.Status);
        Assert.Equal(1, state.Attempts);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds(), state.NextAttemptAt.ToUnixTimeSeconds());
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(0L, await factory.CountRowsAsync("notifications"));
        Assert.True(await factory.IntakeBlobExistsAsync(submission, first));
        Assert.True(await factory.IntakeBlobExistsAsync(submission, second));
    }

    [Fact]
    public async Task AfterTheThirdFailedAttempt_EveryAttachmentBecomesAManualEntryDraft()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, attemptCount: 2);
        await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.Enqueue(ExtractionResult.Failed("service_error"));
        factory.Extractor.EnqueueFailure();

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("manual_entry:null,manual_entry:null", await DraftSummaryAsync());
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
    }

    [Fact]
    public async Task APartialSuccess_MakesDraftsForTheSuccesses_AndManualEntryForTheRest_WithoutRetrying()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.Enqueue(ServiceRecord());
        factory.Extractor.Enqueue(ExtractionResult.Failed("service_error"));

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("needs_review:service_record,manual_entry:null", await DraftSummaryAsync());
        var state = await factory.ReadSubmissionStateAsync(submission);
        Assert.Equal("ready", state.Status);
        Assert.Equal(1, state.Attempts);
        Assert.Equal(2L, await factory.CountRowsAsync("notifications"));
    }

    [Fact]
    public async Task AFailureMixedWithAnOtherwiseSettledAttachment_IsNotRetried()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.Enqueue(new ExtractionResult { Kind = ExtractionKind.None, Confidence = 0.9m });
        factory.Extractor.Enqueue(ExtractionResult.Failed("service_error"));

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("manual_entry:null", await DraftSummaryAsync());
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(submission)).Status);
    }

    [Fact]
    public async Task SkippedAttachments_AreReportedInEachDraftsNotes_AfterTheModelsOwnRemark()
    {
        var (_, _) = await SeedPdfAsync(skipped: SkippedOne);
        factory.Extractor.Enqueue(ServiceRecord());

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Oil change invoice 1 attachment was skipped: unsupported type", await ScalarAsync("SELECT notes FROM maintenance_drafts"));
    }

    [Fact]
    public async Task SeveralSkippedAttachments_AreCounted_AndAppendedToEveryDraft_IncludingManualEntryOnes()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, skippedAttachmentsJson: SkippedTwo);
        await factory.InsertAttachmentAsync(submission, Alice, "one.pdf", "application/pdf", Pdf);
        await factory.InsertAttachmentAsync(submission, Alice, "two.png", "image/png", Png);
        factory.Extractor.Enqueue(ServiceRecord());
        factory.Extractor.Enqueue(ExtractionResult.Failed("content_filter"));

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(
            "Oil change invoice 2 attachments were skipped: unsupported type||2 attachments were skipped: unsupported type",
            await ScalarAsync("SELECT string_agg(notes, '||' ORDER BY attachment_id) FROM maintenance_drafts"));
    }

    [Fact]
    public async Task TheSkippedNote_FitsTheFiveHundredCharacterLimit_BySheddingTheModelsRemarkFirst()
    {
        var (_, _) = await SeedPdfAsync(skipped: SkippedOne);
        factory.Extractor.Enqueue(ServiceRecord() with { Notes = new string('n', 700) });

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        var notes = (await ScalarAsync("SELECT notes FROM maintenance_drafts"))!;
        Assert.Equal(500, notes.Length);
        Assert.EndsWith(" 1 attachment was skipped: unsupported type", notes);
    }

    [Fact]
    public async Task ASubmissionWithOnlyUnsupportedFilesAndNoText_IsNotApplicable_WithoutCallingTheExtractor()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, bodyText: "  ", skippedAttachmentsJson: SkippedOne);

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.Empty(factory.Extractor.Calls);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task ATextOnlyDraft_AlsoReportsTheSkippedFiles()
    {
        await factory.InsertSubmissionAsync(Alice, skippedAttachmentsJson: SkippedOne);
        factory.Extractor.Enqueue(ServiceRecord());

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Oil change invoice 1 attachment was skipped: unsupported type", await ScalarAsync("SELECT notes FROM maintenance_drafts"));
        Assert.Null(await ScalarAsync("SELECT attachment_id FROM maintenance_drafts"));
    }

    [Fact]
    public async Task ABlobWhoseBytesAreNoLongerThatFileType_IsRefused_NotSentAndNotDrafted()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, bodyText: null);
        var tampered = await factory.InsertAttachmentAsync(submission, Alice, "tampered.pdf", "application/pdf", "<html>not a pdf</html>"u8.ToArray());
        var retyped = await factory.InsertAttachmentAsync(submission, Alice, "retyped.pdf", "application/pdf", Png);

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Empty(factory.Extractor.Calls);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
        Assert.False(await factory.IntakeBlobExistsAsync(submission, tampered));
        Assert.False(await factory.IntakeBlobExistsAsync(submission, retyped));
    }

    [Fact]
    public async Task ARefusedFile_DoesNotStopTheValidOne_AndIsCountedAsSkipped()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        await factory.InsertAttachmentAsync(submission, Alice, "tampered.pdf", "application/pdf", "plain text"u8.ToArray());
        await factory.InsertAttachmentAsync(submission, Alice, "receipt.png", "image/png", Png);
        factory.Extractor.Enqueue(ServiceRecord());

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(["receipt.png"], factory.Extractor.Documents.Select(document => document.FileName));
        Assert.Equal("needs_review:service_record", await DraftSummaryAsync());
        Assert.Equal("Oil change invoice 1 attachment was skipped: unsupported type", await ScalarAsync("SELECT notes FROM maintenance_drafts"));
    }

    [Fact]
    public async Task AnAttachmentWhoseBlobIsMissing_IsSkipped_NotSentAndNotDrafted()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, bodyText: null);
        await factory.InsertAttachmentAsync(submission, Alice, "gone.pdf", "application/pdf", content: null);

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Empty(factory.Extractor.Calls);
        Assert.Equal("not_applicable", (await factory.ReadSubmissionStateAsync(submission)).Status);
    }

    [Fact]
    public async Task AnAttachmentRowOfAnotherOwner_IsNeverReadOrSent()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, subject: null, bodyText: null);
        await factory.InsertAttachmentAsync(submission, Bob, "bobs.pdf", "application/pdf", Pdf);

        await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None);

        Assert.Empty(factory.Extractor.Calls);
        Assert.Equal(0L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task Logging_WithAttachments_StillCarriesIdsCountsAndStatusesOnly()
    {
        var submission = await factory.InsertSubmissionAsync(Alice, subject: "QZSUBJECT", bodyText: "QZBODY", skippedAttachmentsJson: "[{\"fileName\":\"QZSKIPPED.zip\",\"reason\":\"unsupported_type\"}]");
        await factory.InsertAttachmentAsync(submission, Alice, "QZFILE.pdf", "application/pdf", Pdf);
        await factory.InsertAttachmentAsync(submission, Alice, "QZOTHER.png", "image/png", Png);
        await factory.InsertAttachmentAsync(submission, Alice, "QZBAD.pdf", "application/pdf", "QZ text"u8.ToArray());
        factory.Extractor.Enqueue(ServiceRecord() with { Notes = "QZNOTES" });
        factory.Extractor.EnqueueFailure(new InvalidOperationException("QZEXCEPTION QZFILE.pdf"));
        var logger = new CapturingLogger();

        await factory.CreateProcessor(logger: logger).ProcessOnceAsync(CancellationToken.None);

        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("QZ", StringComparison.Ordinal) || entry.Contains("contoso", StringComparison.OrdinalIgnoreCase));
    }
}

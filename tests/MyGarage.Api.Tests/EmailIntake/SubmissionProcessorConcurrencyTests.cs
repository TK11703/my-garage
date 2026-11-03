using MyGarage.Api.EmailIntake;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.EmailIntake;

public class SubmissionProcessorConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TwoProcessorsOverTwentySubmissions_ProcessEachExactlyOnce()
    {
        const int count = 20;
        var submissions = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            submissions.Add(await factory.InsertSubmissionAsync("alice-oid", nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-count + i)));
        }

        factory.Extractor.DefaultResult = new ExtractionResult
        {
            Kind = ExtractionKind.ServiceRecord,
            Confidence = 0.8m,
            Date = "2026-10-12",
            OdometerReading = 1000,
            LineItems = [new ExtractedLineItem("Service", 10m, null, null, null)],
        };

        static async Task<int> RunUntilIdleAsync(SubmissionProcessor processor)
        {
            var handled = 0;
            while (await processor.ProcessOnceAsync(CancellationToken.None))
            {
                handled++;
            }

            return handled;
        }

        var handledCounts = await Task.WhenAll(RunUntilIdleAsync(factory.CreateProcessor()), RunUntilIdleAsync(factory.CreateProcessor()));

        Assert.Equal(count, handledCounts.Sum());
        Assert.Equal(count, factory.Extractor.Calls.Count);
        Assert.Equal((long)count, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal((long)count, await factory.CountRowsAsync("notifications"));
        Assert.Equal((long)count, (long)(await factory.ExecuteScalarAsync("SELECT count(DISTINCT submission_id) FROM maintenance_drafts"))!);
        Assert.Equal((long)count, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM email_submissions WHERE status = 'ready' AND attempt_count = 1"))!);
    }
}

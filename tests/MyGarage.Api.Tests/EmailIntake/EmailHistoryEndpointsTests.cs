using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.EmailIntake;

public class EmailHistoryEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    private Task<Guid> SubmissionAsync(string ownerId, int minutes, string status = "ready") =>
        factory.InsertSubmissionAsync(ownerId, status, subject: "QZSUBJECT", bodyText: "QZBODY", nextAttemptAt: T0.AddMinutes(minutes));

    private static async Task<PagedResultDto<EmailHistoryItemDto>> PageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResultDto<EmailHistoryItemDto>>())!;
    }

    [Fact]
    public async Task List_ReturnsTheCallersEmailsNewestFirst_WithOutcomeAndRecordLinks()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, "Ford", "Mustang", 2019);
        var fuelRecord = Guid.CreateVersion7();
        var open = await SubmissionAsync(Alice, 1);
        await factory.InsertDraftAsync(Alice, open, "needs_review", "service_record");
        var included = await SubmissionAsync(Alice, 2);
        await factory.InsertDraftAsync(Alice, included, "included", "fuel", vehicle, createdRecordKind: "fuel", createdRecordId: fuelRecord);
        var discarded = await SubmissionAsync(Alice, 3);
        await factory.InsertDraftAsync(Alice, discarded, "discarded", "tax");
        var expired = await SubmissionAsync(Alice, 4);
        await factory.InsertDraftAsync(Alice, expired, "expired", "tax");
        var notApplicable = await SubmissionAsync(Alice, 5, "not_applicable");
        var failed = await SubmissionAsync(Alice, 6, "failed");

        var page = await PageAsync(await AliceClient().GetAsync("/api/emails"));

        Assert.Equal(6, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal([failed, notApplicable, expired, discarded, included, open], page.Items.Select(item => item.SubmissionId));
        Assert.Equal(
            [
                EmailHistoryOutcome.Failed,
                EmailHistoryOutcome.NotApplicable,
                EmailHistoryOutcome.Expired,
                EmailHistoryOutcome.Discarded,
                EmailHistoryOutcome.Included,
                EmailHistoryOutcome.AwaitingReview,
            ],
            page.Items.Select(item => item.Outcome));
        Assert.Equal(T0.AddMinutes(6), page.Items[0].ArrivedAt);
        var created = Assert.Single(page.Items[4].Records);
        Assert.Equal(DraftKind.Fuel, created.Kind);
        Assert.Equal(vehicle, created.VehicleId);
        Assert.Equal(fuelRecord, created.RecordId);
        Assert.All(page.Items.Where(item => item.Outcome != EmailHistoryOutcome.Included), item => Assert.Empty(item.Records));
    }

    [Fact]
    public async Task List_PagesAndClampsAPageBeyondTheLast_AndBadPagingIs400()
    {
        for (var minute = 1; minute <= 12; minute++)
        {
            await SubmissionAsync(Alice, minute, "not_applicable");
        }

        var first = await PageAsync(await AliceClient().GetAsync("/api/emails"));
        var second = await PageAsync(await AliceClient().GetAsync("/api/emails?page=2"));
        var small = await PageAsync(await AliceClient().GetAsync("/api/emails?pageSize=5&page=3"));
        var beyond = await PageAsync(await AliceClient().GetAsync("/api/emails?page=9"));

        Assert.Equal(10, first.Items.Count);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(2, second.Page);
        Assert.Equal(2, small.Items.Count);
        Assert.Equal(3, small.TotalPages);
        Assert.Equal(2, beyond.Page);
        Assert.Equal(second.Items.Select(item => item.SubmissionId), beyond.Items.Select(item => item.SubmissionId));
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/emails?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/emails?pageSize=51")).StatusCode);
    }

    [Fact]
    public async Task List_WithNoEmails_ReturnsAnEmptyFirstPage()
    {
        var page = await PageAsync(await AliceClient().GetAsync("/api/emails?page=4"));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public async Task List_ShowsOnlyTheCallersEmails()
    {
        var mine = await SubmissionAsync(Alice, 1, "not_applicable");
        await SubmissionAsync(Bob, 2, "not_applicable");
        var bobsOpen = await SubmissionAsync(Bob, 3);
        await factory.InsertDraftAsync(Bob, bobsOpen, "needs_review", "fuel");
        await factory.InsertSubmissionAsync(Alice, "not_accepted", nextAttemptAt: T0.AddMinutes(4));

        var page = await PageAsync(await AliceClient().GetAsync("/api/emails"));

        Assert.Equal([mine], page.Items.Select(item => item.SubmissionId));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task List_NeverIncludesTheSenderSubjectOrText()
    {
        var submission = await SubmissionAsync(Alice, 1);
        await factory.InsertDraftAsync(Alice, submission, "needs_review", "service_record", notes: "QZNOTES");

        var json = await (await AliceClient().GetAsync("/api/emails")).Content.ReadAsStringAsync();

        Assert.Contains(submission.ToString(), json);
        foreach (var leaked in new[] { "QZSUBJECT", "QZBODY", "QZNOTES", "sender@contoso.test", "sender", "subject", "bodyText", "body" })
        {
            Assert.DoesNotContain(leaked, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task List_Is401WithoutAToken_AndRefusesTheWorkflowsAppOnlyToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync("/api/emails")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync("/api/emails")).StatusCode);
        var neither = (await factory.CreateClientWithNeitherRoleNorScope().GetAsync("/api/emails")).StatusCode;
        Assert.True(neither is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"answered {(int)neither}");
    }
}

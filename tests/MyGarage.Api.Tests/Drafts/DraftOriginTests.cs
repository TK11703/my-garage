using System.Net.Http.Json;
using MyGarage.Api.Drafts;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Drafts;

public class DraftOriginTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    // A ready submission with one stored PDF and one open draft, as an email or as an upload would leave it.
    private async Task<Guid> SeedDraftAsync(bool upload)
    {
        var submission = upload
            ? await factory.InsertSubmissionAsync(Alice, "ready", subject: null, bodyText: null, origin: "upload")
            : await factory.InsertSubmissionAsync(Alice, "ready", subject: "Fwd: invoice");
        var attachment = await factory.InsertAttachmentAsync(submission, Alice, content: IntakeClient.PdfBytes);
        return await factory.InsertDraftAsync(Alice, submission, attachmentId: attachment);
    }

    [Fact]
    public async Task TheDraftDetail_CarriesItsOrigin_AndAnUploadsEmailBlockHoldsOnlyTheUploadTime()
    {
        var upload = await SeedDraftAsync(upload: true);
        var email = await SeedDraftAsync(upload: false);

        var uploaded = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{upload}"))!;
        var emailed = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{email}"))!;

        Assert.Equal(DraftOrigin.Upload, uploaded.Origin);
        Assert.NotNull(uploaded.Email);
        Assert.Null(uploaded.Email.Sender);
        Assert.Null(uploaded.Email.Subject);
        Assert.Equal("attachment", uploaded.Source.Kind);
        Assert.Equal(DraftOrigin.Email, emailed.Origin);
        Assert.Equal("Fwd: invoice", emailed.Email!.Subject);
        Assert.Equal("sender@contoso.test", emailed.Email.Sender);
    }

    [Fact]
    public async Task TheDraftList_CarriesTheOriginOfEachRow()
    {
        var upload = await SeedDraftAsync(upload: true);
        var email = await SeedDraftAsync(upload: false);

        var page = (await AliceClient().GetFromJsonAsync<PagedResultDto<MaintenanceDraftSummaryDto>>("/api/drafts"))!;

        Assert.Equal(DraftOrigin.Upload, page.Items.Single(item => item.Id == upload).Origin);
        Assert.Equal(DraftOrigin.Email, page.Items.Single(item => item.Id == email).Origin);
    }

    [Fact]
    public async Task TheHistory_CarriesTheOrigin_AndStillNeverTheSenderSubjectOrText()
    {
        var upload = await factory.InsertSubmissionAsync(Alice, "not_applicable", subject: null, bodyText: null, origin: "upload");
        var email = await factory.InsertSubmissionAsync(Alice, "not_applicable", subject: "QZSUBJECT", bodyText: "QZBODY");

        var response = await AliceClient().GetAsync("/api/emails");
        var json = await response.Content.ReadAsStringAsync();
        var page = (await response.Content.ReadFromJsonAsync<PagedResultDto<EmailHistoryItemDto>>())!;

        Assert.Equal(DraftOrigin.Upload, page.Items.Single(item => item.SubmissionId == upload).Origin);
        Assert.Equal(DraftOrigin.Email, page.Items.Single(item => item.SubmissionId == email).Origin);
        Assert.DoesNotContain("QZSUBJECT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("QZBODY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sender@contoso.test", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("upload", DraftOrigin.Upload)]
    [InlineData("email", DraftOrigin.Email)]
    [InlineData("something-else", DraftOrigin.Email)]
    [InlineData("", DraftOrigin.Email)]
    public void TheMapper_ReadsTheStoredOriginText_AndTreatsAnUnknownOneAsEmail(string text, DraftOrigin expected)
    {
        var summary = new MaintenanceDraftSummaryRecord { Id = Guid.NewGuid(), State = "needs_review", Origin = text };
        var history = new EmailHistoryRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, "awaiting_review", [], text);

        Assert.Equal(expected, DraftMapper.ToSummaryDto(summary).Origin);
        Assert.Equal(expected, DraftMapper.ToHistoryDto(history).Origin);
    }
}

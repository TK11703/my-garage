using System.Net;
using Bunit;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class DraftsListPageTests : DraftTestContext
{
    private IRenderedComponent<DraftsListPage> RenderPage(string uri = "/drafts")
    {
        RegisterDrafts();
        NavigateTo(uri);
        return Render<DraftsListPage>();
    }

    [Fact]
    public void ShowsTheTitleAndTrail()
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Maintenance Drafts", cut.Find("h1").TextContent);
            Assert.Equal(["Home", "Maintenance"], cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void NoDrafts_ShowsTheEmptyState()
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No drafts waiting. Forward a service invoice or receipt to your My Garage mailbox, or upload a file.", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='draft']"));
        });
    }

    [Fact]
    public void TheHeaderHasAnAddMaintenanceLinkToTheUploadPage()
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("a[data-testid='add-maintenance']");
            Assert.Equal("/drafts/upload", link.GetAttribute("href"));
            Assert.Contains("Add maintenance", link.TextContent);
        });
    }

    [Theory]
    [InlineData("1", "1 file uploaded. It is being read; the draft appears below and in your notifications when ready.")]
    [InlineData("3", "3 files uploaded. They are being read; each draft appears below and in your notifications when ready.")]
    public void AnUploadedCount_ShowsAStatusNotice(string count, string expected)
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage($"/drafts?uploaded={count}");

        cut.WaitForAssertion(() =>
        {
            var notice = cut.Find("[data-testid='upload-notice']");
            Assert.Equal("status", notice.GetAttribute("role"));
            Assert.Equal(expected, notice.TextContent.Trim());
        });
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("99999999999")]
    [InlineData("%3Cscript%3E")]
    public void ABadUploadedCount_ShowsNoNoticeAndNeverBreaksThePage(string count)
    {
        DraftList = DraftsPage([DraftSummary()]);

        var cut = RenderPage($"/drafts?uploaded={count}");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='upload-notice']"));
            Assert.Single(cut.FindAll("[data-testid='draft']"));
        });
    }

    [Fact]
    public void Drafts_AreListedInTheOrderReturned_EachLinkingToItsReview()
    {
        var first = DraftSummary(kind: DraftKind.ServiceRecord, vehicleName: "2020 Ford F-150", total: 1234.5m);
        var second = DraftSummary(kind: DraftKind.Inspection, vehicleName: null, total: null);
        DraftList = DraftsPage([first, second]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='draft']");
            Assert.Equal(2, rows.Count);
            Assert.Equal($"/drafts/{first.Id}", rows[0].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("Service record", rows[0].QuerySelector("[data-testid='draft-kind']")!.TextContent.Trim());
            Assert.Equal("$1,234.50", rows[0].QuerySelector("[data-testid='draft-total']")!.TextContent.Trim());
            Assert.Equal("2020 Ford F-150", rows[0].QuerySelector("[data-testid='draft-vehicle']")!.TextContent.Trim());
            Assert.Equal("Arrived Nov 3, 2026", rows[0].QuerySelector("[data-testid='draft-arrived']")!.TextContent.Trim());
            Assert.Equal("Expires Dec 3, 2026", rows[0].QuerySelector("[data-testid='draft-expires']")!.TextContent.Trim());

            Assert.Equal("Inspection", rows[1].QuerySelector("[data-testid='draft-kind']")!.TextContent.Trim());
            Assert.Equal("No vehicle chosen", rows[1].QuerySelector("[data-testid='draft-vehicle']")!.TextContent.Trim());
            Assert.Empty(rows[1].QuerySelectorAll("[data-testid='draft-total']"));
            Assert.Empty(cut.FindAll("table"));
        });
    }

    [Fact]
    public void AnUploadedDraftRow_IsLabelledUploaded_AndAnEmailedOneIsNot()
    {
        var uploaded = DraftSummary(origin: DraftOrigin.Upload);
        var emailed = DraftSummary();
        DraftList = DraftsPage([uploaded, emailed]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='draft']");
            Assert.Equal("Uploaded", rows[0].QuerySelector("[data-testid='draft-origin']")!.TextContent.Trim());
            Assert.Empty(rows[1].QuerySelectorAll("[data-testid='draft-origin']"));
            Assert.Equal("Uploaded Nov 3, 2026", rows[0].QuerySelector("[data-testid='draft-arrived']")!.TextContent.Trim());
            Assert.Equal("Arrived Nov 3, 2026", rows[1].QuerySelector("[data-testid='draft-arrived']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void AnUploadedHistoryRow_IsLabelledUploaded_AndStillShowsItsOutcome()
    {
        HistoryList = HistoryPage([UploadHistoryItem(EmailHistoryOutcome.Discarded), HistoryItem(EmailHistoryOutcome.Discarded)]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='history-item']");
            Assert.Equal("Uploaded", rows[0].QuerySelector("[data-testid='history-origin']")!.TextContent.Trim());
            Assert.Empty(rows[1].QuerySelectorAll("[data-testid='history-origin']"));
            Assert.All(rows, row => Assert.Equal("Discarded", row.QuerySelector("[data-testid='history-outcome']")!.TextContent.Trim()));
        });
    }

    [Fact]
    public void ADraftWaitingForARecordType_SaysSo()
    {
        DraftList = DraftsPage([DraftSummary(state: DraftState.ManualEntry, kind: null, vehicleName: null, total: null)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Choose a record type", cut.Find("[data-testid='draft-kind']").TextContent.Trim());
            Assert.Equal("Needs details", cut.Find("[data-testid='draft-state']").TextContent.Trim());
        });
    }

    [Fact]
    public void Pager_LinksToTheDraftsPages()
    {
        DraftList = DraftsPage([DraftSummary()], page: 2, totalCount: 25);

        var cut = RenderPage("/drafts?page=2");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/drafts", cut.Find("[data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal("/drafts?page=3", cut.Find("[data-testid='pager-next'] a").GetAttribute("href"));
            Assert.Contains(Calls, call => call.Path == "/api/drafts?page=2");
        });
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-4")]
    public void ABadPageValue_MeansThePageOne(string value)
    {
        DraftList = DraftsPage([DraftSummary()]);

        var cut = RenderPage($"/drafts?page={value}");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Calls, call => call.Path == "/api/drafts?page=1");
            Assert.Single(cut.FindAll("[data-testid='draft']"));
        });
    }

    [Fact]
    public void AnUnknownAnswer_FromTheApi_ShowsTheEmptyState_AndNeverThrows()
    {
        DraftList = null;

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("No drafts waiting.", cut.Markup));
    }

    [Fact]
    public void UserTextInADraftRow_IsEncoded()
    {
        DraftList = DraftsPage([DraftSummary(vehicleName: "<b>Truck</b>")]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Contains("<b>Truck</b>", cut.Find("[data-testid='draft-vehicle']").TextContent);
        });
    }

    [Fact]
    public void AServerError_ShowsTheFailureAlert()
    {
        DraftFailure = HttpStatusCode.InternalServerError;

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.Unavailable, cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='draft']"));
        });
    }

    [Fact]
    public void ARejectedSignIn_ShowsTheSignInAgainAction()
    {
        DraftFailure = HttpStatusCode.Unauthorized;

        var cut = RenderPage("/drafts?page=2");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.NotAccepted, cut.Markup);
            Assert.Equal($"/authentication/login?returnUrl={Uri.EscapeDataString("/drafts?page=2")}", cut.Find("a.btn-primary").GetAttribute("href"));
        });
    }

    // ---- History tab ----

    [Fact]
    public void TheTabs_AreLinks_AndTheCurrentOneIsMarked()
    {
        DraftList = DraftsPage([]);
        HistoryList = HistoryPage([]);

        var awaiting = RenderPage();
        awaiting.WaitForAssertion(() =>
        {
            var tabs = awaiting.FindAll("[data-testid='drafts-tabs'] a");
            Assert.Equal(["Awaiting review", "History"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["/drafts", "/drafts?tab=history"], tabs.Select(tab => tab.GetAttribute("href")));
            Assert.Equal("page", tabs[0].GetAttribute("aria-current"));
            Assert.Null(tabs[1].GetAttribute("aria-current"));
            Assert.Empty(awaiting.FindAll("[data-testid='drafts-history']"));
        });
    }

    [Fact]
    public void EachTab_HasAnIcon()
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("[data-testid='drafts-tabs'] a");
            Assert.All(tabs, tab => Assert.NotNull(tab.QuerySelector("svg.bi")));
        });
    }

    [Fact]
    public void TheAwaitingTab_HasNoStatusFilter()
    {
        DraftList = DraftsPage([]);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='history-filter']")));
    }

    [Fact]
    public void TheHistoryTab_OffersAStatusFilter_WithEveryStatusAndAllSelected()
    {
        HistoryList = HistoryPage([HistoryItem()]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            var form = cut.Find("form[data-testid='history-filter']");
            Assert.Equal("get", form.GetAttribute("method"), ignoreCase: true);
            Assert.Equal("history", form.QuerySelector("input[name='tab']")!.GetAttribute("value"));
            Assert.NotNull(form.QuerySelector("label[for='history-status']"));

            var select = form.QuerySelector("select#history-status")!;
            Assert.Equal("status", select.GetAttribute("name"));
            Assert.Equal(
                ["All statuses", "Awaiting review", "Included", "Discarded", "Expired", "Not applicable", "Failed"],
                select.QuerySelectorAll("option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["awaiting_review", "included", "discarded", "expired", "not_applicable", "failed"], select.QuerySelectorAll("option").Skip(1).Select(option => option.GetAttribute("value")));
            Assert.Equal("", select.QuerySelectorAll("option").Single(option => option.HasAttribute("selected")).GetAttribute("value"));
            Assert.Empty(form.QuerySelectorAll("button, a"));
            Assert.Contains("justify-content-end", form.ClassList);
            Assert.True(select.HasAttribute("data-auto-submit"));
        });
    }

    [Fact]
    public void AStatus_IsSentToTheApi_SelectedInTheFilter_AndKeptByTheTabAndPagerLinks()
    {
        HistoryList = HistoryPage([HistoryItem(EmailHistoryOutcome.Included)], page: 2, totalCount: 25);

        var cut = RenderPage("/drafts?tab=history&status=included&page=2");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Calls, call => call.Path == "/api/emails?page=2&outcome=included");
            Assert.Equal("included", cut.Find("select#history-status option[selected]").GetAttribute("value"));
            Assert.Equal("/drafts?tab=history&status=included", cut.Find("[data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal("/drafts?tab=history&status=included&page=3", cut.Find("[data-testid='pager-next'] a").GetAttribute("href"));
            Assert.Equal("/drafts?tab=history", cut.FindAll("[data-testid='drafts-tabs'] a")[1].GetAttribute("href"));
        });
    }

    [Fact]
    public void AnUnknownStatus_MeansAllStatuses()
    {
        HistoryList = HistoryPage([HistoryItem()]);

        var cut = RenderPage("/drafts?tab=history&status=bogus");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(Calls, call => call.Path == "/api/emails?page=1");
        });
    }

    [Fact]
    public void AFilterWithNoMatches_SaysSo()
    {
        HistoryList = HistoryPage([]);

        var cut = RenderPage("/drafts?tab=history&status=failed");

        cut.WaitForAssertion(() => Assert.Contains("Nothing has this status.", cut.Find("[data-testid='drafts-history']").TextContent));
    }

    [Fact]
    public void TheHistoryTab_MarksItselfCurrent_AndShowsOnlyTheHistorySection()
    {
        DraftList = DraftsPage([DraftSummary()]);
        HistoryList = HistoryPage([HistoryItem()]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("[data-testid='drafts-tabs'] a");
            Assert.Null(tabs[0].GetAttribute("aria-current"));
            Assert.Equal("page", tabs[1].GetAttribute("aria-current"));
            Assert.Single(cut.FindAll("[data-testid='history-item']"));
            Assert.Empty(cut.FindAll("[data-testid='drafts-awaiting']"));
            Assert.Empty(cut.FindAll("[data-testid='draft']"));
            Assert.DoesNotContain(Calls, call => call.Path.StartsWith("/api/drafts", StringComparison.Ordinal));
            Assert.Contains(Calls, call => call.Path == "/api/emails?page=1");
        });
    }

    [Fact]
    public void TheAwaitingTab_NeverCallsTheHistoryRoute_AndAnUnknownTabMeansAwaiting()
    {
        DraftList = DraftsPage([DraftSummary()]);
        HistoryList = HistoryPage([HistoryItem()]);

        var cut = RenderPage("/drafts?tab=bogus");

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-testid='draft']"));
            Assert.Empty(cut.FindAll("[data-testid='history-item']"));
            Assert.DoesNotContain(Calls, call => call.Path.StartsWith("/api/emails", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void History_ListsArrivalTimeOutcomeAndLinksToTheCreatedRecords()
    {
        var withFuel = Guid.NewGuid();
        var withService = Guid.NewGuid();
        var withTax = Guid.NewGuid();
        var included = HistoryItem(
            EmailHistoryOutcome.Included,
            new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero),
            new EmailHistoryRecordDto { Kind = DraftKind.ServiceRecord, VehicleId = withService, RecordId = Guid.NewGuid() },
            new EmailHistoryRecordDto { Kind = DraftKind.Energy, VehicleId = withFuel, RecordId = Guid.NewGuid() },
            new EmailHistoryRecordDto { Kind = DraftKind.Tax, VehicleId = withTax, RecordId = Guid.NewGuid() });
        HistoryList = HistoryPage(
        [
            included,
            HistoryItem(EmailHistoryOutcome.AwaitingReview, new DateTimeOffset(2026, 11, 2, 9, 5, 0, TimeSpan.Zero)),
            HistoryItem(EmailHistoryOutcome.Discarded),
            HistoryItem(EmailHistoryOutcome.Expired),
            HistoryItem(EmailHistoryOutcome.NotApplicable),
            HistoryItem(EmailHistoryOutcome.Failed),
        ]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='history-item']");
            Assert.Equal(6, rows.Count);
            Assert.Equal("Nov 3, 2026 at 3:30 PM UTC", rows[0].QuerySelector("[data-testid='history-arrived']")!.TextContent.Trim());
            Assert.Equal("Nov 2, 2026 at 9:05 AM UTC", rows[1].QuerySelector("[data-testid='history-arrived']")!.TextContent.Trim());
            Assert.Equal(
                ["Included", "Awaiting review", "Discarded", "Expired", "Not applicable", "Failed"],
                rows.Select(row => row.QuerySelector("[data-testid='history-outcome']")!.TextContent.Trim()));
            Assert.All(rows, row => Assert.Contains("badge", row.QuerySelector("[data-testid='history-outcome']")!.ClassName));

            var links = rows[0].QuerySelectorAll("a[data-testid='history-record']");
            Assert.Equal(["Service record", "Charge", "Tax"], links.Select(link => link.TextContent.Trim()));
            Assert.Equal(
                [
                    $"/vehicles/{withService}?tab=service-records",
                    $"/vehicles/{withFuel}?tab=service-records&section=fuel",
                    $"/vehicles/{withTax}?tab=service-records&section=compliance",
                ],
                links.Select(link => link.GetAttribute("href")));
            Assert.All(rows.Skip(1), row => Assert.Empty(row.QuerySelectorAll("a[data-testid='history-record']")));
            Assert.Empty(cut.FindAll("table"));
        });
    }

    [Fact]
    public void History_WithNoEmails_ShowsTheEmptyState()
    {
        HistoryList = HistoryPage([]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No emails or uploads yet. Emails you forward to your My Garage mailbox and files you upload appear here.", cut.Find("[data-testid='drafts-history']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='history-item']"));
        });
    }

    [Fact]
    public void History_Pages_WithLinksThatKeepTheTab()
    {
        HistoryList = HistoryPage([HistoryItem()], page: 2, totalCount: 25);

        var cut = RenderPage("/drafts?tab=history&page=2");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/drafts?tab=history", cut.Find("[data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal("/drafts?tab=history&page=3", cut.Find("[data-testid='pager-next'] a").GetAttribute("href"));
            Assert.Contains(Calls, call => call.Path == "/api/emails?page=2");
        });
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public void History_ABadPageValue_MeansThePageOne(string value)
    {
        HistoryList = HistoryPage([HistoryItem()]);

        var cut = RenderPage($"/drafts?tab=history&page={value}");

        cut.WaitForAssertion(() => Assert.Contains(Calls, call => call.Path == "/api/emails?page=1"));
    }

    [Fact]
    public void History_ShowsExactlyWhatTheApiReturnedForThisUser()
    {
        var mine = HistoryItem(EmailHistoryOutcome.Failed);
        HistoryList = HistoryPage([mine]);
        DraftList = DraftsPage([DraftSummary(), DraftSummary()]);

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-testid='history-item']"));
            Assert.Equal("Failed", cut.Find("[data-testid='history-outcome']").TextContent.Trim());
        });
    }

    [Fact]
    public void History_AnUnknownAnswerFromTheApi_ShowsTheEmptyState()
    {
        HistoryList = null;

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() => Assert.Contains("No emails or uploads yet.", cut.Markup));
    }

    [Fact]
    public void History_AServerError_ShowsTheFailureAlert_WithTheTabsStillThere()
    {
        DraftFailure = HttpStatusCode.InternalServerError;

        var cut = RenderPage("/drafts?tab=history");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.Unavailable, cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='history-item']"));
            Assert.Equal(2, cut.FindAll("[data-testid='drafts-tabs'] a").Count);
        });
    }

    [Fact]
    public void History_ARejectedSignIn_ReturnsToTheHistoryTab()
    {
        DraftFailure = HttpStatusCode.Unauthorized;

        var cut = RenderPage("/drafts?tab=history&page=2");

        cut.WaitForAssertion(() =>
            Assert.Equal(
                $"/authentication/login?returnUrl={Uri.EscapeDataString("/drafts?tab=history&page=2")}",
                cut.Find("a.btn-primary").GetAttribute("href")));
    }
}

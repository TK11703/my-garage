using System.Net;
using Bunit;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class MaintenanceViewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int recordsPage = 1)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.RecordsPage, recordsPage));
    }

    [Fact]
    public void RendersAnAccordionWithServiceRecordsInspectionsAndFuelData_InThatOrder()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll(".accordion-item .accordion-header button");
            Assert.Equal(3, buttons.Count);
            Assert.StartsWith("Service Records", buttons[0].TextContent.Trim());
            Assert.Equal("Inspections", buttons[1].TextContent.Trim());
            Assert.Equal("Charging and Fueling", buttons[2].TextContent.Trim());
        });
    }

    [Fact]
    public void TheAccordionItemsAreCollapsibleAndOpenIndependently()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            // Independent items: no item names a parent, so opening one never closes another.
            Assert.Empty(cut.FindAll("[data-bs-parent]"));

            var accordion = cut.Find(".accordion");
            Assert.Contains("gap-3", accordion.ClassList);
            Assert.All(cut.FindAll(".accordion-item"), item =>
            {
                Assert.Contains("border", item.ClassList);
                Assert.Contains("rounded", item.ClassList);
            });

            foreach (var button in cut.FindAll(".accordion-header button"))
            {
                Assert.Equal("collapse", button.GetAttribute("data-bs-toggle"));
                var target = button.GetAttribute("data-bs-target")!;
                Assert.Equal(target.TrimStart('#'), button.GetAttribute("aria-controls"));
                Assert.NotNull(cut.Find(".accordion-collapse" + target));
            }

            Assert.Contains("show", cut.Find("#maintenance-service-records").ClassList);
            Assert.DoesNotContain("show", cut.Find("#maintenance-inspections").ClassList);
            Assert.DoesNotContain("show", cut.Find("#maintenance-fuel-data").ClassList);
        });
    }

    [Fact]
    public void InspectionsAndFuelData_ArePlaceholders()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("future update", cut.Find("#maintenance-inspections .accordion-body").TextContent);
            Assert.Contains("future update", cut.Find("#maintenance-fuel-data .accordion-body").TextContent);
        });
    }

    [Fact]
    public void NoRecords_ShowsTheEmptyStateAndAnAddAction()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No service records yet", cut.Markup);
            var add = cut.Find("[data-testid='add-service-record']");
            Assert.Equal($"/vehicles/{vehicleId}/service-records/new", add.GetAttribute("href"));
            Assert.Equal("Add service record", add.TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='service-record']"));
        });
    }

    [Fact]
    public void Records_AreListedInTheOrderReturnedWithTheirValuesAndALink()
    {
        var newer = RecordSummary(new DateOnly(2026, 6, 1), 46_000, "Joe's Garage", lineItems: 2, total: 1234.5m);
        var older = RecordSummary(new DateOnly(2025, 1, 2), 30_000, null, lineItems: 1, total: 40m);
        ServiceRecords = ServiceRecordsPage([newer, older]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='service-record']");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Jun 1, 2026", rows[0].QuerySelector("[data-testid='record-date']")!.TextContent.Trim());
            Assert.Equal("46,000 mi", rows[0].QuerySelector("[data-testid='record-odometer']")!.TextContent.Trim());
            Assert.Equal("Joe's Garage", rows[0].QuerySelector("[data-testid='record-shop']")!.TextContent.Trim());
            Assert.Equal("2 line items", rows[0].QuerySelector("[data-testid='record-line-items']")!.TextContent.Trim());
            Assert.Equal("$1,234.50", rows[0].QuerySelector("[data-testid='record-total']")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{newer.Id}", rows[0].QuerySelector("a")!.GetAttribute("href"));

            Assert.Equal("Jan 2, 2025", rows[1].QuerySelector("[data-testid='record-date']")!.TextContent.Trim());
            Assert.Equal("Not set", rows[1].QuerySelector("[data-testid='record-shop']")!.TextContent.Trim());
            Assert.Equal("1 line item", rows[1].QuerySelector("[data-testid='record-line-items']")!.TextContent.Trim());
            Assert.Equal("$40.00", rows[1].QuerySelector("[data-testid='record-total']")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("table"));
        });
    }

    [Fact]
    public void UserTextIsEncoded()
    {
        ServiceRecords = ServiceRecordsPage([RecordSummary(shop: "<b>Bold</b> Garage")]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Contains("<b>Bold</b> Garage", cut.Find("[data-testid='record-shop']").TextContent);
        });
    }

    [Fact]
    public void Pager_KeepsTheTabAndUsesRecordsPage()
    {
        ServiceRecords = ServiceRecordsPage([RecordSummary()], page: 2, totalCount: 25);

        var cut = RenderView(recordsPage: 2);

        cut.WaitForAssertion(() =>
        {
            var previous = cut.Find("[data-testid='pager-previous'] a");
            var next = cut.Find("[data-testid='pager-next'] a");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records", previous.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&recordsPage=3", next.GetAttribute("href"));
        });
    }

    [Fact]
    public void WithRecords_TheAccordionHeaderSummarizesTheCountAndTheMostRecentServiceDate()
    {
        ServiceRecords = ServiceRecordsPage(
            [RecordSummary(new DateOnly(2026, 6, 1)), RecordSummary(new DateOnly(2025, 1, 2))], totalCount: 2);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Service Records (2)", cut.Find("[data-testid='service-records-count']").TextContent.Trim());
            Assert.Equal("Last Service Jun 1, 2026", cut.Find("[data-testid='service-records-last']").TextContent.Trim());
        });
    }

    [Fact]
    public void ASingleRecord_UsesTheSingularForm()
    {
        ServiceRecords = ServiceRecordsPage([RecordSummary(new DateOnly(2026, 6, 1))]);

        var cut = RenderView();

        cut.WaitForAssertion(() => Assert.Equal("Service Records (1)", cut.Find("[data-testid='service-records-count']").TextContent.Trim()));
    }

    [Fact]
    public void OnALaterPage_TheHeaderStillShowsTheMostRecentDateFromThePageOne()
    {
        ServiceRecords = null;
        RegisterApi(request =>
        {
            var page = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            return page == 1
                ? Json(ServiceRecordsPage([RecordSummary(new DateOnly(2026, 6, 1))], page: 1, totalCount: 12))
                : Json(ServiceRecordsPage([RecordSummary(new DateOnly(2024, 1, 1))], page: 2, totalCount: 12));
        });

        var cut = Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.RecordsPage, 2));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Service Records (12)", cut.Find("[data-testid='service-records-count']").TextContent.Trim());
            Assert.Equal("Last Service Jun 1, 2026", cut.Find("[data-testid='service-records-last']").TextContent.Trim());
        });
    }

    [Fact]
    public void WithNoRecords_NoLastServiceDateIsShown()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No service records yet", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='service-records-last']"));
        });
    }

    [Fact]
    public void AFailedListCall_ShowsNoLastServiceDate()
    {
        ServiceRecordsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='service-records-last']"));
        });
    }

    [Fact]
    public void ApiFailure_ShowsAnAlertAndTheAddActionStaysAvailable()
    {
        ServiceRecordsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='service-record']"));
            Assert.NotNull(cut.Find("[data-testid='add-service-record']"));
        });
    }
}

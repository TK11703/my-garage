using System.Net;
using Bunit;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.FuelEnergyEntries;
using MyGarage.Web.Tests.Inspections;
using MyGarage.Web.Tests.Insurance;
using MyGarage.Web.Tests.Registrations;
using MyGarage.Web.Tests.Taxes;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Maintenance;

public class MaintenanceViewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int recordsPage = 1, MaintenanceSection section = MaintenanceSection.Records)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, section)
            .Add(view => view.RecordsPage, recordsPage));
    }

    [Fact]
    public void RendersAnAccordionWithServiceRecordsChargingAndFuelingTheComplianceItemAndDocuments_InThatOrder()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll(".accordion-item .accordion-header button");
            Assert.Equal(4, buttons.Count);
            Assert.StartsWith("Service Records", buttons[0].TextContent.Trim());
            Assert.StartsWith("Charging and Fueling", buttons[1].TextContent.Trim());
            Assert.StartsWith("Compliance", buttons[2].TextContent.Trim());
            Assert.StartsWith("Documents", buttons[3].TextContent.Trim());
            Assert.NotNull(cut.Find("[data-testid='accordion-service-records']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-fuel']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-compliance']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-documents']"));
        });
    }

    [Fact]
    public void OffersImportRecord_LinkingToTheUploadPageWithThisVehicle()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("a[data-testid='import-record']");
            Assert.Equal($"/drafts/upload?vehicleId={vehicleId}", link.GetAttribute("href"));
            Assert.Contains("Import Record", link.TextContent);
            Assert.Equal("Import a record for this vehicle", link.GetAttribute("aria-label"));
            Assert.Contains("btn-outline-primary", link.ClassName);
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
            Assert.DoesNotContain("show", cut.Find("#maintenance-fuel").ClassList);
            Assert.DoesNotContain("show", cut.Find("#maintenance-compliance").ClassList);
        });
    }

    [Theory]
    [InlineData(MaintenanceSection.Records, "maintenance-service-records")]
    [InlineData(MaintenanceSection.Fuel, "maintenance-fuel")]
    [InlineData(MaintenanceSection.Compliance, "maintenance-compliance")]
    [InlineData(MaintenanceSection.Documents, "maintenance-documents")]
    public void TheSectionParameter_OpensOnlyThatItem(MaintenanceSection section, string openId)
    {
        var cut = RenderView(section: section);

        cut.WaitForAssertion(() =>
        {
            var open = cut.FindAll(".accordion-collapse.show");
            Assert.Single(open);
            Assert.Equal(openId, open[0].Id);
            foreach (var button in cut.FindAll(".accordion-header button"))
            {
                var expanded = button.GetAttribute("data-bs-target") == "#" + openId;
                Assert.Equal(expanded ? "true" : "false", button.GetAttribute("aria-expanded"));
                Assert.Equal(expanded, !button.ClassList.Contains("collapsed"));
            }
        });
    }

    [Fact]
    public void NoRecords_ShowsTheEmptyState()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No service records yet", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='service-record']"));
        });
    }

    [Theory]
    [InlineData("accordion-service-records")]
    [InlineData("accordion-fuel")]
    [InlineData("accordion-compliance")]
    public void TheAccordionItemsHaveNoAddActions_TheyLiveInTheVehiclePageBar(string item)
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find($"[data-testid='{item}']"));
            Assert.Empty(cut.FindAll($"[data-testid='{item}'] [data-testid^='add-']"));
            Assert.Empty(cut.FindAll($"[data-testid='{item}'] .mg-accordion-add"));
            Assert.Empty(cut.FindAll($"[data-testid='{item}'] button[data-bs-toggle='dropdown']"));
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
    public void ApiFailure_ShowsAnAlert()
    {
        ServiceRecordsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='service-record']"));
        });
    }

    [Fact]
    public void TheDocumentsItem_ShowsItsEmptyStateAndHeader()
    {
        ServiceRecords = ServiceRecordsPage([]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("[data-testid='accordion-documents']");
            Assert.Contains("No documents kept yet.", item.TextContent);
            Assert.StartsWith("Documents", item.QuerySelector(".accordion-header button")!.TextContent.Trim());
        });
    }

    [Fact]
    public void TheDocumentsPage_UsesItsOwnPageParameter()
    {
        ServiceRecords = ServiceRecordsPage([]);
        MaintenanceLists["documents"] = new
        {
            items = new[] { new { id = Guid.NewGuid(), fileName = "invoice.pdf", contentType = "application/pdf", sizeBytes = 1000, createdAt = DateTimeOffset.UtcNow } },
            page = 2,
            pageSize = 10,
            totalCount = 25,
            totalPages = 3,
        };
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, MaintenanceSection.Documents)
            .Add(view => view.DocumentsPage, 2));

        cut.WaitForAssertion(() =>
        {
            var next = cut.Find("#maintenance-documents [data-testid='pager-next'] a");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=documents&documentsPage=3", next.GetAttribute("href"));
        });
    }

    [Fact]
    public void RowsFromAnEmail_ShowAFromEmailBadgeInEachOfTheSixLists()
    {
        ServiceRecords = ServiceRecordsPage([RecordSummary(fromEmail: true), RecordSummary(fromEmail: false)]);
        MaintenanceLists["fuel-energy-entries"] = FuelEnergyEntryTestData.FuelPage([FuelEnergyEntryTestData.Entry(fromEmail: true), FuelEnergyEntryTestData.Entry()]);
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(fromEmail: true), InspectionTestData.Entry()]);
        MaintenanceLists["registrations"] = RegistrationTestData.RegistrationPage([RegistrationTestData.Entry(fromEmail: true), RegistrationTestData.Entry()]);
        MaintenanceLists["insurance"] = InsurancePolicyTestData.InsurancePage([InsurancePolicyTestData.Entry(fromEmail: true), InsurancePolicyTestData.Entry()]);
        MaintenanceLists["taxes"] = TaxTestData.TaxPage([TaxTestData.Entry(fromEmail: true), TaxTestData.Entry()]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            foreach (var list in new[] { "service-record-list", "fuel-energy-entry-list", "inspection-list", "registration-list", "insurance-list", "tax-list" })
            {
                var rows = cut.FindAll($"[data-testid='{list}'] > li");
                Assert.Equal(2, rows.Count);
                var badge = Assert.Single(rows[0].QuerySelectorAll("[data-testid='from-email']"));
                Assert.Equal("From email", badge.TextContent.Trim());
                Assert.Contains("badge", badge.ClassList);
                Assert.Empty(rows[1].QuerySelectorAll("[data-testid='from-email']"));
            }
        });
    }
}

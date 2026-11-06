using System.Net;
using Bunit;
using MyGarage.Shared.Taxes;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Inspections;
using MyGarage.Web.Tests.Insurance;
using MyGarage.Web.Tests.Registrations;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Taxes.TaxTestData;

namespace MyGarage.Web.Tests.Taxes;

public class TaxSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int taxesPage = 1)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, MaintenanceSection.Compliance)
            .Add(view => view.TaxesPage, taxesPage));
    }

    [Fact]
    public void NoTaxes_ShowsTheEmptyState()
    {
        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='compliance-taxes']");
            Assert.Contains("No taxes yet. Add the first tax for this vehicle.", section.TextContent);
            Assert.Equal("Taxes", section.QuerySelector("h3")!.TextContent.Trim());
        });
    }

    [Fact]
    public void Taxes_AreListedWithTheirTypeAmountRecurrenceAndAnEditLink()
    {
        var personal = Entry(vehicleId, type: TaxType.PersonalProperty, amount: 312.40m, recurrence: TaxRecurrence.Annual);
        var lease = Entry(vehicleId, type: TaxType.Lease, amount: 45m, recurrence: TaxRecurrence.Monthly);
        var purchase = Entry(vehicleId, type: TaxType.Purchase, amount: 1800m, recurrence: TaxRecurrence.Once);
        var biAnnual = Entry(vehicleId, type: TaxType.Purchase, amount: 150m, recurrence: TaxRecurrence.BiAnnual);
        MaintenanceLists["taxes"] = TaxPage([personal, lease, purchase, biAnnual]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='tax']");
            Assert.Equal(4, rows.Count);
            Assert.Equal("Personal Property", rows[0].QuerySelector("[data-testid='entry-type']")!.TextContent.Trim());
            Assert.Equal("$312.40", rows[0].QuerySelector("[data-testid='entry-amount']")!.TextContent.Trim());
            Assert.Equal("Annual", rows[0].QuerySelector("[data-testid='entry-recurrence']")!.TextContent.Trim());
            var edit = rows[0].QuerySelector("[data-testid='edit-entry']")!;
            Assert.Equal($"/vehicles/{vehicleId}/taxes/{personal.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit Personal Property tax", edit.GetAttribute("aria-label"));
            Assert.Equal("Edit Personal Property tax", edit.GetAttribute("title"));

            Assert.Equal("Lease", rows[1].QuerySelector("[data-testid='entry-type']")!.TextContent.Trim());
            Assert.Equal("Monthly", rows[1].QuerySelector("[data-testid='entry-recurrence']")!.TextContent.Trim());
            Assert.Equal("Once", rows[2].QuerySelector("[data-testid='entry-recurrence']")!.TextContent.Trim());
            Assert.Equal("Purchase", rows[2].QuerySelector("[data-testid='entry-type']")!.TextContent.Trim());
            Assert.Equal("Bi-Annual", rows[3].QuerySelector("[data-testid='entry-recurrence']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ThePager_KeepsTheTabAndSectionAndUsesTaxesPage()
    {
        MaintenanceLists["taxes"] = TaxPage([Entry(vehicleId)], page: 2, totalCount: 25);

        var cut = RenderView(taxesPage: 2);

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='compliance-taxes']");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", section.QuerySelector("[data-testid='pager-previous'] a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance&taxesPage=3", section.QuerySelector("[data-testid='pager-next'] a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ATaxesFailure_ShowsAnAlertInItsSectionWhileTheOtherThreeBlocksStillRender()
    {
        MaintenanceListFailures["taxes"] = HttpStatusCode.InternalServerError;
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId)]);
        MaintenanceLists["registrations"] = RegistrationTestData.RegistrationPage([RegistrationTestData.Entry(vehicleId)]);
        MaintenanceLists["insurance"] = InsurancePolicyTestData.InsurancePage([InsurancePolicyTestData.Entry(vehicleId)]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='compliance-taxes'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='tax']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-inspections'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-registrations'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-insurance'] [role='alert']"));
            Assert.Single(cut.FindAll("[data-testid='inspection']"));
            Assert.Single(cut.FindAll("[data-testid='registration']"));
            Assert.Single(cut.FindAll("[data-testid='insurance-policy']"));
            Assert.Equal("1 inspection, 1 registration, 1 insurance", cut.Find("[data-testid='compliance-summary']").TextContent.Trim());
        });
    }

    [Fact]
    public void TheHeading_SummarizesEveryListsCount()
    {
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId), InspectionTestData.Entry(vehicleId)]);
        MaintenanceLists["registrations"] = RegistrationTestData.RegistrationPage([RegistrationTestData.Entry(vehicleId)]);
        MaintenanceLists["taxes"] = TaxPage([Entry(vehicleId), Entry(vehicleId), Entry(vehicleId)]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
            Assert.Equal("2 inspections, 1 registration, 0 insurance, 3 taxes", cut.Find("[data-testid='compliance-summary']").TextContent.Trim()));
    }

    [Theory]
    [InlineData(0, "0 taxes")]
    [InlineData(1, "1 tax")]
    [InlineData(2, "2 taxes")]
    public void TheHeading_SummarizesTheTaxCount(int count, string expectedEnding)
    {
        MaintenanceLists["taxes"] = TaxPage([.. Enumerable.Range(0, count).Select(_ => Entry(vehicleId))]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
            Assert.Equal($"0 inspections, 0 registrations, 0 insurance, {expectedEnding}", cut.Find("[data-testid='compliance-summary']").TextContent.Trim()));
    }
}

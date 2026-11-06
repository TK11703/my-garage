using System.Net;
using Bunit;
using MyGarage.Shared.Insurance;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Inspections;
using MyGarage.Web.Tests.Registrations;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Insurance.InsurancePolicyTestData;

namespace MyGarage.Web.Tests.Insurance;

public class InsuranceSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int insurancePage = 1)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, MaintenanceSection.Compliance)
            .Add(view => view.InsurancePage, insurancePage));
    }

    [Fact]
    public void NoInsurance_ShowsTheEmptyState()
    {
        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='compliance-insurance']");
            Assert.Contains("No insurance yet. Add the first policy for this vehicle.", section.TextContent);
            Assert.Equal("Insurance", section.QuerySelector("h3")!.TextContent.Trim());
        });
    }

    [Fact]
    public void Policies_AreListedWithTheirSpanCostWithFrequencyAndAnEditLink()
    {
        var annual = Entry(vehicleId, startDate: new DateOnly(2026, 5, 1), endDate: new DateOnly(2027, 5, 1), duration: InsuranceDuration.Annual, cost: 1240m);
        var biAnnual = Entry(vehicleId, startDate: new DateOnly(2025, 5, 1), endDate: new DateOnly(2026, 5, 1), duration: InsuranceDuration.BiAnnual, cost: 640m);
        var monthly = Entry(vehicleId, startDate: new DateOnly(2024, 5, 1), endDate: new DateOnly(2025, 5, 1), duration: InsuranceDuration.Monthly, cost: 120m);
        MaintenanceLists["insurance"] = InsurancePage([annual, biAnnual, monthly]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='insurance-policy']");
            Assert.Equal(3, rows.Count);
            Assert.Equal("May 1, 2026 to May 1, 2027", rows[0].QuerySelector("[data-testid='entry-span']")!.TextContent.Trim());
            Assert.Equal("$1,240.00 Annual", rows[0].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            var edit = rows[0].QuerySelector("[data-testid='edit-entry']")!;
            Assert.Equal($"/vehicles/{vehicleId}/insurance/{annual.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit insurance ending May 1, 2027", edit.GetAttribute("aria-label"));
            Assert.Equal("Edit insurance ending May 1, 2027", edit.GetAttribute("title"));

            Assert.Equal("$640.00 Bi-Annual", rows[1].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            Assert.Equal("May 1, 2025 to May 1, 2026", rows[1].QuerySelector("[data-testid='entry-span']")!.TextContent.Trim());
            Assert.Equal("$120.00 Monthly", rows[2].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ThePager_KeepsTheTabAndSectionAndUsesInsurancePage()
    {
        MaintenanceLists["insurance"] = InsurancePage([Entry(vehicleId)], page: 2, totalCount: 25);

        var cut = RenderView(insurancePage: 2);

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='compliance-insurance']");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", section.QuerySelector("[data-testid='pager-previous'] a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance&insurancePage=3", section.QuerySelector("[data-testid='pager-next'] a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void AnInsuranceFailure_ShowsAnAlertInItsSectionWhileInspectionsAndRegistrationsStillRender()
    {
        MaintenanceListFailures["insurance"] = HttpStatusCode.InternalServerError;
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId)]);
        MaintenanceLists["registrations"] = RegistrationTestData.RegistrationPage([RegistrationTestData.Entry(vehicleId)]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='compliance-insurance'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='insurance-policy']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-inspections'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-registrations'] [role='alert']"));
            Assert.Single(cut.FindAll("[data-testid='inspection']"));
            Assert.Single(cut.FindAll("[data-testid='registration']"));
            Assert.Equal("1 inspection, 1 registration, 0 taxes", cut.Find("[data-testid='compliance-summary']").TextContent.Trim());
        });
    }

    [Theory]
    [InlineData(0, "1 inspection, 0 registrations, 0 insurance, 0 taxes")]
    [InlineData(1, "1 inspection, 0 registrations, 1 insurance, 0 taxes")]
    [InlineData(2, "1 inspection, 0 registrations, 2 insurance, 0 taxes")]
    public void TheHeading_SummarizesTheInsuranceCount(int count, string expected)
    {
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId)]);
        MaintenanceLists["insurance"] = InsurancePage([.. Enumerable.Range(0, count).Select(_ => Entry(vehicleId))]);

        var cut = RenderView();

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("[data-testid='compliance-summary']").TextContent.Trim()));
    }
}

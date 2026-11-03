using System.Net;
using Bunit;
using MyGarage.Shared.Inspections;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Inspections.InspectionTestData;

namespace MyGarage.Web.Tests.Inspections;

public class ComplianceSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int inspectionsPage = 1, MaintenanceSection section = MaintenanceSection.Compliance)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, section)
            .Add(view => view.InspectionsPage, inspectionsPage));
    }

    [Fact]
    public void NoInspections_ShowsTheEmptyStateWithAnAddAction()
    {
        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No inspections yet. Add the first inspection for this vehicle.", cut.Find("#maintenance-compliance").TextContent);
            var add = cut.Find("[data-testid='add-inspection']");
            Assert.Equal($"/vehicles/{vehicleId}/inspections/new", add.GetAttribute("href"));
            Assert.Equal("Add inspection", add.TextContent.Trim());
            Assert.Equal("Inspections", cut.Find("[data-testid='compliance-inspections'] h3").TextContent.Trim());
        });
    }

    [Fact]
    public void Inspections_AreListedWithTheirValuesAndAnEditLink()
    {
        var safety = Entry(vehicleId, type: InspectionType.Safety, date: new DateOnly(2026, 6, 1), cost: 25.5m, nextDueDate: new DateOnly(2027, 6, 1));
        var emissions = Entry(vehicleId, type: InspectionType.Emissions, date: new DateOnly(2025, 5, 1), cost: 40m);
        MaintenanceLists["inspections"] = InspectionPage([safety, emissions]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='inspection']");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Jun 1, 2026", rows[0].QuerySelector("[data-testid='entry-date']")!.TextContent.Trim());
            Assert.Equal("Safety", rows[0].QuerySelector("[data-testid='entry-type']")!.TextContent.Trim());
            Assert.Equal("$25.50", rows[0].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            Assert.Equal("Next due Jun 1, 2027", rows[0].QuerySelector("[data-testid='entry-next-due']")!.TextContent.Trim());
            var edit = rows[0].QuerySelector("[data-testid='edit-entry']")!;
            Assert.Equal($"/vehicles/{vehicleId}/inspections/{safety.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit inspection from Jun 1, 2026", edit.GetAttribute("aria-label"));

            Assert.Equal("Emissions", rows[1].QuerySelector("[data-testid='entry-type']")!.TextContent.Trim());
            Assert.Equal("$40.00", rows[1].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            Assert.Equal("Next due Not set", rows[1].QuerySelector("[data-testid='entry-next-due']")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("#maintenance-compliance table"));
        });
    }

    [Fact]
    public void ThePager_KeepsTheTabAndSectionAndUsesInspectionsPage()
    {
        MaintenanceLists["inspections"] = InspectionPage([Entry(vehicleId)], page: 2, totalCount: 25);

        var cut = RenderView(inspectionsPage: 2);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", cut.Find("#maintenance-compliance [data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance&inspectionsPage=3", cut.Find("#maintenance-compliance [data-testid='pager-next'] a").GetAttribute("href"));
        });
    }

    [Fact]
    public void AnApiFailure_ShowsAnAlertInsideTheComplianceItemWhileTheOtherItemsStillRender()
    {
        MaintenanceListFailures["inspections"] = HttpStatusCode.InternalServerError;

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("#maintenance-compliance [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='inspection']"));
            Assert.StartsWith("0 registrations", cut.Find("[data-testid='compliance-summary']").TextContent.Trim());
            Assert.NotNull(cut.Find("[data-testid='add-inspection']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-service-records']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-fuel']"));
        });
    }

    [Theory]
    [InlineData(0, "0 inspections")]
    [InlineData(1, "1 inspection")]
    [InlineData(2, "2 inspections")]
    public void TheHeading_SummarizesTheInspectionCount(int count, string expected)
    {
        MaintenanceLists["inspections"] = InspectionPage([.. Enumerable.Range(0, count).Select(_ => Entry(vehicleId))]);

        var cut = RenderView();

        cut.WaitForAssertion(() => Assert.StartsWith(expected, cut.Find("[data-testid='compliance-summary']").TextContent.Trim()));
    }

    [Theory]
    [InlineData(MaintenanceSection.Compliance, true)]
    [InlineData(MaintenanceSection.Records, false)]
    [InlineData(MaintenanceSection.Fuel, false)]
    public void TheItemIsExpandedOnlyWhenTheSectionIsCompliance(MaintenanceSection section, bool expanded)
    {
        var cut = RenderView(section: section);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(expanded, cut.Find("#maintenance-compliance").ClassList.Contains("show"));
            var button = cut.Find("[data-bs-target='#maintenance-compliance']");
            Assert.Equal(expanded ? "true" : "false", button.GetAttribute("aria-expanded"));
            Assert.Equal(!expanded, button.ClassList.Contains("collapsed"));
            Assert.Equal("maintenance-compliance", button.GetAttribute("aria-controls"));
        });
    }
}

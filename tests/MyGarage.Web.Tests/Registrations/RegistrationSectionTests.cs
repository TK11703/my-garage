using System.Net;
using Bunit;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Inspections;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Registrations.RegistrationTestData;

namespace MyGarage.Web.Tests.Registrations;

public class RegistrationSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int registrationsPage = 1)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, MaintenanceSection.Compliance)
            .Add(view => view.RegistrationsPage, registrationsPage));
    }

    [Fact]
    public void NoRegistrations_ShowsTheEmptyStateWithAnAddAction()
    {
        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No registrations yet. Add the first registration for this vehicle.", cut.Find("[data-testid='compliance-registrations']").TextContent);
            var add = cut.Find("[data-testid='add-registration']");
            Assert.Equal($"/vehicles/{vehicleId}/registrations/new", add.GetAttribute("href"));
            Assert.Equal("Add registration", add.TextContent.Trim());
            Assert.Equal("Registrations", cut.Find("[data-testid='compliance-registrations'] h3").TextContent.Trim());
        });
    }

    [Fact]
    public void Registrations_AreListedWithTheirValuesAndAnEditLink()
    {
        var washington = Entry(vehicleId, state: "WA", cost: 78.75m, expirationDate: new DateOnly(2027, 5, 1));
        var dc = Entry(vehicleId, state: "DC", cost: 40m, expirationDate: new DateOnly(2025, 1, 31));
        MaintenanceLists["registrations"] = RegistrationPage([washington, dc]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='registration']");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Washington (WA)", rows[0].QuerySelector("[data-testid='entry-state']")!.TextContent.Trim());
            Assert.Equal("$78.75", rows[0].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            Assert.Equal("Expires May 1, 2027", rows[0].QuerySelector("[data-testid='entry-expires']")!.TextContent.Trim());
            var edit = rows[0].QuerySelector("[data-testid='edit-entry']")!;
            Assert.Equal($"/vehicles/{vehicleId}/registrations/{washington.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit registration expiring May 1, 2027", edit.GetAttribute("aria-label"));
            Assert.Equal("Edit registration expiring May 1, 2027", edit.GetAttribute("title"));

            Assert.Equal("District of Columbia (DC)", rows[1].QuerySelector("[data-testid='entry-state']")!.TextContent.Trim());
            Assert.Equal("$40.00", rows[1].QuerySelector("[data-testid='entry-cost']")!.TextContent.Trim());
            Assert.Equal("Expires Jan 31, 2025", rows[1].QuerySelector("[data-testid='entry-expires']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ThePager_KeepsTheTabAndSectionAndUsesRegistrationsPage()
    {
        MaintenanceLists["registrations"] = RegistrationPage([Entry(vehicleId)], page: 2, totalCount: 25);

        var cut = RenderView(registrationsPage: 2);

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='compliance-registrations']");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", section.QuerySelector("[data-testid='pager-previous'] a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance&registrationsPage=3", section.QuerySelector("[data-testid='pager-next'] a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ARegistrationsFailure_ShowsAnAlertInItsSectionWhileInspectionsStillRender()
    {
        MaintenanceListFailures["registrations"] = HttpStatusCode.InternalServerError;
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId)]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='compliance-registrations'] [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='registration']"));
            Assert.Empty(cut.FindAll("[data-testid='compliance-inspections'] [role='alert']"));
            Assert.Single(cut.FindAll("[data-testid='inspection']"));
            Assert.StartsWith("1 inspection", cut.Find("[data-testid='compliance-summary']").TextContent.Trim());
            Assert.NotNull(cut.Find("[data-testid='add-registration']"));
        });
    }

    [Theory]
    [InlineData(0, "1 inspection, 0 registrations")]
    [InlineData(1, "1 inspection, 1 registration")]
    [InlineData(2, "1 inspection, 2 registrations")]
    public void TheHeading_SummarizesTheInspectionAndRegistrationCounts(int count, string expected)
    {
        MaintenanceLists["inspections"] = InspectionTestData.InspectionPage([InspectionTestData.Entry(vehicleId)]);
        MaintenanceLists["registrations"] = RegistrationPage([.. Enumerable.Range(0, count).Select(_ => Entry(vehicleId))]);

        var cut = RenderView();

        cut.WaitForAssertion(() => Assert.StartsWith(expected, cut.Find("[data-testid='compliance-summary']").TextContent.Trim()));
    }
}

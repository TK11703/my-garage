using System.Net;
using Bunit;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.FuelEnergyEntries.FuelEnergyEntryTestData;

namespace MyGarage.Web.Tests.FuelEnergyEntries;

public class FuelSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<MaintenanceView> RenderView(int fuelPage = 1, MaintenanceSection section = MaintenanceSection.Fuel)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Render<MaintenanceView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Section, section)
            .Add(view => view.FuelPage, fuelPage));
    }

    [Fact]
    public void NoEntries_ShowsTheEmptyState()
    {
        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No fuel or charging entries yet. Add the first one for this vehicle.", cut.Find("#maintenance-fuel").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='fuel-last']"));
            Assert.Equal("Charging and Fueling (0)", cut.Find("[data-testid='fuel-count']").TextContent.Trim());
        });
    }

    [Fact]
    public void Entries_AreListedNewestFirstWithTheirValuesAndAnEditLink()
    {
        var fuel = Entry(vehicleId, kind: FuelEnergyKind.Fuel, date: new DateOnly(2026, 6, 1), unitPrice: 3.459m, quantity: 12.345m, totalCost: 42.70m);
        var charge = Entry(vehicleId, kind: FuelEnergyKind.Energy, date: new DateOnly(2026, 5, 1), unitPrice: 0.1342m, quantity: 28.5m, totalCost: 3.82m);
        MaintenanceLists["fuel-energy-entries"] = FuelPage([fuel, charge]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='fuel-energy-entry']");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Jun 1, 2026", rows[0].QuerySelector("[data-testid='entry-date']")!.TextContent.Trim());
            Assert.Equal("Fuel", rows[0].QuerySelector("[data-testid='entry-kind']")!.TextContent.Trim());
            Assert.Equal("12.345 gal at $3.459/gal", rows[0].QuerySelector("[data-testid='entry-detail']")!.TextContent.Trim());
            Assert.Equal("$42.70", rows[0].QuerySelector("[data-testid='entry-total']")!.TextContent.Trim());
            var edit = rows[0].QuerySelector("[data-testid='edit-entry']")!;
            Assert.Equal($"/vehicles/{vehicleId}/fuel-energy-entries/{fuel.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit fuel from Jun 1, 2026", edit.GetAttribute("aria-label"));

            Assert.Equal("Charge", rows[1].QuerySelector("[data-testid='entry-kind']")!.TextContent.Trim());
            Assert.Equal("28.5 kWh at $0.1342/kWh", rows[1].QuerySelector("[data-testid='entry-detail']")!.TextContent.Trim());
            Assert.Equal("$3.82", rows[1].QuerySelector("[data-testid='entry-total']")!.TextContent.Trim());
            Assert.Equal("Edit charge from May 1, 2026", rows[1].QuerySelector("[data-testid='edit-entry']")!.GetAttribute("aria-label"));
            Assert.Empty(cut.FindAll("#maintenance-fuel table"));
        });
    }

    [Fact]
    public void ThePager_KeepsTheTabAndSectionAndUsesFuelPage()
    {
        MaintenanceLists["fuel-energy-entries"] = FuelPage([Entry(vehicleId)], page: 2, totalCount: 25);

        var cut = RenderView(fuelPage: 2);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=fuel", cut.Find("#maintenance-fuel [data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=fuel&fuelPage=3", cut.Find("#maintenance-fuel [data-testid='pager-next'] a").GetAttribute("href"));
        });
    }

    [Fact]
    public void TheHeading_ShowsTheCountAndTheLatestEntryDate()
    {
        MaintenanceLists["fuel-energy-entries"] = FuelPage([Entry(vehicleId, date: new DateOnly(2026, 6, 1)), Entry(vehicleId, date: new DateOnly(2025, 1, 2))]);

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Charging and Fueling (2)", cut.Find("[data-testid='fuel-count']").TextContent.Trim());
            Assert.Equal("Last entry Jun 1, 2026", cut.Find("[data-testid='fuel-last']").TextContent.Trim());
        });
    }

    [Fact]
    public void OnALaterPage_TheHeadingStillShowsTheLatestDateFromPageOne()
    {
        MaintenanceListResponders["fuel-energy-entries"] = request =>
            request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)
                ? Json(FuelPage([Entry(vehicleId, date: new DateOnly(2024, 1, 1))], page: 2, totalCount: 12))
                : Json(FuelPage([Entry(vehicleId, date: new DateOnly(2026, 6, 1))], page: 1, totalCount: 12));

        var cut = RenderView(fuelPage: 2);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Charging and Fueling (12)", cut.Find("[data-testid='fuel-count']").TextContent.Trim());
            Assert.Equal("Last entry Jun 1, 2026", cut.Find("[data-testid='fuel-last']").TextContent.Trim());
        });
    }

    [Fact]
    public void AnApiFailure_ShowsAnAlert()
    {
        MaintenanceListFailures["fuel-energy-entries"] = HttpStatusCode.InternalServerError;

        var cut = RenderView();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("#maintenance-fuel [role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='fuel-energy-entry']"));
            Assert.Empty(cut.FindAll("[data-testid='fuel-count'] + [data-testid='fuel-last']"));
        });
    }

    [Fact]
    public void AFailureInThisList_DoesNotAffectTheServiceRecordsItem()
    {
        MaintenanceListFailures["fuel-energy-entries"] = HttpStatusCode.InternalServerError;

        var cut = RenderView(section: MaintenanceSection.Records);

        cut.WaitForAssertion(() => Assert.Contains("No service records yet", cut.Find("#maintenance-service-records").TextContent));
    }
}

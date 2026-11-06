using Bunit;
using MyGarage.Web.Maintenance;

namespace MyGarage.Web.Tests.Maintenance;

public class AddMaintenanceMenuTests : BunitContext
{
    private readonly Guid vehicleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private IRenderedComponent<AddMaintenanceMenu> RenderMenu() =>
        Render<AddMaintenanceMenu>(parameters => parameters.Add(menu => menu.VehicleId, vehicleId));

    [Fact]
    public void ItIsOneDropdownButtonNamedAdd()
    {
        var cut = RenderMenu();

        var toggle = cut.Find("[data-testid='add-maintenance-menu'] > button");
        Assert.Equal("dropdown", toggle.GetAttribute("data-bs-toggle"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.StartsWith("Add", toggle.TextContent.Trim());
        Assert.NotNull(toggle.QuerySelector("svg.bi"));
    }

    [Fact]
    public void ItHoldsEveryAddActionInOrder_WithTheirLinks()
    {
        var cut = RenderMenu();

        var items = cut.FindAll(".dropdown-menu a.dropdown-item");
        Assert.Equal(
            ["add-service-record", "add-fuel", "add-charge", "add-inspection", "add-registration", "add-insurance", "add-tax", "add-reminder"],
            items.Select(link => link.GetAttribute("data-testid")));
        Assert.Equal(
            ["Add service record", "Add fuel", "Add charge", "Add inspection", "Add registration", "Add insurance", "Add tax", "Add reminder"],
            items.Select(link => link.TextContent.Trim()));
        Assert.Equal(
            [
                $"/vehicles/{vehicleId}/service-records/new",
                $"/vehicles/{vehicleId}/fuel-energy-entries/new?kind=fuel",
                $"/vehicles/{vehicleId}/fuel-energy-entries/new?kind=energy",
                $"/vehicles/{vehicleId}/inspections/new",
                $"/vehicles/{vehicleId}/registrations/new",
                $"/vehicles/{vehicleId}/insurance/new",
                $"/vehicles/{vehicleId}/taxes/new",
                $"/vehicles/{vehicleId}/reminders/new",
            ],
            items.Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public void TheActionsAreGroupedUnderHeadings()
    {
        var cut = RenderMenu();

        Assert.Equal(["Service", "Charging and fueling", "Compliance", "Reminders"], cut.FindAll(".dropdown-header").Select(header => header.TextContent.Trim()));
    }
}

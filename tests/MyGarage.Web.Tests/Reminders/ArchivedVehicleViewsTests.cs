using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Dashboard;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

// How a reminder of an archived vehicle and an archived vehicle's spend are shown (specs/014-archive-vehicle).
public class ArchivedVehicleViewsTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid reminderId = Guid.NewGuid();

    private ReminderDto ArchivedReminder() =>
        Reminder(vehicleId, reminderId, "Registration due", intervalMiles: null, intervalTime: new() { Value = 1, Unit = ReminderTimeUnit.Years },
            status: ReminderStatus.Archived, nextDueMileage: null, nextDueDate: new DateOnly(2026, 1, 1), milesRemaining: null, daysRemaining: -310, isPaused: true, vehicleArchived: true);

    [Fact]
    public void TheArchivedBadge_ShowsTheWordArchived_InTheMutedStyle()
    {
        var cut = Render<ReminderStatusBadge>(parameters => parameters.Add(badge => badge.Status, ReminderStatus.Archived));

        var badge = cut.Find("[data-testid='reminder-status']");
        Assert.Equal("Archived", badge.TextContent.Trim());
        Assert.Contains("text-bg-light", badge.ClassName);
        Assert.Contains("border", badge.ClassName);
    }

    [Fact]
    public void TheVehicleTabRow_SaysDisabledAndShowsNoRemainingAmount()
    {
        var cut = Render<ReminderDueRow>(parameters => parameters.Add(row => row.Reminder, ArchivedReminder()));

        Assert.Equal("Disabled, vehicle archived", cut.Find("[data-testid='reminder-archived']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='reminder-remaining']"));
        Assert.Equal("Archived", cut.Find("[data-testid='reminder-status']").TextContent.Trim());
        Assert.Contains("bg-secondary-subtle", cut.Find("[data-testid='due-tile']").ClassName);
    }

    [Fact]
    public void TheDetailPage_ExplainsTheDisabledReminder_AndOffersNoActionsOrMarkDone()
    {
        RegisterApi(_ => Json(DetailOf(ArchivedReminder())));

        var cut = Render<ReminderDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Registration due", cut.Find("h1").TextContent);
            Assert.Equal("Archived", cut.Find("[data-testid='reminder-details'] [data-testid='reminder-status']").TextContent.Trim());
            Assert.Contains("Disabled, vehicle archived", cut.Find("[data-testid='archived-note']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='reminder-remaining']"));
            Assert.Empty(cut.FindAll("[data-testid='edit-reminder']"));
            Assert.Empty(cut.FindAll("[data-testid='pause-reminder'], [data-testid='resume-reminder']"));
            Assert.Empty(cut.FindAll("[data-testid='delete-reminder']"));
            Assert.Empty(cut.FindAll("form"));
            Assert.NotNull(cut.Find("[data-testid='history']"));
        });
    }

    [Fact]
    public void AnActiveReminderKeepsItsActions()
    {
        RegisterApi(_ => Json(DetailOf(Reminder(vehicleId, reminderId))));

        var cut = Render<ReminderDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='edit-reminder']"));
            Assert.NotNull(cut.Find("[data-testid='delete-reminder']"));
            Assert.Empty(cut.FindAll("[data-testid='archived-note']"));
        });
    }
}

public class SpendChartArchivedTests : BunitContext
{
    private static readonly Guid Truck = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Sedan = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static FuelEnergySpendVehicleDto Vehicle(Guid id, string name, decimal fuel, bool archived) =>
        new() { VehicleId = id, VehicleName = name, Fuel = fuel, Charging = 0m, VehicleArchived = archived };

    private IRenderedComponent<SpendChart> RenderChart() =>
        Render<SpendChart>(parameters => parameters.Add(chart => chart.Months,
            [new FuelEnergySpendMonthDto
            {
                Month = new DateOnly(2026, 10, 1), Fuel = 80m, Charging = 0m,
                Vehicles = [Vehicle(Truck, "2020 Ford F-150", 60m, false), Vehicle(Sedan, "2018 Honda Civic", 20m, true)],
            }]));

    [Fact]
    public void AnArchivedVehicle_IsMarkedInTheLegend_TheTooltips_AndTheFiguresTable()
    {
        var cut = RenderChart();

        var legend = cut.FindAll("[data-testid='spend-legend'] li").Select(item => item.TextContent.Trim()).ToList();
        Assert.Equal(["2020 Ford F-150", "2018 Honda Civic (archived)"], legend);
        var tooltips = cut.FindAll("[data-testid='chart-fuel'] title").Select(title => title.TextContent).ToList();
        Assert.Contains(tooltips, text => text.Contains("2018 Honda Civic (archived): $20.00", StringComparison.Ordinal));
        Assert.DoesNotContain(tooltips, text => text.Contains("Ford F-150 (archived)", StringComparison.Ordinal));
        var rows = cut.FindAll("[data-testid='spend-table'] [data-testid='figures-row']").Select(row => row.TextContent).ToList();
        Assert.Contains(rows, text => text.Contains("2018 Honda Civic (archived)", StringComparison.Ordinal));
        Assert.Contains(rows, text => text.Contains("2020 Ford F-150", StringComparison.Ordinal) && !text.Contains("(archived)", StringComparison.Ordinal));
    }
}

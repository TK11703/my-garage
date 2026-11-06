using System.Net;
using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Dashboard;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Dashboard;

public class DashboardPageTests : VehicleTestContext
{
    private DashboardDto dashboard = Dashboard();

    private static DashboardDto Dashboard(
        int vehicles = 3,
        int serviceRecords = 12,
        int fuel = 7,
        int drafts = 2,
        int overdue = 1,
        int dueSoon = 2,
        FuelEnergySpendMonthDto[]? spend = null,
        params ReminderDto[] attention) =>
        new()
        {
            Vehicles = vehicles,
            ServiceRecords = serviceRecords,
            FuelEnergyEntries = fuel,
            DraftsAwaitingReview = drafts,
            RemindersOverdue = overdue,
            RemindersDueSoon = dueSoon,
            Attention = attention,
            FuelEnergySpend = spend ?? [],
        };

    private static FuelEnergySpendMonthDto Month(int year, int month, decimal fuel, decimal charging) =>
        new() { Month = new DateOnly(year, month, 1), Fuel = fuel, Charging = charging };

    private IRenderedComponent<DashboardPage> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? (request => request.RequestUri!.AbsolutePath == "/api/dashboard" ? Json(dashboard) : new HttpResponseMessage(HttpStatusCode.NotFound)));
        return Render<DashboardPage>();
    }

    private static string Card(IRenderedComponent<DashboardPage> cut, string id) =>
        cut.Find($"[data-testid='metric-{id}']").TextContent;

    [Fact]
    public void ShowsEveryMetricWithALabelAndANumber()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Dashboard", cut.Find("h1").TextContent);
            foreach (var (id, label, value) in new[]
            {
                ("vehicles", "Vehicles", "3"), ("service-records", "Service records", "12"), ("fuel-energy-entries", "Fuel and charging entries", "7"),
                ("drafts", "Drafts awaiting review", "2"), ("overdue", "Reminders overdue", "1"), ("due-soon", "Reminders due soon", "2"),
            })
            {
                Assert.Contains(label, Card(cut, id));
                Assert.Equal(value, cut.Find($"[data-testid='metric-{id}'] [data-testid='metric-value']").TextContent.Trim());
            }
        });
    }

    [Fact]
    public void TheLinkedCardsGoToTheirPages_AndTheOthersHaveNoLink()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/vehicles", cut.Find("[data-testid='metric-vehicles'] a").GetAttribute("href"));
            Assert.Equal("/drafts", cut.Find("[data-testid='metric-drafts'] a").GetAttribute("href"));
            Assert.Equal("/reminders?status=overdue", cut.Find("[data-testid='metric-overdue'] a").GetAttribute("href"));
            Assert.Equal("/reminders?status=due-soon", cut.Find("[data-testid='metric-due-soon'] a").GetAttribute("href"));
            Assert.Empty(cut.FindAll("[data-testid='metric-service-records'] a"));
            Assert.Empty(cut.FindAll("[data-testid='metric-fuel-energy-entries'] a"));
        });
    }

    [Fact]
    public void TheReminderCards_AreTintedWhenThereIsSomethingToSee()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("border-danger", cut.Find("[data-testid='metric-overdue'] .card").ClassName);
            Assert.Contains("border-warning", cut.Find("[data-testid='metric-due-soon'] .card").ClassName);
        });
    }

    [Fact]
    public void TheReminderCards_AreNotTintedWhenThereIsNothingToSee()
    {
        dashboard = Dashboard(overdue: 0, dueSoon: 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("border-danger", cut.Find("[data-testid='metric-overdue'] .card").ClassName);
            Assert.DoesNotContain("border-warning", cut.Find("[data-testid='metric-due-soon'] .card").ClassName);
        });
    }
    [Fact]
    public void WithNoFuelOrChargingCosts_TheSpendSectionSaysSoInsteadOfDrawingAnEmptyChart()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No fuel or charging costs", cut.Find("[data-testid='spend']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='spend-chart']"));
        });
    }

    [Fact]
    public void WithFuelAndChargingCosts_TheSpendSectionDrawsTheChart()
    {
        dashboard = Dashboard(spend: [Month(2026, 9, 40m, 0m), Month(2026, 10, 25m, 12m)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Fuel and charging spend", cut.Find("[data-testid='spend'] h2").TextContent);
            Assert.NotNull(cut.Find("[data-testid='spend-chart'] svg[role='img']"));
        });
    }

    [Fact]
    public void AnEmptyGarage_HasNoSpendSection()
    {
        dashboard = Dashboard(0, 0, 0, 0, 0, 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='spend']")));
    }

    [Fact]
    public void NeedsAttention_ListsEachReminderWithItsVehicleStatusRemainingAndALink()
    {
        var vehicleId = Guid.NewGuid();
        var overdue = Reminder(vehicleId, name: "Inspection", status: ReminderStatus.Overdue, milesRemaining: null, daysRemaining: -3, vehicleName: "2020 Ford F-150");
        var soon = Reminder(vehicleId, name: "Oil change", status: ReminderStatus.DueSoon, milesRemaining: 200, vehicleName: "2020 Ford F-150");
        dashboard = Dashboard(attention: [overdue, soon]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='attention']");
            var rows = section.QuerySelectorAll("[data-testid='reminder']");
            Assert.Equal(2, rows.Length);
            Assert.Equal("Inspection", rows[0].QuerySelector("[data-testid='reminder-link']")!.TextContent.Trim());
            Assert.Equal("Overdue", rows[0].QuerySelector("[data-testid='reminder-status']")!.TextContent.Trim());
            Assert.Equal("3 days overdue", rows[0].QuerySelector("[data-testid='reminder-remaining']")!.TextContent.Trim());
            Assert.Equal("2020 Ford F-150", rows[0].QuerySelector("[data-testid='reminder-vehicle']")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/reminders/{overdue.Id}", rows[0].QuerySelector("[data-testid='reminder-link']")!.GetAttribute("href"));
            Assert.Equal("200 miles left", rows[1].QuerySelector("[data-testid='reminder-remaining']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void WithVehiclesButNothingDue_SaysNothingNeedsAttention()
    {
        dashboard = Dashboard(overdue: 0, dueSoon: 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Nothing needs attention", cut.Find("[data-testid='attention']").TextContent));
    }

    [Fact]
    public void AnEmptyGarage_ShowsZerosAndInvitesTheFirstVehicle_WithoutAnAttentionSection()
    {
        dashboard = Dashboard(0, 0, 0, 0, 0, 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("0", cut.Find("[data-testid='metric-vehicles'] [data-testid='metric-value']").TextContent.Trim());
            Assert.Equal("/vehicles/new", cut.Find("[data-testid='first-vehicle'] a.btn").GetAttribute("href"));
            Assert.Contains("Add your first vehicle", cut.Find("[data-testid='first-vehicle']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='attention']"));
        });
    }

    [Fact]
    public void FieldsThePageDoesNotKnow_AreIgnored()
    {
        var json = """{"vehicles":1,"serviceRecords":0,"fuelEnergyEntries":0,"draftsAwaitingReview":0,"remindersOverdue":0,"remindersDueSoon":0,"attention":[],"fuelUsage":[{"month":"2026-10","gallons":12.5}]}""";

        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find("[data-testid='metric-vehicles'] [data-testid='metric-value']").TextContent.Trim()));
    }

    [Fact]
    public void AnOutage_ShowsAnAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

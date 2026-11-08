using System.Net;
using Bunit;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderRemainingFormatTests
{
    [Theory]
    [InlineData(1_200, "1,200 miles left")]
    [InlineData(1, "1 mile left")]
    [InlineData(0, "due now")]
    [InlineData(-1, "1 mile overdue")]
    [InlineData(-400, "400 miles overdue")]
    public void Miles(int milesRemaining, string expected) =>
        Assert.Equal(expected, ReminderFormat.Remaining(Reminder(milesRemaining: milesRemaining)));

    [Theory]
    [InlineData(18, "due in 18 days")]
    [InlineData(1, "due in 1 day")]
    [InlineData(0, "due today")]
    [InlineData(-1, "1 day overdue")]
    [InlineData(-3, "3 days overdue")]
    public void Days(int daysRemaining, string expected) =>
        Assert.Equal(expected, ReminderFormat.Remaining(Reminder(intervalMiles: null, nextDueMileage: null, milesRemaining: null, daysRemaining: daysRemaining)));

    [Fact]
    public void BothParts_AreShownTogether() =>
        Assert.Equal("200 miles left · due in 90 days", ReminderFormat.Remaining(Reminder(milesRemaining: 200, daysRemaining: 90)));

    [Fact]
    public void WithoutAnyRemainingValue_ThereIsNoText() =>
        Assert.Equal(string.Empty, ReminderFormat.Remaining(Reminder(milesRemaining: null, daysRemaining: null)));

    [Fact]
    public void TheUrgentParts_AreThoseAtOrPastTheirDueSoonLimit()
    {
        var reminder = Reminder(intervalTime: new() { Value = 6, Unit = ReminderTimeUnit.Months }, milesRemaining: 500, daysRemaining: 31);

        var parts = ReminderFormat.RemainingParts(reminder);

        Assert.Equal([("500 miles left", true), ("due in 31 days", false)], parts.Select(part => (part.Text, part.Urgent)));
    }

    [Fact]
    public void ASinglePart_IsNeverMarkedUrgent()
    {
        var parts = ReminderFormat.RemainingParts(Reminder(milesRemaining: 10));

        Assert.False(Assert.Single(parts).Urgent);
    }
}

public class ReminderStatusDisplayTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleDetail> RenderTab(params ReminderDto[] reminders)
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : Json(reminders));
        NavigateTo($"/vehicles/{vehicleId}?tab=reminders");
        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Theory]
    [InlineData(ReminderStatus.Overdue, "Overdue", "text-bg-danger")]
    [InlineData(ReminderStatus.DueSoon, "Due soon", "text-bg-warning")]
    [InlineData(ReminderStatus.Upcoming, "Upcoming", "text-bg-secondary")]
    [InlineData(ReminderStatus.MileageNeeded, "Mileage needed", "text-bg-info")]
    [InlineData(ReminderStatus.Paused, "Paused", "text-bg-light")]
    public void TheBadgeAlwaysHasText_AndAColorPerStatus(ReminderStatus status, string text, string cssClass)
    {
        var cut = Render<ReminderStatusBadge>(parameters => parameters.Add(badge => badge.Status, status));

        Assert.Equal(text, cut.Find("span.badge").TextContent.Trim());
        Assert.Contains(cssClass, cut.Find("span.badge").ClassName);
    }

    [Fact]
    public void TheTabRowsShowTheBadgeAndTheRemainingText()
    {
        var overdue = Reminder(vehicleId, name: "Oil change", status: ReminderStatus.Overdue, milesRemaining: -400);

        var cut = RenderTab(overdue);

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("[data-testid='reminder']");
            Assert.Equal("Overdue", row.QuerySelector("[data-testid='reminder-status']")!.TextContent.Trim());
            Assert.Equal("400 miles overdue", row.QuerySelector("[data-testid='reminder-remaining']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ACombinedReminder_ShowsBothLinesAndMarksThePartAtOrPastItsLimit()
    {
        var combined = Reminder(vehicleId, intervalTime: new() { Value = 6, Unit = ReminderTimeUnit.Months }, status: ReminderStatus.DueSoon,
            nextDueDate: new DateOnly(2027, 5, 1), milesRemaining: 200, daysRemaining: 90);

        var cut = RenderTab(combined);

        cut.WaitForAssertion(() =>
        {
            var remaining = cut.Find("[data-testid='reminder-remaining']");
            Assert.Contains("200 miles left", remaining.TextContent);
            Assert.Contains("due in 90 days", remaining.TextContent);
            var urgent = remaining.QuerySelectorAll(".fw-semibold");
            Assert.Equal("200 miles left", Assert.Single(urgent).TextContent.Replace("(most urgent)", string.Empty).Trim());
            Assert.Contains("most urgent", Assert.Single(urgent).QuerySelector(".visually-hidden")!.TextContent);
        });
    }

    [Fact]
    public void ADistanceOnlyReminderWithoutAMileage_ShowsAHintToRecordTheMileage()
    {
        var needs = Reminder(vehicleId, status: ReminderStatus.MileageNeeded, milesRemaining: null);

        var cut = RenderTab(needs);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Mileage needed", cut.Find("[data-testid='reminder-status']").TextContent.Trim());
            Assert.Contains("Record the vehicle's mileage", cut.Find("[data-testid='reminder']").TextContent);
        });
    }

    [Fact]
    public void APausedReminder_ShowsPausedAndNoRemainingText()
    {
        var paused = Reminder(vehicleId, isPaused: true, status: ReminderStatus.Paused, milesRemaining: -5);

        var cut = RenderTab(paused);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Paused", cut.Find("[data-testid='reminder-status']").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='reminder-remaining']"));
        });
    }
}

public class GarageRemindersPageTests : VehicleTestContext
{
    private readonly Guid vehicleA = Guid.NewGuid();
    private readonly Guid vehicleB = Guid.NewGuid();
    private List<ReminderDto> reminders = [];

    private IRenderedComponent<GarageRemindersPage> RenderPage(string? status = null, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? (request => request.RequestUri!.AbsolutePath == "/api/reminders" ? Json(reminders) : new HttpResponseMessage(HttpStatusCode.NotFound)));
        NavigateTo("/reminders" + (status is null ? string.Empty : $"?status={status}"));
        return Render<GarageRemindersPage>();
    }

    private void Seed() =>
        reminders =
        [
            Reminder(vehicleA, name: "Inspection", status: ReminderStatus.Overdue, milesRemaining: null, daysRemaining: -3, vehicleName: "2020 Ford F-150"),
            Reminder(vehicleA, name: "Oil change", status: ReminderStatus.DueSoon, milesRemaining: 200),
            Reminder(vehicleB, name: "Tire rotation", status: ReminderStatus.MileageNeeded, milesRemaining: null, vehicleName: "2018 Honda Civic"),
            Reminder(vehicleB, name: "Registration", status: ReminderStatus.Upcoming, milesRemaining: 3_000, vehicleName: "2018 Honda Civic"),
            Reminder(vehicleB, name: "Winter tires", isPaused: true, status: ReminderStatus.Paused, vehicleName: "2018 Honda Civic"),
        ];

    [Fact]
    public void ListsEveryReminderInTheOrderGiven_EachWithItsVehicleLinkingToTheTab()
    {
        Seed();

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Reminders", cut.Find("h1").TextContent);
            var rows = cut.FindAll("[data-testid='reminder']");
            Assert.Equal(5, rows.Count);
            Assert.Equal(["Inspection", "Oil change", "Tire rotation", "Registration", "Winter tires"], rows.Select(row => row.QuerySelector("[data-testid='reminder-link']")!.TextContent.Trim()));
            var vehicle = rows[2].QuerySelector("[data-testid='reminder-vehicle']")!;
            Assert.Equal("2018 Honda Civic", vehicle.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleB}?tab=reminders", vehicle.GetAttribute("href"));
        });
    }

    [Fact]
    public void TheFilterRowShowsEachStatusWithItsCount()
    {
        Seed();

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("[data-testid='status-filter'] a");
            Assert.Equal(
                ["All (5)", "Overdue (1)", "Due soon (1)", "Upcoming (1)", "Mileage needed (1)", "Paused (1)"],
                links.Select(link => link.TextContent.Trim()));
            Assert.Equal("/reminders?status=due-soon", links[2].GetAttribute("href"));
            Assert.Equal("/reminders", links[0].GetAttribute("href"));
        });
    }

    [Theory]
    [InlineData("overdue", new[] { "Inspection" })]
    [InlineData("due-soon", new[] { "Oil change" })]
    [InlineData("mileage-needed", new[] { "Tire rotation" })]
    [InlineData("upcoming", new[] { "Registration" })]
    [InlineData("paused", new[] { "Winter tires" })]
    public void AKnownStatus_FiltersTheList(string status, string[] expected)
    {
        Seed();

        var cut = RenderPage(status);

        cut.WaitForAssertion(() =>
            Assert.Equal(expected, cut.FindAll("[data-testid='reminder-link']").Select(link => link.TextContent.Trim())));
    }

    [Fact]
    public void AnUnknownStatus_ShowsEverything()
    {
        Seed();

        var cut = RenderPage("whatever");

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("[data-testid='reminder']").Count));
    }

    [Fact]
    public void NoReminders_ShowsTheEmptyStateWithALinkToMyVehicles()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("no reminders yet", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("/vehicles", cut.Find("a.btn").GetAttribute("href"));
        });
    }

    [Fact]
    public void AFilterWithNoMatches_SaysSoAndLinksBackToAll()
    {
        reminders = [Reminder(vehicleA, status: ReminderStatus.Upcoming)];

        var cut = RenderPage("overdue");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='reminder']"));
            Assert.Contains("No reminders are overdue", cut.Markup);
        });
    }

    [Fact]
    public void AnOutage_ShowsAnAlert()
    {
        var cut = RenderPage(respond: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

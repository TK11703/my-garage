using System.Net;
using Bunit;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderProgressFormatTests
{
    [Theory]
    [InlineData(5_000, 5_000, 0)]
    [InlineData(5_000, 2_500, 50)]
    [InlineData(5_000, 0, 100)]
    [InlineData(5_000, -400, 100)]
    [InlineData(5_000, 9_000, 0)]
    public void ADistanceInterval_IsTheShareAlreadyUsed(int interval, int remaining, int expected) =>
        Assert.Equal(expected, ReminderFormat.PercentUsed(Reminder(intervalMiles: interval, milesRemaining: remaining)));

    [Fact]
    public void ATimeInterval_UsesItsLengthInMonths()
    {
        var reminder = Reminder(intervalMiles: null, intervalTime: new() { Value = 6, Unit = ReminderTimeUnit.Months }, nextDueMileage: null, milesRemaining: null, daysRemaining: 91);

        Assert.Equal(50, ReminderFormat.PercentUsed(reminder));
    }

    [Fact]
    public void ATimeIntervalInYears_UsesItsLengthInDays()
    {
        var reminder = Reminder(intervalMiles: null, intervalTime: new() { Value = 1, Unit = ReminderTimeUnit.Years }, nextDueMileage: null, milesRemaining: null, daysRemaining: 0);

        Assert.Equal(100, ReminderFormat.PercentUsed(reminder));
    }

    [Fact]
    public void WithBothIntervals_TheMoreUsedOneWins()
    {
        var reminder = Reminder(intervalMiles: 5_000, intervalTime: new() { Value = 6, Unit = ReminderTimeUnit.Months }, milesRemaining: 4_000, daysRemaining: 30);

        Assert.Equal(84, ReminderFormat.PercentUsed(reminder));
    }

    [Theory]
    [InlineData(ReminderStatus.Paused)]
    [InlineData(ReminderStatus.Archived)]
    public void APausedOrArchivedReminder_HasNoProgress(ReminderStatus status) =>
        Assert.Null(ReminderFormat.PercentUsed(Reminder(status: status, milesRemaining: 100)));

    [Fact]
    public void WithoutAnyRemainingValue_ThereIsNoProgress() =>
        Assert.Null(ReminderFormat.PercentUsed(Reminder(status: ReminderStatus.MileageNeeded, milesRemaining: null)));
}

public class ReminderGaugeRenderTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    [Theory]
    [InlineData(ReminderStatus.Overdue, "mg-gauge-bad")]
    [InlineData(ReminderStatus.DueSoon, "mg-gauge-accent")]
    [InlineData(ReminderStatus.Upcoming, "mg-gauge-ok")]
    public void TheGaugeIsANamedProgressElement_ToneFollowingTheStatus(ReminderStatus status, string tone)
    {
        var cut = Render<ReminderGauge>(parameters => parameters.Add(gauge => gauge.Reminder, Reminder(status: status, intervalMiles: 5_000, milesRemaining: 1_000)));

        var gauge = cut.Find("progress.mg-gauge");
        Assert.Contains(tone, gauge.ClassName);
        Assert.Equal("80", gauge.GetAttribute("value"));
        Assert.Equal("100", gauge.GetAttribute("max"));
        Assert.Equal("80% of the interval used", gauge.GetAttribute("aria-label"));
    }

    [Fact]
    public void WithoutProgress_NothingIsRendered()
    {
        var cut = Render<ReminderGauge>(parameters => parameters.Add(gauge => gauge.Reminder, Reminder(status: ReminderStatus.Paused, isPaused: true)));

        Assert.Empty(cut.FindAll("progress"));
    }

    [Fact]
    public void TheGaragePage_ShowsAGaugeOnlyOnRowsWithProgress_AndRanksUpcomingAboveMileageNeeded()
    {
        var reminders = new[]
        {
            Reminder(vehicleId, name: "Winter tires", isPaused: true, status: ReminderStatus.Paused),
            Reminder(vehicleId, name: "Tire rotation", status: ReminderStatus.MileageNeeded, milesRemaining: null),
            Reminder(vehicleId, name: "Registration", status: ReminderStatus.Upcoming, milesRemaining: 3_000),
            Reminder(vehicleId, name: "Oil change", status: ReminderStatus.DueSoon, milesRemaining: 200),
            Reminder(vehicleId, name: "Inspection", status: ReminderStatus.Overdue, milesRemaining: -10),
        };
        RegisterApi(request => request.RequestUri!.AbsolutePath == "/api/reminders" ? Json(reminders) : new HttpResponseMessage(HttpStatusCode.NotFound));
        NavigateTo("/reminders");

        var cut = Render<GarageRemindersPage>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='reminder']");
            Assert.Equal(
                ["Inspection", "Oil change", "Registration", "Tire rotation", "Winter tires"],
                rows.Select(row => row.QuerySelector("[data-testid='reminder-link']")!.TextContent.Trim()));
            Assert.Equal([true, true, true, false, false], rows.Select(row => row.QuerySelector("progress.mg-gauge") is not null));
        });
    }
}

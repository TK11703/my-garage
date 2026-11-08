using MyGarage.Api.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderStatusCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static ReminderCalculation Calculate(
        int? miles = null,
        int? months = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = null,
        bool paused = false,
        int? current = null,
        DateOnly? today = null) =>
        ReminderStatusCalculator.Calculate(
            new ReminderCalculationInput(miles, months, lastDoneOn ?? new DateOnly(2026, 11, 1), lastDoneMileage, paused, current),
            today ?? Today);

    [Fact]
    public void DistanceOnly_NextDueIsLastDoneMileagePlusTheInterval()
    {
        var result = Calculate(miles: 5_000, lastDoneMileage: 40_000, current: 41_000);

        Assert.Equal(45_000, result.NextDueMileage);
        Assert.Equal(4_000, result.MilesRemaining);
        Assert.Null(result.NextDueDate);
        Assert.Null(result.DaysRemaining);
        Assert.Equal(ReminderStatus.Upcoming, result.Status);
    }

    [Fact]
    public void TimeOnly_NextDueIsLastDoneDatePlusTheInterval()
    {
        var result = Calculate(months: 12, lastDoneOn: new DateOnly(2026, 3, 15), today: new DateOnly(2026, 11, 6));

        Assert.Equal(new DateOnly(2027, 3, 15), result.NextDueDate);
        Assert.Equal(new DateOnly(2027, 3, 15).DayNumber - new DateOnly(2026, 11, 6).DayNumber, result.DaysRemaining);
        Assert.Null(result.NextDueMileage);
        Assert.Null(result.MilesRemaining);
        Assert.Equal(ReminderStatus.Upcoming, result.Status);
    }

    [Theory]
    [InlineData(501, ReminderStatus.Upcoming)]
    [InlineData(500, ReminderStatus.DueSoon)]
    [InlineData(1, ReminderStatus.DueSoon)]
    [InlineData(0, ReminderStatus.Overdue)]
    [InlineData(-1, ReminderStatus.Overdue)]
    public void DistanceThresholds(int milesLeft, ReminderStatus expected)
    {
        var result = Calculate(miles: 5_000, lastDoneMileage: 40_000, current: 45_000 - milesLeft);

        Assert.Equal(expected, result.Status);
        Assert.Equal(milesLeft, result.MilesRemaining);
    }

    [Theory]
    [InlineData(31, ReminderStatus.Upcoming)]
    [InlineData(30, ReminderStatus.DueSoon)]
    [InlineData(1, ReminderStatus.DueSoon)]
    [InlineData(0, ReminderStatus.Overdue)]
    [InlineData(-5, ReminderStatus.Overdue)]
    public void TimeThresholds(int daysLeft, ReminderStatus expected)
    {
        var due = Today.AddDays(daysLeft);
        var lastDone = due.AddMonths(-12);

        var result = Calculate(months: 12, lastDoneOn: lastDone);

        Assert.Equal(due, result.NextDueDate);
        Assert.Equal(daysLeft, result.DaysRemaining);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Combined_TakesTheMoreUrgentPart()
    {
        // Distance is far off, time is overdue.
        var timeOverdue = Calculate(miles: 5_000, months: 6, lastDoneOn: new DateOnly(2026, 1, 1), lastDoneMileage: 40_000, current: 40_100);
        // Time is far off, distance is overdue.
        var distanceOverdue = Calculate(miles: 5_000, months: 6, lastDoneOn: new DateOnly(2026, 10, 20), lastDoneMileage: 40_000, current: 45_200);
        // Both far off.
        var upcoming = Calculate(miles: 5_000, months: 6, lastDoneOn: new DateOnly(2026, 10, 20), lastDoneMileage: 40_000, current: 40_100);

        Assert.Equal(ReminderStatus.Overdue, timeOverdue.Status);
        Assert.Equal(ReminderStatus.Overdue, distanceOverdue.Status);
        Assert.Equal(ReminderStatus.Upcoming, upcoming.Status);
        Assert.Equal(45_000, upcoming.NextDueMileage);
        Assert.Equal(new DateOnly(2027, 4, 20), upcoming.NextDueDate);
    }

    [Fact]
    public void Combined_DueSoonWhenEitherPartIsWithinItsLimit()
    {
        var result = Calculate(miles: 5_000, months: 12, lastDoneOn: new DateOnly(2026, 11, 1), lastDoneMileage: 40_000, current: 44_800);

        Assert.Equal(ReminderStatus.DueSoon, result.Status);
        Assert.Equal(200, result.MilesRemaining);
    }

    [Theory]
    [InlineData(2024, 2, 29, 12, 2025, 2, 28)]
    [InlineData(2026, 1, 31, 1, 2026, 2, 28)]
    [InlineData(2026, 3, 15, 24, 2028, 3, 15)]
    public void DueDate_ClampsToTheLastDayOfAShorterMonth(int y, int m, int d, int months, int ey, int em, int ed)
    {
        var result = Calculate(months: months, lastDoneOn: new DateOnly(y, m, d));

        Assert.Equal(new DateOnly(ey, em, ed), result.NextDueDate);
    }

    [Fact]
    public void Paused_ShowsPausedWhateverElseHolds_AndStillReturnsTheDuePoints()
    {
        var result = Calculate(miles: 5_000, lastDoneMileage: 40_000, current: 50_000, paused: true);

        Assert.Equal(ReminderStatus.Paused, result.Status);
        Assert.Equal(45_000, result.NextDueMileage);
        Assert.Equal(-5_000, result.MilesRemaining);
    }

    [Fact]
    public void DistanceOnly_WithoutACurrentMileage_NeedsMileage()
    {
        var result = Calculate(miles: 5_000, lastDoneMileage: 40_000, current: null);

        Assert.Equal(ReminderStatus.MileageNeeded, result.Status);
        Assert.Equal(45_000, result.NextDueMileage);
        Assert.Null(result.MilesRemaining);
    }

    [Fact]
    public void Combined_WithoutACurrentMileage_FollowsTheDatePartOnly()
    {
        var overdue = Calculate(miles: 5_000, months: 6, lastDoneOn: new DateOnly(2026, 1, 1), lastDoneMileage: 40_000, current: null);
        var upcoming = Calculate(miles: 5_000, months: 6, lastDoneOn: new DateOnly(2026, 10, 1), lastDoneMileage: 40_000, current: null);

        Assert.Equal(ReminderStatus.Overdue, overdue.Status);
        Assert.Equal(ReminderStatus.Upcoming, upcoming.Status);
        Assert.Null(upcoming.MilesRemaining);
    }

    [Fact]
    public void AFirstDueDateAlreadyInThePast_IsOverdue()
    {
        var result = Calculate(months: 6, lastDoneOn: new DateOnly(2020, 1, 1));

        Assert.Equal(ReminderStatus.Overdue, result.Status);
        Assert.True(result.DaysRemaining < 0);
    }

    [Fact]
    public void ACurrentMileageBelowTheLastDoneMileage_StillCalculates()
    {
        var result = Calculate(miles: 5_000, lastDoneMileage: 40_000, current: 39_000);

        Assert.Equal(6_000, result.MilesRemaining);
        Assert.Equal(ReminderStatus.Upcoming, result.Status);
    }
}

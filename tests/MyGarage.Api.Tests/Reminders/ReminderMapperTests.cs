using MyGarage.Api.Reminders;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderMapperTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static ReminderRecord Record(
        int? miles = 5_000,
        int? months = null,
        int? lastMileage = 40_000,
        int? current = 41_000,
        bool paused = false,
        int? year = 2020) =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            VehicleYear = year,
            VehicleMake = "Ford",
            VehicleModel = "F-150",
            Name = "Oil change",
            IntervalMiles = miles,
            IntervalMonths = months,
            LastDoneOn = new DateOnly(2026, 5, 1),
            LastDoneMileage = lastMileage,
            IsPaused = paused,
            CurrentMileage = current,
        };

    [Fact]
    public void ToDto_CopiesTheFieldsAndAddsTheCalculatedValues()
    {
        var record = Record(months: 6);

        var dto = ReminderMapper.ToDto(record, Today);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal("2020 Ford F-150", dto.VehicleName);
        Assert.Equal("Oil change", dto.Name);
        Assert.Equal(5_000, dto.IntervalMiles);
        Assert.Equal(new ReminderTimeIntervalDto { Value = 6, Unit = ReminderTimeUnit.Months }, dto.IntervalTime, new IntervalComparer());
        Assert.Equal(new DateOnly(2026, 5, 1), dto.LastDoneOn);
        Assert.Equal(40_000, dto.LastDoneMileage);
        Assert.False(dto.IsPaused);
        Assert.Equal(45_000, dto.NextDueMileage);
        Assert.Equal(4_000, dto.MilesRemaining);
        Assert.Equal(new DateOnly(2026, 11, 1), dto.NextDueDate);
        Assert.Equal(-5, dto.DaysRemaining);
        Assert.Equal(ReminderStatus.Overdue, dto.Status);
    }

    [Fact]
    public void ToDto_AVehicleWithoutAYear_LeavesItOutOfTheName() =>
        Assert.Equal("Ford F-150", ReminderMapper.ToDto(Record(year: null), Today).VehicleName);

    [Fact]
    public void ToDto_APausedReminder_IsPaused() =>
        Assert.Equal(ReminderStatus.Paused, ReminderMapper.ToDto(Record(paused: true), Today).Status);

    [Theory]
    [InlineData(24, 2, ReminderTimeUnit.Years)]
    [InlineData(12, 1, ReminderTimeUnit.Years)]
    [InlineData(18, 18, ReminderTimeUnit.Months)]
    [InlineData(6, 6, ReminderTimeUnit.Months)]
    public void IntervalMonths_AreShownInYearsWhenTheyDivideByTwelve(int months, int value, ReminderTimeUnit unit)
    {
        var interval = ReminderMapper.ToIntervalTime(months);

        Assert.Equal(value, interval!.Value);
        Assert.Equal(unit, interval.Unit);
    }

    [Fact]
    public void ToIntervalTime_NullStaysNull() => Assert.Null(ReminderMapper.ToIntervalTime(null));

    [Theory]
    [InlineData(2, ReminderTimeUnit.Years, 24)]
    [InlineData(6, ReminderTimeUnit.Months, 6)]
    public void ToValues_ConvertsTheTimeIntervalToMonths(int value, ReminderTimeUnit unit, int months)
    {
        var values = ReminderMapper.ToValues(new ReminderRequest
        {
            Name = "Registration",
            IntervalTime = new ReminderTimeIntervalDto { Value = value, Unit = unit },
            LastDoneOn = new DateOnly(2026, 1, 1),
        });

        Assert.Equal(months, values.IntervalMonths);
        Assert.Null(values.IntervalMiles);
        Assert.Null(values.LastDoneMileage);
    }

    [Fact]
    public void ToValues_TrimsTheName()
    {
        var values = ReminderMapper.ToValues(new ReminderRequest
        {
            Name = "  Oil change ",
            IntervalMiles = 5_000,
            LastDoneOn = new DateOnly(2026, 1, 1),
            LastDoneMileage = 100,
        });

        Assert.Equal("Oil change", values.Name);
    }

    [Fact]
    public void ToDetailDto_ListsTheCompletionsInTheGivenOrder()
    {
        var record = Record();
        var newer = new ReminderCompletionRecord { Id = Guid.NewGuid(), ReminderId = record.Id, DoneOn = new DateOnly(2026, 6, 1), Mileage = 40_000 };
        var older = new ReminderCompletionRecord { Id = Guid.NewGuid(), ReminderId = record.Id, DoneOn = new DateOnly(2026, 1, 1), Mileage = null };

        var dto = ReminderMapper.ToDetailDto(record, [newer, older], Today);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal([newer.Id, older.Id], dto.Completions.Select(item => item.Id));
        Assert.Equal(40_000, dto.Completions[0].Mileage);
        Assert.Null(dto.Completions[1].Mileage);
    }

    private sealed class IntervalComparer : IEqualityComparer<ReminderTimeIntervalDto?>
    {
        public bool Equals(ReminderTimeIntervalDto? x, ReminderTimeIntervalDto? y) => x?.Value == y?.Value && x?.Unit == y?.Unit;

        public int GetHashCode(ReminderTimeIntervalDto? obj) => HashCode.Combine(obj?.Value, obj?.Unit);
    }
}

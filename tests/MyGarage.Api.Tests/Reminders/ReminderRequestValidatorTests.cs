using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly ReminderRequestValidator validator = new(new TestTimeProvider(Now));

    private static ReminderRequest Valid(
        string? name = "Oil change",
        int? miles = 5_000,
        ReminderTimeIntervalDto? time = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000) =>
        new()
        {
            Name = name,
            IntervalMiles = miles,
            IntervalTime = time,
            LastDoneOn = lastDoneOn ?? new DateOnly(2026, 5, 1),
            LastDoneMileage = lastDoneMileage,
        };

    private static ReminderTimeIntervalDto Time(int value, ReminderTimeUnit unit) => new() { Value = value, Unit = unit };

    private bool IsValid(ReminderRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(ReminderRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    [Fact]
    public void ADistanceReminder_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void ATimeOnlyReminderWithoutAMileage_IsValid() =>
        Assert.True(IsValid(Valid(miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneMileage: null)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankName_Fails(string? name) => Assert.Equal(["Name"], ErrorFields(Valid(name)));

    [Fact]
    public void NameLength_IsLimitedTo100() 
    {
        Assert.True(IsValid(Valid(new string('a', 100))));
        Assert.Equal(["Name"], ErrorFields(Valid(new string('a', 101))));
    }

    [Fact]
    public void AtLeastOneInterval_IsRequired()
    {
        var result = validator.Validate(Valid(miles: null, time: null, lastDoneMileage: null));

        Assert.Equal(["IntervalMiles"], result.Errors.Select(error => error.PropertyName));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(999_999, true)]
    [InlineData(1_000_000, false)]
    public void IntervalMiles_IsBounded(int miles, bool valid) => Assert.Equal(valid, IsValid(Valid(miles: miles)));

    [Theory]
    [InlineData(0, ReminderTimeUnit.Months, false)]
    [InlineData(1, ReminderTimeUnit.Months, true)]
    [InlineData(600, ReminderTimeUnit.Months, true)]
    [InlineData(601, ReminderTimeUnit.Months, false)]
    [InlineData(0, ReminderTimeUnit.Years, false)]
    [InlineData(1, ReminderTimeUnit.Years, true)]
    [InlineData(50, ReminderTimeUnit.Years, true)]
    [InlineData(51, ReminderTimeUnit.Years, false)]
    public void IntervalTime_IsBoundedPerUnit(int value, ReminderTimeUnit unit, bool valid)
    {
        var request = Valid(miles: null, time: Time(value, unit), lastDoneMileage: null);

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Contains("IntervalTime.Value", ErrorFields(request));
        }
    }

    [Fact]
    public void LastDoneOn_MustBePresentNotInTheFutureAndNotBefore1900()
    {
        Assert.Equal(["LastDoneOn"], ErrorFields(new ReminderRequest { Name = "x", IntervalMiles = 1, LastDoneMileage = 0 }));
        Assert.Equal(["LastDoneOn"], ErrorFields(Valid(lastDoneOn: new DateOnly(2026, 11, 7))));
        Assert.Equal(["LastDoneOn"], ErrorFields(Valid(lastDoneOn: new DateOnly(1899, 12, 31))));
        Assert.True(IsValid(Valid(lastDoneOn: new DateOnly(2026, 11, 6))));
        Assert.True(IsValid(Valid(lastDoneOn: new DateOnly(1900, 1, 1))));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(10_000_000, false)]
    public void LastDoneMileage_IsBounded(int mileage, bool valid) => Assert.Equal(valid, IsValid(Valid(lastDoneMileage: mileage)));

    [Fact]
    public void LastDoneMileage_IsRequiredWithADistanceInterval_ButNotWithoutOne()
    {
        Assert.Equal(["LastDoneMileage"], ErrorFields(Valid(miles: 5_000, lastDoneMileage: null)));
        Assert.Equal(["LastDoneMileage"], ErrorFields(Valid(miles: 5_000, time: Time(6, ReminderTimeUnit.Months), lastDoneMileage: null)));
        Assert.True(IsValid(Valid(miles: null, time: Time(6, ReminderTimeUnit.Months), lastDoneMileage: null)));
    }
}

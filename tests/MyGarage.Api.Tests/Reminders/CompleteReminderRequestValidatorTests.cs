using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public class CompleteReminderRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly CompleteReminderRequestValidator validator = new(new TestTimeProvider(Now));

    private static CompleteReminderRequest Request(DateOnly? doneOn = null, int? mileage = 45_000) =>
        new() { DoneOn = doneOn ?? new DateOnly(2026, 11, 3), Mileage = mileage };

    private string[] ErrorFields(CompleteReminderRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    [Fact]
    public void AFullRequest_IsValid() => Assert.True(validator.Validate(Request()).IsValid);

    [Fact]
    public void TheMileageIsOptionalHere_BecauseTheEndpointKnowsWhetherTheReminderNeedsIt() =>
        Assert.True(validator.Validate(Request(mileage: null)).IsValid);

    [Fact]
    public void DoneOn_IsRequired() =>
        Assert.Equal(["DoneOn"], ErrorFields(new CompleteReminderRequest { Mileage = 1 }));

    [Theory]
    [InlineData("2026-11-07", false)]
    [InlineData("2026-11-06", true)]
    [InlineData("1900-01-01", true)]
    [InlineData("1899-12-31", false)]
    public void DoneOn_IsNotInTheFutureAndNotBefore1900(string date, bool valid) =>
        Assert.Equal(valid, validator.Validate(Request(DateOnly.Parse(date))).IsValid);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(10_000_000, false)]
    public void Mileage_IsBounded(int mileage, bool valid)
    {
        var result = validator.Validate(Request(mileage: mileage));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(["Mileage"], result.Errors.Select(error => error.PropertyName));
        }
    }
}

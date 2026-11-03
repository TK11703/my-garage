using System.Globalization;
using MyGarage.Api.Insurance;
using MyGarage.Shared.Insurance;

namespace MyGarage.Api.Tests.Insurance;

public class InsurancePolicyRequestValidatorTests
{
    private readonly InsurancePolicyRequestValidator validator = new();

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static InsurancePolicyRequest Valid(
        DateOnly? start = null,
        DateOnly? end = null,
        InsuranceDuration? duration = InsuranceDuration.Annual,
        decimal? cost = 1240m) =>
        new() { StartDate = start ?? new DateOnly(2026, 5, 1), EndDate = end ?? new DateOnly(2027, 5, 1), Duration = duration, Cost = cost };

    private bool IsValid(InsurancePolicyRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(InsurancePolicyRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private string[] Messages(InsurancePolicyRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];

    [Fact]
    public void FullRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void MissingValues_AreReportedOnEachRequiredField()
    {
        var request = new InsurancePolicyRequest();

        Assert.Equal(["StartDate", "EndDate", "Duration", "Cost"], ErrorFields(request));
        Assert.Equal(
            ["Start date is required.", "End date is required.", "Choose how often the premium is paid.", "Cost is required."],
            Messages(request));
    }

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2020, 6, 15, true)]
    [InlineData(2100, 12, 30, true)]
    [InlineData(2101, 1, 1, false)]
    public void StartDate_MustBeBetween1900And2100(int year, int month, int day, bool valid)
    {
        var request = Valid(start: new DateOnly(year, month, day), end: new DateOnly(2100, 12, 31));

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Contains("StartDate", ErrorFields(request));
            Assert.Contains("Start date must be between 1900-01-01 and 2100-12-31.", Messages(request));
        }
    }

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 2, true)]
    [InlineData(2020, 6, 15, true)]
    [InlineData(2100, 12, 31, true)]
    [InlineData(2101, 1, 1, false)]
    public void EndDate_MustBeBetween1900And2100(int year, int month, int day, bool valid)
    {
        var end = new DateOnly(year, month, day);
        var request = Valid(start: new DateOnly(1900, 1, 1), end: end);

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Contains("EndDate", ErrorFields(request));
            Assert.Contains("End date must be between 1900-01-01 and 2100-12-31.", Messages(request));
        }
    }

    [Fact]
    public void PastDates_AreAccepted() =>
        Assert.True(IsValid(Valid(start: new DateOnly(2000, 1, 1), end: new DateOnly(2001, 1, 1))));

    [Fact]
    public void EndDate_OneDayAfterStart_IsValid() =>
        Assert.True(IsValid(Valid(start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 5, 2))));

    [Theory]
    [InlineData("2026-05-01", "2026-05-01")]
    [InlineData("2026-05-01", "2026-04-30")]
    [InlineData("2026-05-01", "2025-05-01")]
    public void EndDate_EqualOrEarlierThanStart_IsRejectedOnEndDate(string start, string end)
    {
        var request = Valid(start: DateOnly.Parse(start), end: DateOnly.Parse(end));

        Assert.False(IsValid(request));
        Assert.Equal(["EndDate"], ErrorFields(request));
        Assert.Equal(["End date must be later than the start date."], Messages(request));
    }

    [Fact]
    public void EndAfterStart_IsOnlyCheckedWhenBothDatesAreGiven()
    {
        var onlyEnd = new InsurancePolicyRequest { EndDate = new DateOnly(2026, 5, 1), Duration = InsuranceDuration.Annual, Cost = 10m };
        var onlyStart = new InsurancePolicyRequest { StartDate = new DateOnly(2026, 5, 1), Duration = InsuranceDuration.Annual, Cost = 10m };

        Assert.Equal(["StartDate"], ErrorFields(onlyEnd));
        Assert.Equal(["EndDate"], ErrorFields(onlyStart));
    }

    [Theory]
    [InlineData(InsuranceDuration.Monthly)]
    [InlineData(InsuranceDuration.BiAnnual)]
    [InlineData(InsuranceDuration.Annual)]
    public void Duration_AcceptsEachDefinedValue(InsuranceDuration duration) => Assert.True(IsValid(Valid(duration: duration)));

    [Fact]
    public void Duration_MustBeGivenAndDefined()
    {
        Assert.Equal(["Duration"], ErrorFields(Valid(duration: null)));
        Assert.Equal(["Choose how often the premium is paid."], Messages(Valid(duration: (InsuranceDuration)99)));
        Assert.Equal(["Duration"], ErrorFields(Valid(duration: (InsuranceDuration)99)));
    }

    [Fact]
    public void Duration_IsNotCheckedAgainstTheDates()
    {
        var request = Valid(start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 6, 1), duration: InsuranceDuration.Annual);

        Assert.True(IsValid(request));
    }

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void Cost_RangeAndDecimals(string cost, bool valid)
    {
        var request = Valid(cost: D(cost));

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Equal(["Cost"], ErrorFields(request));
        }
    }

    [Fact]
    public void Cost_MessagesNameTheProblem()
    {
        Assert.Equal(["Cost must be between 0.00 and 9,999,999.99."], Messages(Valid(cost: -0.01m)));
        Assert.Equal(["Cost can have at most 2 decimal places."], Messages(Valid(cost: 1.005m)));
    }
}

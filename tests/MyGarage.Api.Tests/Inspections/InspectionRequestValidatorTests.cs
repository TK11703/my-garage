using System.Globalization;
using MyGarage.Api.Inspections;
using MyGarage.Shared.Inspections;

namespace MyGarage.Api.Tests.Inspections;

public class InspectionRequestValidatorTests
{
    // 2026-11-02 (UTC).
    private static readonly DateTimeOffset Today = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly InspectionRequestValidator validator = new(new FixedTimeProvider(Today));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static InspectionRequest Valid(
        InspectionType? type = InspectionType.Safety,
        DateOnly? date = null,
        decimal? cost = 25.50m,
        DateOnly? nextDue = null) =>
        new() { Type = type, InspectionDate = date ?? new DateOnly(2026, 5, 1), Cost = cost, NextDueDate = nextDue };

    private bool IsValid(InspectionRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(InspectionRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private string[] Messages(InspectionRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];

    [Fact]
    public void FullRequest_IsValid() => Assert.True(IsValid(Valid(nextDue: new DateOnly(2027, 5, 1))));

    [Fact]
    public void NoNextDueDate_IsValid() => Assert.True(IsValid(Valid(nextDue: null)));

    [Fact]
    public void MissingValues_AreReportedOnEachRequiredField()
    {
        var request = new InspectionRequest();

        Assert.Equal(["Type", "InspectionDate", "Cost"], ErrorFields(request));
        Assert.Equal(["Choose a type.", "Inspection date is required.", "Cost is required."], Messages(request));
    }

    [Fact]
    public void Type_MustBeDefined() => Assert.Equal(["Type"], ErrorFields(Valid(type: (InspectionType)99)));

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2026, 11, 2, true)]
    [InlineData(2026, 11, 3, false)]
    public void InspectionDate_MustBeBetween1900AndToday(int year, int month, int day, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(date: new DateOnly(year, month, day))));

    [Fact]
    public void InspectionDate_MessagesNameTheProblem()
    {
        Assert.Equal(["Inspection date cannot be in the future."], Messages(Valid(date: new DateOnly(2026, 11, 3))));
        Assert.Equal(["Inspection date cannot be before 1900."], Messages(Valid(date: new DateOnly(1899, 12, 31))));
    }

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void Cost_RangeAndDecimals(string cost, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(cost: D(cost))));

    [Fact]
    public void Cost_MessagesNameTheProblem()
    {
        Assert.Equal(["Cost must be between 0.00 and 9,999,999.99."], Messages(Valid(cost: -0.01m)));
        Assert.Equal(["Cost can have at most 2 decimal places."], Messages(Valid(cost: 1.005m)));
    }

    [Fact]
    public void NextDueDate_CanEqualTheInspectionDate() =>
        Assert.True(IsValid(Valid(date: new DateOnly(2026, 5, 1), nextDue: new DateOnly(2026, 5, 1))));

    [Fact]
    public void NextDueDate_CannotBeBeforeTheInspectionDate()
    {
        var request = Valid(date: new DateOnly(2026, 5, 1), nextDue: new DateOnly(2026, 4, 30));

        Assert.Equal(["NextDueDate"], ErrorFields(request));
        Assert.Equal(["Next due date cannot be before the inspection date."], Messages(request));
    }

    [Theory]
    [InlineData(2100, 12, 31, true)]
    [InlineData(2101, 1, 1, false)]
    public void NextDueDate_CannotBeAfter2100(int year, int month, int day, bool valid)
    {
        var request = Valid(nextDue: new DateOnly(year, month, day));

        Assert.Equal(valid, IsValid(request));
        if (!valid)
        {
            Assert.Equal(["Next due date cannot be after 2100-12-31."], Messages(request));
        }
    }

    [Fact]
    public void WithoutAnInspectionDate_TheNextDueDateIsNotComparedWithIt()
    {
        var request = new InspectionRequest { Type = InspectionType.Safety, Cost = 1m, NextDueDate = new DateOnly(2026, 1, 1) };

        Assert.Equal(["InspectionDate"], ErrorFields(request));
    }
}

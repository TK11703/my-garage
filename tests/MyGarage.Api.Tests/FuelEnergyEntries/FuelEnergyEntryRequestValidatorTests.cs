using System.Globalization;
using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Shared.FuelEnergyEntries;

namespace MyGarage.Api.Tests.FuelEnergyEntries;

public class FuelEnergyEntryRequestValidatorTests
{
    // 2026-11-02 (UTC).
    private static readonly DateTimeOffset Today = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FuelEnergyEntryRequestValidator validator = new(new FixedTimeProvider(Today));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static FuelEnergyEntryRequest Valid(
        FuelEnergyKind? kind = FuelEnergyKind.Fuel,
        DateOnly? date = null,
        decimal? price = 3.459m,
        decimal? quantity = 12.345m,
        decimal? total = 42.70m,
        bool defaultDate = true) =>
        new()
        {
            Kind = kind,
            EntryDate = defaultDate ? date ?? new DateOnly(2026, 5, 1) : date,
            UnitPrice = price,
            Quantity = quantity,
            TotalCost = total,
        };

    private bool IsValid(FuelEnergyEntryRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(FuelEnergyEntryRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    [Fact]
    public void FullRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void EmptyTotal_IsValid() => Assert.True(IsValid(Valid(total: null)));

    [Fact]
    public void MissingValues_AreReportedOnEachField() =>
        Assert.Equal(["Kind", "EntryDate", "UnitPrice", "Quantity"], ErrorFields(new FuelEnergyEntryRequest()));

    [Fact]
    public void Kind_MustBeDefined() => Assert.Equal(["Kind"], ErrorFields(Valid(kind: (FuelEnergyKind)99)));

    [Theory]
    [InlineData(1899, 12, 31, false)]
    [InlineData(1900, 1, 1, true)]
    [InlineData(2026, 11, 2, true)]
    [InlineData(2026, 11, 3, false)]
    public void EntryDate_MustBeBetween1900AndToday(int year, int month, int day, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(date: new DateOnly(year, month, day))));

    [Theory]
    [InlineData("-0.0001", false)]
    [InlineData("0", true)]
    [InlineData("9999.9999", true)]
    [InlineData("10000", false)]
    [InlineData("1.00001", false)]
    [InlineData("0.1342", true)]
    public void UnitPrice_RangeAndDecimals(string price, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(price: D(price), total: 1m)));

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.001", true)]
    [InlineData("99999.999", true)]
    [InlineData("100000", false)]
    [InlineData("1.0001", false)]
    public void Quantity_RangeAndDecimals(string quantity, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(quantity: D(quantity), price: 0m, total: 1m)));

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void TotalCost_RangeAndDecimals(string total, bool valid) =>
        Assert.Equal(valid, IsValid(Valid(total: D(total))));

    [Fact]
    public void ACalculatedTotalAboveTheMaximum_IsRejectedOnTotalCost()
    {
        var request = Valid(price: 9999.9999m, quantity: 99999.999m, total: null);

        Assert.Equal(["TotalCost"], ErrorFields(request));
        Assert.Contains("too large", validator.Validate(request).Errors.Single().ErrorMessage);
    }

    [Fact]
    public void AnEnteredTotalIsNotComparedWithPriceTimesAmount() =>
        Assert.True(IsValid(Valid(price: 1m, quantity: 1m, total: 500m)));
}

public class FuelEnergyTotalTests
{
    [Theory]
    [InlineData("3.459", "12.345", "42.70")]
    [InlineData("0.1342", "28.5", "3.82")]
    [InlineData("0.5", "0.005", "0.00")]
    [InlineData("2.675", "1", "2.68")]
    public void WithoutAnEnteredTotal_IsPriceTimesAmountRoundedToTheCent(string price, string quantity, string expected) =>
        Assert.Equal(
            decimal.Parse(expected, CultureInfo.InvariantCulture),
            FuelEnergyTotal.Resolve(decimal.Parse(price, CultureInfo.InvariantCulture), decimal.Parse(quantity, CultureInfo.InvariantCulture), null));

    [Theory]
    [InlineData("43.00")]
    [InlineData("0")]
    public void AnEnteredTotal_IsKeptAsEntered(string total) =>
        Assert.Equal(
            decimal.Parse(total, CultureInfo.InvariantCulture),
            FuelEnergyTotal.Resolve(3.459m, 12.345m, decimal.Parse(total, CultureInfo.InvariantCulture)));
}

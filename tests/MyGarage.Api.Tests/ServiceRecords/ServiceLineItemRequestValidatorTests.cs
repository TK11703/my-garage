using MyGarage.Api.ServiceRecords;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceLineItemRequestValidatorTests
{
    private readonly ServiceLineItemRequestValidator validator = new();

    private static ServiceLineItemRequest Flat(string? description = "Tire rotation", decimal? cost = 40m) =>
        new() { Description = description, FlatCost = cost };

    private static ServiceLineItemRequest Breakdown(
        decimal? hours = 1.5m, decimal? rate = 90m, params ServicePartRequest[] parts) =>
        new() { Description = "Oil change", LaborHours = hours, LaborRate = rate, Parts = parts };

    private static ServicePartRequest Part(string? name = "Oil filter", decimal? quantity = 1m, decimal? price = 12.50m) =>
        new() { Name = name, Quantity = quantity, UnitPrice = price };

    private bool IsValid(ServiceLineItemRequest request) => validator.Validate(request).IsValid;

    private string[] ErrorFields(ServiceLineItemRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];

    private string[] Messages(ServiceLineItemRequest request) =>
        [.. validator.Validate(request).Errors.Select(error => error.ErrorMessage)];

    [Fact]
    public void AFlatCostLine_IsValid() => Assert.True(IsValid(Flat()));

    [Fact]
    public void ABreakdownWithLaborAndParts_IsValid() => Assert.True(IsValid(Breakdown(parts: [Part(), Part("Oil", 5m, 8.25m)])));

    [Fact]
    public void LaborOnly_AndPartsOnly_AreValid()
    {
        Assert.True(IsValid(Breakdown()));
        Assert.True(IsValid(new ServiceLineItemRequest { Description = "Wipers", Parts = [Part()] }));
    }

    [Fact]
    public void AFreeServiceIsAFlatCostOfZero() => Assert.True(IsValid(Flat(cost: 0m)));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    public void Description_IsRequiredAndTrimmed(string? description, bool valid) =>
        Assert.Equal(valid, IsValid(Flat(description)));

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Description_MaxLength200(int length, bool valid) => Assert.Equal(valid, IsValid(Flat(new string('a', length))));

    [Fact]
    public void Description_RejectsControlCharacters() => Assert.Equal(["Description"], ErrorFields(Flat("bad\u0007text")));

    [Theory]
    [InlineData("-10000000", false)]
    [InlineData("-9999999.99", true)]
    [InlineData("-4.04", true)]
    [InlineData("-0.01", true)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    [InlineData("-1.005", false)]
    public void FlatCost_RangeAndTwoDecimals(string cost, bool valid) =>
        Assert.Equal(valid, IsValid(Flat(cost: decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void AFlatCostOutOfRange_SaysTheAllowedRange() =>
        Assert.Equal(["Flat cost must be between -9,999,999.99 and 9,999,999.99."], Messages(Flat(cost: -10_000_000m)));

    [Fact]
    public void ADiscountWithLaborOrParts_IsRejectedAsBoth()
    {
        Assert.Equal(["Pricing"], ErrorFields(new ServiceLineItemRequest { Description = "Coupon", FlatCost = -4.04m, LaborHours = 1m, LaborRate = 10m }));
        Assert.Equal(["Pricing"], ErrorFields(new ServiceLineItemRequest { Description = "Coupon", FlatCost = -4.04m, Parts = [Part()] }));
    }

    private static ServiceLineItemRequest LaborTotal(decimal? laborCost, params ServicePartRequest[] parts) =>
        new() { Description = "Labor", LaborCost = laborCost, Parts = parts };

    [Fact]
    public void ALaborTotalAlone_IsValid() => Assert.True(IsValid(LaborTotal(180m)));

    [Fact]
    public void ALaborTotalWithParts_IsValid() => Assert.True(IsValid(LaborTotal(180m, Part())));

    [Fact]
    public void AZeroLaborTotal_IsValid() => Assert.True(IsValid(LaborTotal(0m)));

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void LaborTotal_RangeAndTwoDecimals(string total, bool valid) =>
        Assert.Equal(valid, IsValid(LaborTotal(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void HoursAndRateWithATypedLaborTotal_AreValid_BecauseTheCalculationWins() =>
        Assert.True(IsValid(new ServiceLineItemRequest { Description = "Labor", LaborHours = 2.5m, LaborRate = 120m, LaborCost = 999.99m }));

    [Fact]
    public void ALaborTotalWithAFlatCost_IsRejectedAsBoth()
    {
        var request = new ServiceLineItemRequest { Description = "x", FlatCost = 10m, LaborCost = 5m };

        Assert.Equal(["Pricing"], ErrorFields(request));
        Assert.Equal(["Use either a flat cost or labor and parts, not both."], Messages(request));
    }

    [Fact]
    public void ALaborTotalPlusPartsAboveTheMaximum_IsRejected() =>
        Assert.Equal(["Pricing"], ErrorFields(LaborTotal(9_999_999.99m, Part("Extra", 1m, 1m))));

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.01", true)]
    [InlineData("999.99", true)]
    [InlineData("1000", false)]
    [InlineData("1.005", false)]
    public void LaborHours_RangeAndTwoDecimals(string hours, bool valid) =>
        Assert.Equal(valid, IsValid(Breakdown(hours: decimal.Parse(hours, System.Globalization.CultureInfo.InvariantCulture))));

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("99999.99", true)]
    [InlineData("100000", false)]
    [InlineData("1.005", false)]
    public void LaborRate_RangeAndTwoDecimals(string rate, bool valid) =>
        Assert.Equal(valid, IsValid(Breakdown(rate: decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void LaborHoursWithoutARate_IsAnErrorOnTheRate() =>
        Assert.Equal(["LaborRate"], ErrorFields(Breakdown(hours: 1m, rate: null, Part())));

    [Fact]
    public void ALaborRateWithoutHours_IsAnErrorOnTheHours() =>
        Assert.Equal(["LaborHours"], ErrorFields(Breakdown(hours: null, rate: 50m, Part())));

    [Fact]
    public void AFlatCostWithLabor_IsRejectedAsBoth()
    {
        var request = new ServiceLineItemRequest { Description = "x", FlatCost = 10m, LaborHours = 1m, LaborRate = 10m };

        Assert.Equal(["Pricing"], ErrorFields(request));
        Assert.Equal(["Use either a flat cost or labor and parts, not both."], Messages(request));
    }

    [Fact]
    public void AFlatCostWithAPart_IsRejectedAsBoth()
    {
        var request = new ServiceLineItemRequest { Description = "x", FlatCost = 10m, Parts = [Part()] };

        Assert.Equal(["Pricing"], ErrorFields(request));
    }

    [Fact]
    public void NoPricingAtAll_IsRejected()
    {
        var request = new ServiceLineItemRequest { Description = "x" };

        Assert.Equal(["Pricing"], ErrorFields(request));
        Assert.Equal(["Enter a flat cost, labor (hours and a rate, or a total), or at least one part."], Messages(request));
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Parts_AtMost25(int count, bool valid) =>
        Assert.Equal(valid, IsValid(Breakdown(hours: null, rate: null, [.. Enumerable.Range(0, count).Select(i => Part($"Part {i}", 1m, 1m))])));

    [Fact]
    public void ThePartErrorsAreKeyedByRowAndField() =>
        Assert.Equal(
            ["Parts[1].Name", "Parts[1].Quantity", "Parts[1].UnitPrice"],
            ErrorFields(Breakdown(parts: [Part(), Part(null, null, null)])));

    [Fact]
    public void AComputedLineTotalAboveTheMaximum_IsRejected()
    {
        var request = Breakdown(hours: 999.99m, rate: 99999.99m, parts: [Part("Engine", 9999.99m, 9_999_999.99m)]);

        Assert.Equal(["Pricing"], ErrorFields(request));
    }
}

public class ServicePartRequestValidatorTests
{
    private readonly ServicePartRequestValidator validator = new();

    private static ServicePartRequest Part(string? name = "Oil filter", decimal? quantity = 1m, decimal? price = 12.50m) =>
        new() { Name = name, Quantity = quantity, UnitPrice = price };

    private bool IsValid(ServicePartRequest request) => validator.Validate(request).IsValid;

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("a", true)]
    public void Name_Required(string? name, bool valid) => Assert.Equal(valid, IsValid(Part(name)));

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Name_MaxLength100(int length, bool valid) => Assert.Equal(valid, IsValid(Part(new string('a', length))));

    [Fact]
    public void Name_RejectsControlCharacters() => Assert.False(IsValid(Part("bad\u0007name")));

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.01", true)]
    [InlineData("9999.99", true)]
    [InlineData("10000", false)]
    [InlineData("1.005", false)]
    public void Quantity_RangeAndTwoDecimals(string quantity, bool valid) =>
        Assert.Equal(valid, IsValid(Part(quantity: decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void Quantity_Required() => Assert.False(IsValid(Part(quantity: null)));

    [Theory]
    [InlineData("-0.01", false)]
    [InlineData("0", true)]
    [InlineData("9999999.99", true)]
    [InlineData("10000000", false)]
    [InlineData("1.005", false)]
    public void UnitPrice_RangeAndTwoDecimals(string price, bool valid) =>
        Assert.Equal(valid, IsValid(Part(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))));

    [Fact]
    public void UnitPrice_Required() => Assert.False(IsValid(Part(price: null)));
}

using MyGarage.Api.Common;

namespace MyGarage.Api.Tests.Common;

public class MoneyRulesTests
{
    [Fact]
    public void MaxAmount_IsNineMillionNineHundredNinetyNineThousandNineHundredNinetyNineAndNinetyNineCents() =>
        Assert.Equal(9_999_999.99m, MoneyRules.MaxAmount);

    [Theory]
    [InlineData(null, 2)]
    [InlineData("1.5", 2)]
    [InlineData("1.50", 2)]
    [InlineData("1.005", 3)]
    [InlineData("0.1342", 4)]
    public void HasAtMostDecimals_AcceptsValuesWithinThePlaces(string? value, int places) =>
        Assert.True(MoneyRules.HasAtMostDecimals(value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), places));

    [Theory]
    [InlineData("1.005", 2)]
    [InlineData("1.00001", 4)]
    [InlineData("0.1342", 3)]
    public void HasAtMostDecimals_RejectsValuesBeyondThePlaces(string value, int places) =>
        Assert.False(MoneyRules.HasAtMostDecimals(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), places));

    [Fact]
    public void HasAtMostTwoDecimals_StillBehavesAsBefore()
    {
        Assert.True(MoneyRules.HasAtMostTwoDecimals(null));
        Assert.True(MoneyRules.HasAtMostTwoDecimals(12.34m));
        Assert.False(MoneyRules.HasAtMostTwoDecimals(12.345m));
    }
}

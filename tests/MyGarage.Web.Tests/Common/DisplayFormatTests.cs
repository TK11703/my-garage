using System.Globalization;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class DisplayFormatTests
{
    [Fact]
    public void Money_UsesDollarSignTwoDecimalsAndThousandsSeparators()
    {
        Assert.Equal("$1,234.50", DisplayFormat.Money(1234.5m));
        Assert.Equal("$0.00", DisplayFormat.Money(0));
        Assert.Equal("-$3.00", DisplayFormat.Money(-3m));
    }

    [Fact]
    public void Date_IsReadable() => Assert.Equal("Nov 2, 2026", DisplayFormat.Date(new DateOnly(2026, 11, 2)));

    [Fact]
    public void Number_TrimsTrailingZeros() => Assert.Equal("3.5", DisplayFormat.Number(3.50m));

    [Fact]
    public void NotSet_IsTheSharedText() => Assert.Equal("Not set", DisplayFormat.NotSet);

    [Theory]
    [InlineData("3.459", "$3.459")]
    [InlineData("0.1342", "$0.1342")]
    [InlineData("4", "$4.00")]
    [InlineData("3.4", "$3.40")]
    public void UnitPrice_ShowsAtLeastTwoAndAtMostFourDecimals(string price, string expected) =>
        Assert.Equal(expected, DisplayFormat.UnitPrice(decimal.Parse(price, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("12.345", "12.345")]
    [InlineData("28.5", "28.5")]
    [InlineData("10", "10")]
    public void Quantity_ShowsUpToThreeDecimalsTrimmed(string quantity, string expected) =>
        Assert.Equal(expected, DisplayFormat.Quantity(decimal.Parse(quantity, CultureInfo.InvariantCulture)));

    [Fact]
    public void Output_IgnoresTheThreadCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("$1,234.50", DisplayFormat.Money(1234.5m));
            Assert.Equal("$0.1342", DisplayFormat.UnitPrice(0.1342m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

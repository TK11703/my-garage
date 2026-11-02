using MyGarage.Database.ServiceRecords;

namespace MyGarage.Database.Tests.ServiceRecords;

public class ServiceCostCalculatorTests
{
    private static readonly (decimal Quantity, decimal UnitPrice)[] NoParts = [];

    [Theory]
    [InlineData(40.00, 40.00)]
    [InlineData(0.00, 0.00)]
    [InlineData(9999999.99, 9999999.99)]
    public void LineTotal_FlatCost_IsTheFlatCost(double flat, double expected) =>
        Assert.Equal((decimal)expected, ServiceCostCalculator.LineTotal((decimal)flat, null, null, NoParts));

    [Theory]
    [InlineData(1.5, 90.00, 135.00)]
    [InlineData(0.33, 10.00, 3.30)]
    [InlineData(0.07, 12.50, 0.88)]
    public void LaborCost_RoundsToTwoDecimalsAwayFromZero(double hours, double rate, double expected) =>
        Assert.Equal((decimal)expected, ServiceCostCalculator.LaborCost((decimal)hours, (decimal)rate));

    [Fact]
    public void PartTotal_RoundsEachPartToTwoDecimalsAwayFromZero() =>
        Assert.Equal(8.33m, ServiceCostCalculator.PartTotal(2.5m, 3.33m));

    [Fact]
    public void LineTotal_SumsRoundedPartTotals() =>
        Assert.Equal(16.66m, ServiceCostCalculator.LineTotal(null, null, null, [(2.5m, 3.33m), (2.5m, 3.33m)]));

    [Fact]
    public void LineTotal_LaborAndParts_AddsAllOfThem() =>
        Assert.Equal(188.75m, ServiceCostCalculator.LineTotal(null, 1.5m, 90.00m, [(1m, 12.50m), (5m, 8.25m)]));

    [Fact]
    public void LineTotal_PartsOnly_SumsTheParts() =>
        Assert.Equal(53.75m, ServiceCostCalculator.LineTotal(null, null, null, [(1m, 12.50m), (5m, 8.25m)]));

    [Fact]
    public void LineTotal_LaborOnly_IsTheLaborCost() =>
        Assert.Equal(135.00m, ServiceCostCalculator.LineTotal(null, 1.5m, 90.00m, NoParts));

    [Fact]
    public void LineTotal_FlatCostIgnoresOtherInputs() =>
        Assert.Equal(40.00m, ServiceCostCalculator.LineTotal(40.00m, 1m, 10m, [(1m, 5m)]));
}

using MyGarage.Database.ServiceRecords;

namespace MyGarage.Database.Tests.ServiceRecords;

public class ServiceCostCalculatorTests
{
    private static readonly (decimal Quantity, decimal UnitPrice)[] NoParts = [];

    [Theory]
    [InlineData(40.00, 40.00)]
    [InlineData(0.00, 0.00)]
    [InlineData(9999999.99, 9999999.99)]
    [InlineData(-4.04, -4.04)]
    [InlineData(-9999999.99, -9999999.99)]
    public void LineTotal_FlatCost_IsTheFlatCost(double flat, double expected) =>
        Assert.Equal((decimal)expected, ServiceCostCalculator.LineTotal((decimal)flat, null, null, null, NoParts));

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
        Assert.Equal(16.66m, ServiceCostCalculator.LineTotal(null, null, null, null, [(2.5m, 3.33m), (2.5m, 3.33m)]));

    [Fact]
    public void LineTotal_LaborAndParts_AddsAllOfThem() =>
        Assert.Equal(188.75m, ServiceCostCalculator.LineTotal(null, 1.5m, 90.00m, null, [(1m, 12.50m), (5m, 8.25m)]));

    [Fact]
    public void LineTotal_PartsOnly_SumsTheParts() =>
        Assert.Equal(53.75m, ServiceCostCalculator.LineTotal(null, null, null, null, [(1m, 12.50m), (5m, 8.25m)]));

    [Fact]
    public void LineTotal_LaborOnly_IsTheLaborCost() =>
        Assert.Equal(135.00m, ServiceCostCalculator.LineTotal(null, 1.5m, 90.00m, null, NoParts));

    [Fact]
    public void LineTotal_FlatCostIgnoresOtherInputs() =>
        Assert.Equal(40.00m, ServiceCostCalculator.LineTotal(40.00m, 1m, 10m, 5m, [(1m, 5m)]));

    [Fact]
    public void LineTotal_EnteredLaborCostAlone_IsThatAmount() =>
        Assert.Equal(180.00m, ServiceCostCalculator.LineTotal(null, null, null, 180.00m, NoParts));

    [Fact]
    public void LineTotal_EnteredLaborCostAndParts_AddsThem() =>
        Assert.Equal(205.00m, ServiceCostCalculator.LineTotal(null, null, null, 180.00m, [(1m, 25.00m)]));

    [Fact]
    public void LineTotal_HoursAndRateWin_OverASuppliedLaborCost() =>
        Assert.Equal(300.00m, ServiceCostCalculator.LineTotal(null, 2.5m, 120.00m, 999.99m, NoParts));

    [Theory]
    [InlineData(2.5, 120.00, null, 300.00)]
    [InlineData(2.5, 120.00, 999.99, 300.00)]
    [InlineData(null, null, 180.00, 180.00)]
    [InlineData(null, null, null, null)]
    public void EffectiveLaborCost_IsCalculatedWhenBothArePresent_OtherwiseTheEnteredAmount(double? hours, double? rate, double? entered, double? expected) =>
        Assert.Equal(
            (decimal?)expected,
            ServiceCostCalculator.EffectiveLaborCost((decimal?)hours, (decimal?)rate, (decimal?)entered));

    [Fact]
    public void LineTotal_NegativePartsRoundAwayFromZero() =>
        Assert.Equal(-8.33m, ServiceCostCalculator.LineTotal(null, null, null, null, [(2.5m, -3.33m)]));
}

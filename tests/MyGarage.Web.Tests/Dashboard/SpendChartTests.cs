using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Web.Dashboard;

namespace MyGarage.Web.Tests.Dashboard;

public class SpendChartTests : BunitContext
{
    private static readonly Guid Truck = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Sedan = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static FuelEnergySpendVehicleDto Vehicle(Guid id, string name, decimal fuel, decimal charging) =>
        new() { VehicleId = id, VehicleName = name, Fuel = fuel, Charging = charging };

    private static FuelEnergySpendMonthDto Month(int year, int month, params FuelEnergySpendVehicleDto[] vehicles) =>
        new() { Month = new DateOnly(year, month, 1), Fuel = vehicles.Sum(v => v.Fuel), Charging = vehicles.Sum(v => v.Charging), Vehicles = vehicles };

    // Twelve months, November 2025 through October 2026; the callback supplies each month's vehicles.
    private static IReadOnlyList<FuelEnergySpendMonthDto> Year(Func<int, FuelEnergySpendVehicleDto[]> vehicles) =>
        [.. Enumerable.Range(0, 12).Select(index => Month(2025 + (index + 10) / 12, (index + 10) % 12 + 1, vehicles(index)))];

    private IRenderedComponent<SpendChart> RenderChart(IReadOnlyList<FuelEnergySpendMonthDto> months) =>
        Render<SpendChart>(parameters => parameters.Add(chart => chart.Months, months));

    private static double Height(AngleSharp.Dom.IElement rect) =>
        double.Parse(rect.GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void DrawsAFuelChartAndAChargingChart_EachWithOneBarPerMonth()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 50m, 10m)]));

        Assert.Equal(12, cut.FindAll("[data-testid='chart-fuel'] [data-testid='month']").Count);
        Assert.Equal(12, cut.FindAll("[data-testid='chart-charging'] [data-testid='month']").Count);
        Assert.Equal("Fuel $600.00", cut.Find("[data-testid='fuel-panel'] h3").TextContent.Trim());
        Assert.Equal("Charging $120.00", cut.Find("[data-testid='charging-panel'] h3").TextContent.Trim());
    }

    [Fact]
    public void EachBarIsStackedByVehicle_AndTheLegendNamesEveryVehicle()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 60m, 0m), Vehicle(Sedan, "2018 Honda Civic", 20m, 0m)]));

        var firstFuelBar = cut.Find("[data-testid='chart-fuel'] [data-testid='month']").QuerySelectorAll("rect");
        Assert.Equal(2, firstFuelBar.Length);
        Assert.Equal(Truck.ToString(), firstFuelBar[0].GetAttribute("data-series"));
        Assert.Equal(Sedan.ToString(), firstFuelBar[1].GetAttribute("data-series"));
        Assert.Equal(Height(firstFuelBar[0]) / 3, Height(firstFuelBar[1]), 1);
        var legend = cut.FindAll("[data-testid='spend-legend'] li").Select(item => item.TextContent.Trim()).ToList();
        Assert.Equal(["2020 Ford F-150", "2018 Honda Civic"], legend);
    }

    [Fact]
    public void TheVehicleWithTheMostSpendSitsAtTheBottomAndGetsTheFirstColor()
    {
        var cut = RenderChart(Year(index => [Vehicle(Sedan, "2018 Honda Civic", 10m, 0m), Vehicle(Truck, "2020 Ford F-150", index == 0 ? 5m : 90m, 0m)]));

        var legend = cut.FindAll("[data-testid='spend-legend'] li");
        Assert.Contains("mg-chart-1", legend[0].ClassName);
        Assert.Contains("2020 Ford F-150", legend[0].TextContent);
        Assert.Contains("mg-chart-2", legend[1].ClassName);
        var bottom = cut.Find("[data-testid='chart-fuel'] [data-testid='month'] rect");
        Assert.Equal(Truck.ToString(), bottom.GetAttribute("data-series"));
    }

    [Fact]
    public void FuelAndChargingAreSeparateCharts_SoAVehicleThatOnlyChargesAppearsOnlyInTheChargingChart()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 50m, 0m), Vehicle(Sedan, "2023 Tesla Model 3", 0m, 30m)]));

        var fuelSeries = cut.FindAll("[data-testid='chart-fuel'] rect").Select(rect => rect.GetAttribute("data-series")).Distinct().ToList();
        var chargingSeries = cut.FindAll("[data-testid='chart-charging'] rect").Select(rect => rect.GetAttribute("data-series")).Distinct().ToList();
        Assert.Equal([Truck.ToString()], fuelSeries);
        Assert.Equal([Sedan.ToString()], chargingSeries);
    }

    [Fact]
    public void MoreThanSevenVehicles_ShareOneGrayOtherLayer()
    {
        var ids = Enumerable.Range(1, 9).Select(n => Guid.Parse($"00000000-0000-0000-0000-{n:D12}")).ToList();
        var cut = RenderChart(Year(_ => [.. ids.Select((id, index) => Vehicle(id, $"Vehicle {index + 1}", 100m - index * 10, 0m))]));

        var legend = cut.FindAll("[data-testid='spend-legend'] li").Select(item => item.TextContent.Trim()).ToList();
        Assert.Equal(8, legend.Count);
        Assert.Equal("Other vehicles", legend[^1]);
        var firstFuelBar = cut.Find("[data-testid='chart-fuel'] [data-testid='month']").QuerySelectorAll("rect");
        Assert.Equal(8, firstFuelBar.Length);
        Assert.Equal("other", firstFuelBar[^1].GetAttribute("data-series"));
        Assert.Contains("mg-chart-other", firstFuelBar[^1].ClassName);
        // Vehicles 8 and 9 (30 and 20) are summed into the Other layer, half the height of the 100 layer.
        var layers = firstFuelBar.Select(Height).ToList();
        Assert.Equal(layers[0] * (30d + 20d) / 100d, layers[^1], 1);
    }

    [Fact]
    public void TheAxisCoversTheTallestBar_WithRoundLabels()
    {
        var cut = RenderChart(Year(index => index == 3 ? [Vehicle(Truck, "2020 Ford F-150", 87.50m, 0m)] : []));

        var labels = cut.FindAll("[data-testid='chart-fuel'] text[text-anchor='end']").Select(text => text.TextContent).ToList();
        Assert.Equal(["$0", "$25", "$50", "$75", "$100"], labels);
    }

    [Fact]
    public void EveryBarHasATooltipNamingTheMonthKindVehicleAndCost()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 10m, 5m)]));

        Assert.Contains("Nov 2025 fuel, 2020 Ford F-150: $10.00", cut.Find("[data-testid='chart-fuel'] rect title").TextContent);
        Assert.Contains("Nov 2025 charging, 2020 Ford F-150: $5.00", cut.Find("[data-testid='chart-charging'] rect title").TextContent);
    }

    [Fact]
    public void EachChartIsNamedForAssistiveTechnology()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 10m, 5m)]));

        var fuel = cut.Find("[data-testid='chart-fuel'] svg[role='img']");
        Assert.Equal("fuel-chart-title", fuel.GetAttribute("aria-labelledby"));
        Assert.Contains("stacked by vehicle", cut.Find("#fuel-chart-title").TextContent);
        Assert.Equal("charging-chart-title", cut.Find("[data-testid='chart-charging'] svg[role='img']").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void TheFiguresTableListsEachMonthAndVehicle_AndMarksMonthsWithNoEntries()
    {
        var cut = RenderChart(Year(index => index == 0 ? [Vehicle(Truck, "2020 Ford F-150", 100m, 20m), Vehicle(Sedan, "2018 Honda Civic", 30m, 0m)] : []));

        var rows = cut.FindAll("[data-testid='figures-row']").Select(row => row.TextContent.Replace("\n", " ").Trim()).ToList();
        Assert.Equal(13, rows.Count);
        Assert.Contains("Nov 2025", rows[0]);
        Assert.Contains("2018 Honda Civic", rows[0]);
        Assert.Contains("$30.00", rows[0]);
        Assert.Contains("2020 Ford F-150", rows[1]);
        Assert.Contains("$100.00", rows[1]);
        Assert.Contains("$20.00", rows[1]);
        Assert.Contains("No entries", rows[2]);
    }

    [Fact]
    public void LabelsTheMonthsAndMarksTheYear()
    {
        var cut = RenderChart(Year(_ => [Vehicle(Truck, "2020 Ford F-150", 1m, 1m)]));

        var monthLabels = cut.FindAll("[data-testid='chart-fuel'] [data-testid='month'] text").Select(text => text.TextContent).ToList();
        Assert.Contains("Nov", monthLabels);
        Assert.Contains("2025", monthLabels);
        Assert.Contains("2026", monthLabels);
    }
}

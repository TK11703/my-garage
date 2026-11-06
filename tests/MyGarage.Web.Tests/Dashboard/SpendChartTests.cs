using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Web.Dashboard;

namespace MyGarage.Web.Tests.Dashboard;

public class SpendChartTests : BunitContext
{
    private static FuelEnergySpendMonthDto Month(int year, int month, decimal fuel, decimal charging) =>
        new() { Month = new DateOnly(year, month, 1), Fuel = fuel, Charging = charging };

    private static IReadOnlyList<FuelEnergySpendMonthDto> Year(Func<int, (decimal Fuel, decimal Charging)> amounts) =>
        [.. Enumerable.Range(0, 12).Select(index => { var (fuel, charging) = amounts(index); return Month(2025 + (index + 10) / 12, (index + 10) % 12 + 1, fuel, charging); })];

    private IRenderedComponent<SpendChart> RenderChart(IReadOnlyList<FuelEnergySpendMonthDto> months) =>
        Render<SpendChart>(parameters => parameters.Add(chart => chart.Months, months));

    [Fact]
    public void DrawsOneFuelBarAndOneChargingBarPerMonth_InSeparateSeries()
    {
        var cut = RenderChart(Year(index => (10m * (index + 1), index % 2 == 0 ? 5m : 0m)));

        Assert.Equal(12, cut.FindAll("rect[data-series='fuel']").Count);
        Assert.Equal(12, cut.FindAll("rect[data-series='charging']").Count);
        Assert.Equal(12, cut.FindAll("[data-testid='month']").Count);
    }

    [Fact]
    public void BarHeightsAreProportionalToTheCost()
    {
        var cut = RenderChart(Year(index => index == 0 ? (50m, 25m) : (0m, 0m)));

        var fuel = double.Parse(cut.Find("rect[data-series='fuel']").GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);
        var charging = double.Parse(cut.Find("rect[data-series='charging']").GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(fuel > 0);
        Assert.Equal(fuel / 2, charging, 1);
    }

    [Fact]
    public void TheAxisCoversTheLargestMonth_WithRoundLabels()
    {
        var cut = RenderChart(Year(index => index == 3 ? (87.50m, 12m) : (0m, 0m)));

        var labels = cut.FindAll("text[text-anchor='end']").Select(text => text.TextContent).ToList();
        Assert.Equal(["$0", "$25", "$50", "$75", "$100"], labels);
    }

    [Fact]
    public void TheLegendAndTheTableGiveEachSeriesItsTotalAndEveryMonthItsFigures()
    {
        var cut = RenderChart(Year(index => index < 2 ? (100m, 20m) : (0m, 0m)));

        Assert.Contains("Fuel $200.00", cut.Find("[data-testid='legend-fuel']").TextContent);
        Assert.Contains("Charging $40.00", cut.Find("[data-testid='legend-charging']").TextContent);
        var rows = cut.FindAll("[data-testid='spend-table'] tbody tr");
        Assert.Equal(12, rows.Count);
        Assert.Contains("Nov 2025", rows[0].TextContent);
        Assert.Contains("$100.00", rows[0].TextContent);
        Assert.Contains("$20.00", rows[0].TextContent);
    }

    [Fact]
    public void TheChartIsNamedForAssistiveTechnology_AndEveryBarHasATooltip()
    {
        var cut = RenderChart(Year(index => (10m, 5m)));

        var svg = cut.Find("svg[role='img']");
        Assert.Equal("spend-chart-title", svg.GetAttribute("aria-labelledby"));
        Assert.Contains("last 12 months", cut.Find("#spend-chart-title").TextContent);
        Assert.Contains("Nov 2025 fuel: $10.00", cut.Find("rect[data-series='fuel'] title").TextContent);
        Assert.Contains("Nov 2025 charging: $5.00", cut.Find("rect[data-series='charging'] title").TextContent);
    }

    [Fact]
    public void LabelsTheMonthsAndMarksTheYear()
    {
        var cut = RenderChart(Year(_ => (1m, 1m)));

        var monthLabels = cut.FindAll("[data-testid='month'] text").Select(text => text.TextContent).ToList();
        Assert.Contains("Nov", monthLabels);
        Assert.Contains("2025", monthLabels);
        Assert.Contains("2026", monthLabels);
    }
}

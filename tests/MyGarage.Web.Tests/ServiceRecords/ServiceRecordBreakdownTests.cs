using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;

namespace MyGarage.Web.Tests.ServiceRecords;

public sealed class ServiceRecordBreakdownTests
{
    private static ServiceLineItemDto Line(decimal? flat = null, decimal? labor = null, params decimal[] parts) =>
        new()
        {
            Id = Guid.NewGuid(),
            Description = "Work",
            FlatCost = flat,
            LaborCost = labor,
            Parts = [.. parts.Select(price => new ServicePartDto { Name = "Part", Quantity = 1m, UnitPrice = price, TotalPrice = price })],
            TotalCost = (flat ?? 0m) + (labor ?? 0m) + parts.Sum(),
        };

    private static ServiceRecordDto Record(decimal? tax, params ServiceLineItemDto[] lines) =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            DateServiced = new DateOnly(2026, 10, 30),
            OdometerReading = 90_100,
            LineItems = lines,
            Tax = tax,
            TotalCost = lines.Sum(line => line.TotalCost) + (tax ?? 0m),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    [Fact]
    public void Breakdown_SplitsLaborPartsFlatTaxAndCredit_InThatOrder()
    {
        var record = Record(8.25m, Line(flat: 100m), Line(flat: -4.04m), Line(labor: 180m), Line(null, null, 12.50m));

        var shares = ServiceRecordFormat.Breakdown(record);

        Assert.Equal(["labor", "parts", "flat", "tax", "credit"], shares.Select(share => share.Key));
        Assert.Equal([180m, 12.50m, 100m, 8.25m, -4.04m], shares.Select(share => share.Amount));
    }

    [Fact]
    public void Breakdown_LeavesOutCategoriesWithNothingInThem()
    {
        var shares = ServiceRecordFormat.Breakdown(Record(null, Line(flat: 40m)));

        Assert.Equal(["flat"], shares.Select(share => share.Key));
    }

    [Fact]
    public void Breakdown_OfARecordWithNoLinesAndNoTax_IsEmpty() =>
        Assert.Empty(ServiceRecordFormat.Breakdown(Record(null)));

    [Theory]
    [InlineData(40.0, null, 0, "flat", "Flat")]
    [InlineData(null, 180.0, 0, "labor", "Labor")]
    [InlineData(null, null, 1, "parts", "Parts")]
    [InlineData(null, 90.0, 1, "itemized", "Itemized")]
    [InlineData(-4.04, null, 0, "credit", "Credit")]
    public void LineKind_NamesHowTheLineWasPriced(double? flat, double? labor, int partCount, string key, string label)
    {
        var line = Line((decimal?)flat, (decimal?)labor, [.. Enumerable.Repeat(12m, partCount)]);

        var kind = ServiceRecordFormat.LineKind(line);

        Assert.Equal((key, label), (kind.Key, kind.Label));
    }
}

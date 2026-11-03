using System.Globalization;
using MyGarage.Web.FuelEnergyEntries;
using static MyGarage.Web.Tests.FuelEnergyEntries.FuelEnergyEntryTestData;

namespace MyGarage.Web.Tests.FuelEnergyEntries;

public class FuelEnergyEntryFormModelTests
{
    [Fact]
    public void From_ShowsThePriceAndAmountWithoutTrailingZeros()
    {
        var model = FuelEnergyEntryFormModel.From(Entry(unitPrice: 3.4590m, quantity: 28.500m, totalCost: 42.70m));

        Assert.Equal("3.459", model.UnitPrice!.Value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("28.5", model.Quantity!.Value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(42.70m, model.TotalCost);
    }

    [Fact]
    public void ToRequest_CarriesTheKindAndLeavesAnEmptyTotalEmpty()
    {
        var model = FuelEnergyEntryFormModel.For(MyGarage.Shared.FuelEnergyEntries.FuelEnergyKind.Energy);
        model.EntryDate = new DateOnly(2026, 5, 1);
        model.UnitPrice = 0.1342m;
        model.Quantity = 28.5m;

        var request = model.ToRequest();

        Assert.Equal(MyGarage.Shared.FuelEnergyEntries.FuelEnergyKind.Energy, request.Kind);
        Assert.Null(request.TotalCost);
        Assert.Equal(0.1342m, request.UnitPrice);
    }
}

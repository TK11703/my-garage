using Bunit;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCardYearTests : VehicleTestContext
{
    private IRenderedComponent<VehicleCard> RenderCard(VehicleSummaryDto vehicle) =>
        Render<VehicleCard>(parameters => parameters.Add(card => card.Vehicle, vehicle));

    [Fact]
    public void CardShowsTheYearWithTheMakeAndModel()
    {
        var cut = RenderCard(Summary("Daily", "Toyota", "Tacoma", year: 2018));

        Assert.Equal("2018 Toyota Tacoma", cut.Find("[data-testid='vehicle-make-model']").TextContent.Trim());
    }

    [Fact]
    public void CardOmitsTheYearWhenItIsMissing()
    {
        var cut = RenderCard(Summary("Legacy", "Ford", "F-150", year: null));

        Assert.Equal("Ford F-150", cut.Find("[data-testid='vehicle-make-model']").TextContent.Trim());
    }
}

public class VehicleListCriteriaTests
{
    private static readonly Guid TypeId = Guid.NewGuid();
    private static readonly Guid FuelId = Guid.NewGuid();

    private static VehicleListCriteria Parse(
        string? q = null,
        string? minYear = null,
        string? maxYear = null,
        string? vehicleTypeId = null,
        string? fuelTypeId = null,
        string? transmissionId = null,
        string? drivetrainId = null) =>
        VehicleListCriteria.FromQuery(q, null, null, null, null, null, null, minYear, maxYear, vehicleTypeId, fuelTypeId, transmissionId, drivetrainId);

    [Fact]
    public void NewKeys_AreParsedAndRoundTripThroughTheQueryString()
    {
        var criteria = Parse("tacoma", "2000", "2020", TypeId.ToString(), FuelId.ToString());

        Assert.Equal(2000, criteria.MinYear);
        Assert.Equal(2020, criteria.MaxYear);
        Assert.Equal(TypeId, criteria.VehicleTypeId);
        Assert.Equal(FuelId, criteria.FuelTypeId);
        Assert.True(criteria.HasFilters);
        var query = criteria.ToQueryString(2);
        Assert.Contains("q=tacoma", query);
        Assert.Contains("minYear=2000", query);
        Assert.Contains("maxYear=2020", query);
        Assert.Contains($"vehicleTypeId={TypeId}", query);
        Assert.Contains($"fuelTypeId={FuelId}", query);
        Assert.EndsWith("page=2", query);
    }

    [Fact]
    public void NonNumericYearsAndNonGuidIds_AreIgnored()
    {
        var criteria = Parse(minYear: "abc", maxYear: "-5", vehicleTypeId: "not-a-guid", drivetrainId: "123");

        Assert.Null(criteria.MinYear);
        Assert.Null(criteria.MaxYear);
        Assert.Null(criteria.VehicleTypeId);
        Assert.Null(criteria.DrivetrainId);
        Assert.False(criteria.HasFilters);
    }

    [Fact]
    public void Active_ListsOneBadgePerCriterionUsingTheOptionNames()
    {
        var options = new VehicleFilterOptionsDto
        {
            Makes = [],
            Colors = [],
            Tags = [],
            VehicleTypes = [new LookupValueDto { Id = TypeId, Name = "Truck" }],
            FuelTypes = [new LookupValueDto { Id = FuelId, Name = "Diesel" }],
        };
        var criteria = Parse("tow", "2000", "2020", TypeId.ToString(), FuelId.ToString());

        var labels = criteria.Active(options).ToDictionary(badge => badge.Key, badge => badge.Label);

        Assert.Equal("Search: tow", labels["q"]);
        Assert.Equal("Min year: 2000", labels["minYear"]);
        Assert.Equal("Max year: 2020", labels["maxYear"]);
        Assert.Equal("Vehicle type: Truck", labels["vehicleTypeId"]);
        Assert.Equal("Fuel type: Diesel", labels["fuelTypeId"]);
    }

    [Fact]
    public void Active_WithoutOptionsNeverShowsAnIdentifier()
    {
        var labels = Parse(vehicleTypeId: TypeId.ToString()).Active(null).Select(badge => badge.Label).ToList();

        Assert.Equal(["Vehicle type: selected"], labels);
    }

    [Fact]
    public void Without_RemovesOnlyThatCriterion()
    {
        var criteria = Parse("tow", "2000", "2020", TypeId.ToString(), FuelId.ToString());

        var smaller = criteria.Without("minYear").Without("fuelTypeId");

        Assert.Null(smaller.MinYear);
        Assert.Null(smaller.FuelTypeId);
        Assert.Equal(2020, smaller.MaxYear);
        Assert.Equal(TypeId, smaller.VehicleTypeId);
        Assert.Equal("tow", smaller.Query);
    }
}

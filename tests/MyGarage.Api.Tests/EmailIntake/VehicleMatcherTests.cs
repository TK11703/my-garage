using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.Drafts;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Tests.EmailIntake;

public class VehicleMatcherTests
{
    private static VehicleMatchCandidate Vehicle(string make, string model, int? year = null, string? vin = null) =>
        new() { Id = Guid.NewGuid(), Make = make, Model = model, Year = year, Vin = vin };

    private static VehicleHint Hint(string? vin = null, int? year = null, string? make = null, string? model = null) =>
        new(vin, year, make, model, null);

    [Fact]
    public void APreselectedVehicle_IsTheOwnersChoice_EvenWhenTheHintPointsToAnotherVehicle()
    {
        var truck = Vehicle("Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        var car = Vehicle("Honda", "Civic", 2020, "2HGFC2F69LH500002");

        var result = VehicleMatcher.Match([truck, car], Hint(vin: "2HGFC2F69LH500002", make: "Honda", model: "Civic"), truck.Id);

        Assert.Equal(truck.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Selected, result.Match);
    }

    [Fact]
    public void APreselectedVehicle_IsUsedWhenThereIsNoHintAtAll()
    {
        var truck = Vehicle("Ford", "F-150", 2018);
        var car = Vehicle("Honda", "Civic", 2020);

        var result = VehicleMatcher.Match([truck, car], null, car.Id);

        Assert.Equal(car.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Selected, result.Match);
    }

    [Fact]
    public void APreselectedVehicleThatIsNotACandidate_FallsBackToTheNormalOrder()
    {
        var truck = Vehicle("Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        var car = Vehicle("Honda", "Civic", 2020);

        var byVin = VehicleMatcher.Match([truck, car], Hint(vin: "1FTEW1EP0JKD00001"), Guid.NewGuid());
        var none = VehicleMatcher.Match([truck, car], null, Guid.NewGuid());

        Assert.Equal(truck.Id, byVin.VehicleId);
        Assert.Equal(VehicleMatch.Vin, byVin.Match);
        Assert.Null(none.VehicleId);
        Assert.Equal(VehicleMatch.None, none.Match);
    }

    [Fact]
    public void ANullPreselectedVehicle_BehavesAsBefore()
    {
        var only = Vehicle("Ford", "F-150", 2018);

        var result = VehicleMatcher.Match([only], null, null);

        Assert.Equal(only.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.OnlyVehicle, result.Match);
    }

    [Fact]
    public void ExactVin_WinsEvenWhenTheDescriptionPointsElsewhere()
    {
        var truck = Vehicle("Ford", "F-150", 2018, "1FTEW1EP0JKD00001");
        var car = Vehicle("Honda", "Civic", 2020, "2HGFC2F69LH500002");

        var result = VehicleMatcher.Match([truck, car], Hint(vin: "  2hgfc2f69lh500002 ", year: 2018, make: "Ford", model: "F-150"));

        Assert.Equal(car.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Vin, result.Match);
    }

    [Fact]
    public void UniqueYearMakeAndModel_MatchesByDescription_IgnoringCaseAndSpaces()
    {
        var older = Vehicle("Ford", "F-150", 2012);
        var newer = Vehicle("Ford", "F-150", 2018);

        var result = VehicleMatcher.Match([older, newer], Hint(year: 2018, make: " ford ", model: "f-150"));

        Assert.Equal(newer.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Description, result.Match);
    }

    [Fact]
    public void UniqueMakeAndModel_WithoutAYear_MatchesByDescription()
    {
        var truck = Vehicle("Ford", "F-150", 2018);
        var car = Vehicle("Honda", "Civic", 2020);

        var result = VehicleMatcher.Match([truck, car], Hint(make: "Honda", model: "Civic"));

        Assert.Equal(car.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Description, result.Match);
    }

    [Fact]
    public void AmbiguousMakeAndModel_WithoutAVinOrYear_FallsThroughToNone_WhenThereAreSeveralVehicles()
    {
        var result = VehicleMatcher.Match([Vehicle("Ford", "F-150", 2012), Vehicle("Ford", "F-150", 2018), Vehicle("Honda", "Civic")], Hint(make: "Ford", model: "F-150"));

        Assert.Null(result.VehicleId);
        Assert.Equal(VehicleMatch.None, result.Match);
    }

    [Fact]
    public void AnUnknownYear_FallsBackToTheUniqueMakeAndModel()
    {
        var truck = Vehicle("Ford", "F-150", 2018);

        var result = VehicleMatcher.Match([truck, Vehicle("Honda", "Civic")], Hint(year: 2005, make: "Ford", model: "F-150"));

        Assert.Equal(truck.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Description, result.Match);
    }

    [Fact]
    public void TheOwnersOnlyVehicle_IsUsedWhenNothingElseMatches()
    {
        var only = Vehicle("Ford", "F-150", 2018);

        var result = VehicleMatcher.Match([only], Hint(make: "Toyota", model: "Camry"));

        Assert.Equal(only.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.OnlyVehicle, result.Match);
    }

    [Fact]
    public void NoHintAtAll_WithOneVehicle_IsOnlyVehicle()
    {
        var only = Vehicle("Ford", "F-150");

        Assert.Equal(VehicleMatch.OnlyVehicle, VehicleMatcher.Match([only], null).Match);
        Assert.Equal(only.Id, VehicleMatcher.Match([only], new VehicleHint(null, null, null, null, null)).VehicleId);
    }

    [Fact]
    public void SeveralVehiclesAndNoUniqueMatch_IsNone()
    {
        var result = VehicleMatcher.Match([Vehicle("Ford", "F-150"), Vehicle("Honda", "Civic")], Hint(make: "Toyota", model: "Camry"));

        Assert.Null(result.VehicleId);
        Assert.Equal(VehicleMatch.None, result.Match);
    }

    [Fact]
    public void NoVehicles_IsNone()
    {
        var result = VehicleMatcher.Match([], Hint(vin: "1FTEW1EP0JKD00001", make: "Ford", model: "F-150"));

        Assert.Null(result.VehicleId);
        Assert.Equal(VehicleMatch.None, result.Match);
    }

    [Fact]
    public void AVinThatMatchesNoVehicle_DoesNotBlockTheDescriptionMatch()
    {
        var truck = Vehicle("Ford", "F-150", 2018, "1FTEW1EP0JKD00001");

        var result = VehicleMatcher.Match([truck, Vehicle("Honda", "Civic")], Hint(vin: "ZZZ", make: "Ford", model: "F-150"));

        Assert.Equal(truck.Id, result.VehicleId);
        Assert.Equal(VehicleMatch.Description, result.Match);
    }
}

using MyGarage.Api.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleListQueryValidatorTests
{
    private readonly VehicleListQueryValidator validator = new();

    private bool IsValid(VehicleListQuery query) => validator.Validate(query).IsValid;

    [Fact]
    public void NoCriteria_IsValid() => Assert.True(IsValid(new VehicleListQuery()));

    [Fact]
    public void AllCriteria_AreValid() =>
        Assert.True(IsValid(new VehicleListQuery("truck", "Ford", "Red", 4, "tow package", 1_000, 50_000, 2, 12)));

    [Fact]
    public void Query_OptionalTrimmedMax100()
    {
        Assert.True(IsValid(new VehicleListQuery(Q: new string('a', 100))));
        Assert.True(IsValid(new VehicleListQuery(Q: "   ")));
        Assert.True(IsValid(new VehicleListQuery(Q: " " + new string('a', 100) + " ")));
        Assert.False(IsValid(new VehicleListQuery(Q: new string('a', 101))));
    }

    [Fact]
    public void Query_RejectsControlCharacters() =>
        Assert.False(IsValid(new VehicleListQuery(Q: "bad\u0007")));

    [Fact]
    public void MakeColorAndTag_HaveLengthLimits()
    {
        Assert.True(IsValid(new VehicleListQuery(Make: new string('a', 50), Color: new string('a', 30), Tag: new string('a', 30))));
        Assert.False(IsValid(new VehicleListQuery(Make: new string('a', 51))));
        Assert.False(IsValid(new VehicleListQuery(Color: new string('a', 31))));
        Assert.False(IsValid(new VehicleListQuery(Tag: new string('a', 31))));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    public void Doors_Range(int doors, bool valid) => Assert.Equal(valid, IsValid(new VehicleListQuery(Doors: doors)));

    [Theory]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(-1, false)]
    [InlineData(10_000_000, false)]
    public void MileageBounds_Range(int mileage, bool valid)
    {
        Assert.Equal(valid, IsValid(new VehicleListQuery(MinMileage: mileage)));
        Assert.Equal(valid, IsValid(new VehicleListQuery(MaxMileage: mileage)));
    }

    [Fact]
    public void MinMileageGreaterThanMax_IsInvalid()
    {
        Assert.False(IsValid(new VehicleListQuery(MinMileage: 5_000, MaxMileage: 1_000)));
        Assert.True(IsValid(new VehicleListQuery(MinMileage: 1_000, MaxMileage: 1_000)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void Paging_RulesApply(int page, int? pageSize) =>
        Assert.False(IsValid(new VehicleListQuery(Page: page, PageSize: pageSize)));
}

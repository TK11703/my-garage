using MyGarage.Api.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleRequestValidatorTests
{
    private readonly VehicleRequestValidator validator = new();

    private static VehicleRequest Valid(
        string? nickname = "Daily",
        string? make = "Ford",
        string? model = "F-150",
        string? trim = null,
        string? color = null,
        int? mileage = null,
        int? doors = null) =>
        new() { Nickname = nickname, Make = make, Model = model, Trim = trim, Color = color, Mileage = mileage, Doors = doors };

    private bool IsValid(VehicleRequest request) => validator.Validate(request).IsValid;

    [Fact]
    public void MinimalRequest_IsValid() => Assert.True(IsValid(Valid()));

    [Fact]
    public void FullRequest_IsValid() =>
        Assert.True(IsValid(Valid(trim: "XLT", color: "Blue", mileage: 100_000, doors: 4)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nickname_Required(string? nickname) => Assert.False(IsValid(Valid(nickname: nickname)));

    [Fact]
    public void Nickname_MaxLength100IgnoringSurroundingSpaces()
    {
        Assert.True(IsValid(Valid(nickname: new string('a', 100))));
        Assert.True(IsValid(Valid(nickname: " " + new string('a', 100) + " ")));
        Assert.False(IsValid(Valid(nickname: new string('a', 101))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MakeAndModel_Required(string? value)
    {
        Assert.False(IsValid(Valid(make: value)));
        Assert.False(IsValid(Valid(model: value)));
    }

    [Fact]
    public void MakeAndModel_MaxLength50()
    {
        Assert.True(IsValid(Valid(make: new string('a', 50), model: new string('a', 50))));
        Assert.False(IsValid(Valid(make: new string('a', 51))));
        Assert.False(IsValid(Valid(model: new string('a', 51))));
    }

    [Fact]
    public void Trim_OptionalMaxLength50()
    {
        Assert.True(IsValid(Valid(trim: new string('a', 50))));
        Assert.False(IsValid(Valid(trim: new string('a', 51))));
    }

    [Fact]
    public void Color_OptionalMaxLength30()
    {
        Assert.True(IsValid(Valid(color: new string('a', 30))));
        Assert.False(IsValid(Valid(color: new string('a', 31))));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(-1, false)]
    [InlineData(10_000_000, false)]
    public void Mileage_Range(int mileage, bool valid) => Assert.Equal(valid, IsValid(Valid(mileage: mileage)));

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    public void Doors_Range(int doors, bool valid) => Assert.Equal(valid, IsValid(Valid(doors: doors)));

    [Fact]
    public void ControlCharacters_Rejected()
    {
        Assert.False(IsValid(Valid(nickname: "bad\u0007name")));
        Assert.False(IsValid(Valid(make: "bad\nmake")));
        Assert.False(IsValid(Valid(model: "bad\tmodel")));
        Assert.False(IsValid(Valid(trim: "bad\u0000trim")));
        Assert.False(IsValid(Valid(color: "bad\rcolor")));
    }

    [Fact]
    public void QuotesAndAngleBrackets_Allowed() =>
        Assert.True(IsValid(Valid(nickname: "Bob's \"<b>truck</b>\"")));
}

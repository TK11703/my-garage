using MyGarage.Api.Vehicles.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class AddVehicleTagRequestValidatorTests
{
    private readonly AddVehicleTagRequestValidator validator = new();

    private bool IsValid(string? name) => validator.Validate(new AddVehicleTagRequest { Name = name }).IsValid;

    [Theory]
    [InlineData("sunroof")]
    [InlineData("tow package")]
    [InlineData("  padded  ")]
    [InlineData("a")]
    public void Valid(string name) => Assert.True(IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_IsRequired(string? name) => Assert.False(IsValid(name));

    [Fact]
    public void MaxLength30_AfterTrimming()
    {
        Assert.True(IsValid(new string('a', 30)));
        Assert.True(IsValid("  " + new string('a', 30) + "  "));
        Assert.False(IsValid(new string('a', 31)));
    }

    [Theory]
    [InlineData("bad\u0007tag")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    public void ControlCharacters_Rejected(string name) => Assert.False(IsValid(name));

    [Fact]
    public void QuotesAndAngleBrackets_Allowed() => Assert.True(IsValid("<b>\"sun\"</b>"));
}

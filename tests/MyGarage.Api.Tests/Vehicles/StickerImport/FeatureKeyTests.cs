using MyGarage.Api.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class FeatureKeyTests
{
    [Theory]
    [InlineData("Heated Seats", "heatedseats")]
    [InlineData("heated  seats", "heatedseats")]
    [InlineData("Heated-Seats", "heatedseats")]
    [InlineData("  HEATED SEATS!  ", "heatedseats")]
    [InlineData("Sunroof", "sunroof")]
    [InlineData("Panoramic Sunroof", "panoramicsunroof")]
    [InlineData("360° Camera", "360camera")]
    [InlineData("Café Package", "cafépackage")]
    public void TheKeyKeepsLowerCaseLettersAndDigitsOnly(string name, string expected)
    {
        Assert.Equal(expected, FeatureKey.Of(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- !! --")]
    [InlineData(null)]
    public void AnEmptyOrPunctuationOnlyNameHasAnEmptyKey(string? name)
    {
        Assert.Equal(string.Empty, FeatureKey.Of(name));
    }

    [Fact]
    public void DifferentWordsGiveDifferentKeys()
    {
        Assert.NotEqual(FeatureKey.Of("Sunroof"), FeatureKey.Of("Panoramic Sunroof"));
    }
}

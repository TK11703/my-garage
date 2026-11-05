using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class FeatureCategoriesTests
{
    [Fact]
    public void Other_IsTheLastCategory_AndEveryNameIsUnique()
    {
        Assert.Equal(FeatureCategories.Other, FeatureCategories.All[^1]);
        Assert.Equal(FeatureCategories.All.Count, FeatureCategories.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(FeatureCategories.All, category => Assert.InRange(category.Length, 1, 40));
    }

    [Theory]
    [InlineData("safety", "Safety")]
    [InlineData("  COMFORT & CONVENIENCE ", "Comfort & Convenience")]
    [InlineData("Other", "Other")]
    [InlineData("Gadgets", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_ReturnsTheOfficialNameOrNull(string? value, string? expected) =>
        Assert.Equal(expected, FeatureCategories.Normalize(value));

    [Theory]
    [InlineData("Exterior", "Exterior")]
    [InlineData(null, "Other")]
    [InlineData("Gadgets", "Other")]
    public void Display_FallsBackToOther(string? category, string expected) =>
        Assert.Equal(expected, FeatureCategories.Display(category));
}

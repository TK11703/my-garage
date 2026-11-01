using MyGarage.Database.Common;

namespace MyGarage.Database.Tests.Common;

public class LikePatternTests
{
    [Theory]
    [InlineData("truck", "%truck%")]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("o'brien <b>", "%o'brien <b>%")]
    [InlineData("  trimmed  ", "%trimmed%")]
    public void Contains_EscapesMetacharactersAndWraps(string input, string expected) =>
        Assert.Equal(expected, LikePattern.Contains(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_NothingToSearch_ReturnsNull(string? input) =>
        Assert.Null(LikePattern.Contains(input));
}

using System.Security.Claims;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal UserNamed(string claimType, string value) =>
        new(new ClaimsIdentity([new Claim(claimType, value)], "test"));

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Mary Jane Watson", "MW")]
    [InlineData("prince", "P")]
    [InlineData("  ada   lovelace  ", "AL")]
    [InlineData("ada lovelace", "AL")]
    [InlineData("(admin) Bob", "AB")]
    public void GetInitials_UsesFirstLettersOfFirstAndLastWords(string name, string expected)
    {
        Assert.Equal(expected, UserNamed("name", name).GetInitials());
    }

    [Fact]
    public void GetInitials_FallsBackToPreferredUsername()
    {
        Assert.Equal("A", UserNamed("preferred_username", "ada@example.com").GetInitials());
    }

    [Fact]
    public void GetInitials_IsEmptyForTheNeutralName()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(string.Empty, anonymous.GetInitials());
    }

    [Fact]
    public void GetInitials_IsNeverLongerThanTwoCharacters()
    {
        Assert.True(UserNamed("name", "One Two Three Four Five").GetInitials().Length <= 2);
    }
}

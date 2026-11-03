using MyGarage.Shared.Registrations;

namespace MyGarage.Api.Tests.Registrations;

public class UsStatesTests
{
    [Fact]
    public void All_HasFiftyStatesAndDc() => Assert.Equal(51, UsStates.All.Count);

    [Fact]
    public void All_CodesAreUniqueTwoLetterCapitals()
    {
        Assert.All(UsStates.All, state => Assert.Matches("^[A-Z]{2}$", state.Code));
        Assert.Equal(51, UsStates.All.Select(state => state.Code).Distinct().Count());
    }

    [Fact]
    public void All_NamesAreNonEmpty_AndSortedByName()
    {
        Assert.All(UsStates.All, state => Assert.False(string.IsNullOrWhiteSpace(state.Name)));
        Assert.Equal(UsStates.All.Select(state => state.Name).Order(StringComparer.Ordinal), UsStates.All.Select(state => state.Name));
    }

    [Theory]
    [InlineData("WA", true)]
    [InlineData("DC", true)]
    [InlineData("wa", false)]
    [InlineData("ZZ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_MatchesTheExactCode(string? code, bool expected) => Assert.Equal(expected, UsStates.IsValid(code));

    [Fact]
    public void NameOf_AndDisplay_UseTheStateName()
    {
        Assert.Equal("Washington", UsStates.NameOf("WA"));
        Assert.Equal("District of Columbia", UsStates.NameOf("DC"));
        Assert.Equal("Washington (WA)", UsStates.Display("WA"));
    }

    [Fact]
    public void NameOf_UnknownCode_ReturnsTheCode() => Assert.Equal("ZZ", UsStates.NameOf("ZZ"));
}

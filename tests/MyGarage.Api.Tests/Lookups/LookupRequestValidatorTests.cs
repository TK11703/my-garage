using MyGarage.Api.Lookups;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Tests.Lookups;

public class AddLookupItemRequestValidatorTests
{
    private readonly AddLookupItemRequestValidator validator = new();

    private bool IsValid(string? name) => validator.Validate(new AddLookupItemRequest { Name = name }).IsValid;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Required(string? name) => Assert.False(IsValid(name));

    [Fact]
    public void Name_AllowsBetweenOneAndThirtyCharactersAfterTrimming()
    {
        Assert.True(IsValid("A"));
        Assert.True(IsValid(new string('a', 30)));
        Assert.True(IsValid(" " + new string('a', 30) + " "));
        Assert.False(IsValid(new string('a', 31)));
    }

    [Fact]
    public void Name_RejectsControlCharactersButAllowsQuotesAndBrackets()
    {
        Assert.False(IsValid("bad\u0007"));
        Assert.False(IsValid("bad\nname"));
        Assert.True(IsValid("It's <b>fine</b>"));
    }
}

public class SetLookupVisibilityRequestValidatorTests
{
    private readonly SetLookupVisibilityRequestValidator validator = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsHidden_AcceptsBothValues(bool isHidden) =>
        Assert.True(validator.Validate(new SetLookupVisibilityRequest { IsHidden = isHidden }).IsValid);

    [Fact]
    public void IsHidden_MissingValueIsRejected() =>
        Assert.False(validator.Validate(new SetLookupVisibilityRequest()).IsValid);
}

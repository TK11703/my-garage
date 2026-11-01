using MyGarage.Api.Vehicles.JournalNotes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.JournalNotes;

public class JournalNoteRequestValidatorTests
{
    private readonly JournalNoteRequestValidator validator = new();

    private bool IsValid(string? body, string? category = null) =>
        validator.Validate(new JournalNoteRequest { Body = body, Category = category }).IsValid;

    [Fact]
    public void Body_RequiredAndTrimmed()
    {
        Assert.True(IsValid("Changed the oil"));
        Assert.False(IsValid(null));
        Assert.False(IsValid(""));
        Assert.False(IsValid("   \n\t "));
    }

    [Fact]
    public void Body_MaxLength4000_AfterTrimming()
    {
        Assert.True(IsValid(new string('a', 4000)));
        Assert.True(IsValid("  " + new string('a', 4000) + "\n"));
        Assert.False(IsValid(new string('a', 4001)));
    }

    [Fact]
    public void Body_AllowsLineBreaksAndTabsButNotOtherControlCharacters()
    {
        Assert.True(IsValid("line one\r\nline two\tindented"));
        Assert.False(IsValid("bad\u0007bell"));
        Assert.False(IsValid("null\u0000byte"));
    }

    [Fact]
    public void Body_AllowsQuotesAndAngleBrackets() => Assert.True(IsValid("<b>\"bold\"</b> & 'quotes'"));

    [Fact]
    public void Category_IsOptional()
    {
        Assert.True(IsValid("x", null));
        Assert.True(IsValid("x", ""));
        Assert.True(IsValid("x", "   "));
    }

    [Fact]
    public void Category_MaxLength40_AfterTrimming()
    {
        Assert.True(IsValid("x", new string('a', 40)));
        Assert.True(IsValid("x", " " + new string('a', 40) + " "));
        Assert.False(IsValid("x", new string('a', 41)));
    }

    [Theory]
    [InlineData("Oil change")]
    [InlineData("Road trip 2026")]
    [InlineData("Détailing")]
    [InlineData("<b>odd</b>")]
    public void Category_AcceptsAnyFreeText(string category) => Assert.True(IsValid("x", category));

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("bell\u0007")]
    [InlineData("tab\there")]
    public void Category_RejectsControlCharacters(string category) => Assert.False(IsValid("x", category));
}

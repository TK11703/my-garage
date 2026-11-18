using Bunit;
using MyGarage.Web.Preferences;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Preferences;

public class AppearanceCardTests : VehicleTestContext
{
    [Fact]
    public void OffersLightAndDarkOnly_AsButtonsTheShellScriptDrives()
    {
        var cut = Render<AppearanceCard>();

        Assert.Equal("Appearance", cut.Find("h2").TextContent.Trim());
        var group = cut.Find("[role='group'][aria-label='Color theme']");
        var buttons = group.QuerySelectorAll("button[data-mg-theme-set]");
        Assert.Equal(["light", "dark"], buttons.Select(button => button.GetAttribute("data-mg-theme-set")));
        Assert.Equal(["Light", "Dark"], buttons.Select(button => button.TextContent.Trim()));
        Assert.All(buttons, button =>
        {
            Assert.NotNull(button.QuerySelector("i.mg-theme-swatch[aria-hidden='true']"));
            Assert.Equal("button", button.GetAttribute("type"));
            Assert.Equal("false", button.GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void SaysTheChoiceIsKeptInThisBrowserOnly()
    {
        var cut = Render<AppearanceCard>();

        Assert.Contains("kept in this browser", cut.Find("[data-testid='appearance-note']").TextContent);
    }

    [Fact]
    public void ThePreferencesPageShowsItBetweenTheAccountAndTheLists()
    {
        RegisterApi(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));

        var cut = Render<MyGarage.Web.Preferences.Preferences>();

        cut.WaitForAssertion(() =>
        {
            var headings = cut.FindAll("section h2").Select(heading => heading.TextContent.Trim()).ToList();
            Assert.True(headings.IndexOf("Account") < headings.IndexOf("Appearance"));
        });
    }
}

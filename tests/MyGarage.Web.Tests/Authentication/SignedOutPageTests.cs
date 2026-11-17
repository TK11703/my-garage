using Bunit;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class SignedOutPageTests : BunitContext
{
    [Fact]
    public void TheHeadingIsTheOnlyH1_InACenteredCardWithNoBreadcrumb()
    {
        var cut = Render<SignedOut>();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("You have been signed out", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find(".mg-stage .mg-stage-card h1"));
        Assert.Empty(cut.FindAll("nav[aria-label='Breadcrumb']"));
    }

    [Fact]
    public void ShowsConfirmationAndSignInLink()
    {
        var cut = Render<SignedOut>();

        Assert.Contains("signed out", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/authentication/login", cut.Find("a.btn").GetAttribute("href"));
        Assert.Equal("/about", cut.Find("a.mg-stage-alt").GetAttribute("href"));
        Assert.Equal("What is My Garage?", cut.Find("a.mg-stage-alt").TextContent.Trim());
    }
}

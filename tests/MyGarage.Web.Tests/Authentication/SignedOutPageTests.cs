using Bunit;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class SignedOutPageTests : BunitContext
{
    [Fact]
    public void ShowsStandardPageHeader()
    {
        var cut = Render<SignedOut>();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("You have been signed out", cut.Find("h1").TextContent);
        var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
        Assert.Equal(["Home", "Signed out"], items.Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void ShowsConfirmationAndSignInLink()
    {
        var cut = Render<SignedOut>();

        Assert.Contains("signed out", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/authentication/login", cut.Find("a.btn").GetAttribute("href"));
    }
}

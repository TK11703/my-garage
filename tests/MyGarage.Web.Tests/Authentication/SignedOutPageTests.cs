using Bunit;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class SignedOutPageTests : BunitContext
{
    [Fact]
    public void ShowsConfirmationAndSignInLink()
    {
        var cut = Render<SignedOut>();

        Assert.Contains("signed out", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/authentication/login", cut.Find("a.btn").GetAttribute("href"));
    }
}

using Bunit;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class ApiFailureAlertTests : BunitContext
{
    private IRenderedComponent<ApiFailureAlert> RenderAlert(GarageApiFailure failure, string returnUrl = "/vehicles") =>
        Render<ApiFailureAlert>(ps => ps
            .Add(p => p.Failure, failure)
            .Add(p => p.ReturnUrl, returnUrl)
            .Add(p => p.NotAcceptedMessage, "Your session was not accepted.")
            .Add(p => p.UnavailableMessage, "The service is unavailable."));

    [Fact]
    public void NotAccepted_ShowsMessageAndSignInAgain()
    {
        var cut = RenderAlert(GarageApiFailure.NotAccepted, "/vehicles/abc/edit");

        var alert = cut.Find("div.alert.alert-warning");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("Your session was not accepted.", alert.TextContent);
        var link = cut.Find("a.btn.btn-primary");
        Assert.Equal("Sign in again", link.TextContent.Trim());
        Assert.Equal("/authentication/login?returnUrl=%2Fvehicles%2Fabc%2Fedit", link.GetAttribute("href"));
    }

    [Fact]
    public void Unavailable_ShowsMessageWithoutSignIn()
    {
        var cut = RenderAlert(GarageApiFailure.Unavailable);

        Assert.Contains("The service is unavailable.", cut.Find("div.alert").TextContent);
        Assert.Empty(cut.FindAll("a"));
    }
}

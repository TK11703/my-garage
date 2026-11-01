using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Authentication;

namespace MyGarage.Web.Tests.Authentication;

public class AuthenticationErrorPageTests : BunitContext
{
    private IRenderedComponent<AuthenticationError> RenderWithReason(string? reason)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(reason is null
            ? "/authentication/error"
            : $"/authentication/error?reason={Uri.EscapeDataString(reason)}");

        return Render<AuthenticationError>();
    }

    [Theory]
    [InlineData(AuthFailureReason.Cancelled)]
    [InlineData(AuthFailureReason.Denied)]
    [InlineData(AuthFailureReason.Unavailable)]
    [InlineData(AuthFailureReason.Unknown)]
    public void ShowsFixedMessagePerReason(string reason)
    {
        var cut = RenderWithReason(reason);

        Assert.Contains(AuthFailureMessages.For(reason), cut.Markup);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData(null)]
    public void UnrecognisedReasonShowsUnknownMessageAndNeverEchoesInput(string? reason)
    {
        var cut = RenderWithReason(reason);

        Assert.Contains(AuthFailureMessages.For(AuthFailureReason.Unknown), cut.Markup);
        Assert.DoesNotContain("alert(1)", cut.Markup);
    }

    [Fact]
    public void ShowsStandardPageHeader()
    {
        var cut = RenderWithReason(AuthFailureReason.Cancelled);

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("Sign-in problem", cut.Find("h1").TextContent);
        var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
        Assert.Equal(["Home", "Sign-in problem"], items.Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void TryAgainLinksToLogin()
    {
        var cut = RenderWithReason(AuthFailureReason.Cancelled);

        Assert.Equal("/authentication/login", cut.Find("a.btn").GetAttribute("href"));
    }
}

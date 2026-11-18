using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;
using MyGarage.Web.Notifications;

namespace MyGarage.Web.Tests.Components.Layout;

public class MainLayoutTests : BunitContext
{
    private readonly BunitAuthorizationContext auth;

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
    }

    public MainLayoutTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(new NotificationsApiClient(new HttpClient(new FailingHandler()) { BaseAddress = new Uri("http://api.test/") }));
        auth = AddAuthorization();
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(ps => ps.Add(p => p.Body, "<p id=\"page-body\">Hello</p>"));

    [Fact]
    public void SkipLinkIsTheFirstFocusableElement()
    {
        var cut = RenderLayout();

        var first = cut.Find("a, button, input, select, textarea");
        Assert.Equal("#main-content", first.GetAttribute("href"));
        Assert.Equal("Skip to main content", first.TextContent.Trim());
    }

    [Fact]
    public void SignedOut_ThePublicBarPrecedesTheMainContentArea()
    {
        var cut = RenderLayout();
        var markup = cut.Markup;

        Assert.NotNull(cut.Find("header.mg-publicbar"));
        Assert.True(markup.IndexOf("mg-publicbar", StringComparison.Ordinal) < markup.IndexOf("id=\"main-content\"", StringComparison.Ordinal));
    }

    [Fact]
    public void BodyRendersInTheMainElement()
    {
        var cut = RenderLayout();

        var main = cut.Find("main#main-content");
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        Assert.Contains("mg-content", main.ClassName);
        Assert.NotNull(main.QuerySelector("#page-body"));
    }

    [Fact]
    public void SignedOut_ThereIsNoRail()
    {
        var cut = RenderLayout();

        Assert.Empty(cut.FindAll("aside#mg-rail"));
        Assert.DoesNotContain("mg-content-with-rail", cut.Find("main#main-content").ClassName);
        Assert.Contains("mg-content-public", cut.Find("main#main-content").ClassName);
    }

    [Fact]
    public void SignedIn_TheRailPrecedesTheContent_AndTheContentMakesRoomForIt_WithNoPublicBar()
    {
        auth.SetAuthorized("Ada Lovelace");

        var cut = RenderLayout();
        var markup = cut.Markup;

        var order = new[]
        {
            markup.IndexOf("id=\"mg-rail\"", StringComparison.Ordinal),
            markup.IndexOf("id=\"main-content\"", StringComparison.Ordinal),
        };
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Contains("mg-content-with-rail", cut.Find("main#main-content").ClassName);
        Assert.Empty(cut.FindAll(".mg-publicbar"));
    }

    [Fact]
    public void KeepsTheBlazorErrorUi()
    {
        var cut = RenderLayout();

        Assert.NotNull(cut.Find("#blazor-error-ui"));
    }
}

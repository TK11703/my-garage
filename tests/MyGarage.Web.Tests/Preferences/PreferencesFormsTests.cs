using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Lookups;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Preferences;

public class PreferencesFormsTests : VehicleTestContext
{
    private IRenderedComponent<MyGarage.Web.Preferences.Preferences> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(respond);
        return Render<MyGarage.Web.Preferences.Preferences>();
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static LookupItemDto Created(string name) => new() { Id = Guid.NewGuid(), Name = name, IsDefault = false, IsHidden = false };

    private static HttpResponseMessage Conflict(string title) => Problem(HttpStatusCode.Conflict, title);

    [Fact]
    public void Add_Success_PostsToTheKindAndReturnsToThatTab()
    {
        var cut = RenderPage(_ => Json(Created("Plug-in Hybrid"), HttpStatusCode.Created));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-fuel-types-name")));
        cut.Find("input#lookup-fuel-types-name").Change("Plug-in Hybrid");

        cut.Find("form[data-testid='add-lookup-fuel-types']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/preferences?tab=fuel-types", CurrentUri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal("/api/lookups/fuel-types", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Add_ValidationError_ShowsTheMessageNextToTheFieldKeepsTheTextAndOpensThatTab()
    {
        var cut = RenderPage(_ => ValidationProblem(("Name", "Name must be at most 30 characters.")));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-drivetrains-name")));
        cut.Find("input#lookup-drivetrains-name").Change("x");

        cut.Find("form[data-testid='add-lookup-drivetrains']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("at most 30 characters", cut.Find("#lookup-drivetrains-error").TextContent);
            Assert.Equal("lookup-drivetrains-error", cut.Find("input#lookup-drivetrains-name").GetAttribute("aria-describedby"));
            Assert.Equal("x", cut.Find("input#lookup-drivetrains-name").GetAttribute("value"));
            Assert.Equal(["lookup-drivetrains"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Empty(cut.FindAll("#lookup-fuel-types [id$='-error']"));
        });
    }

    [Theory]
    [InlineData("That item already exists.")]
    [InlineData("That item already exists but is hidden. Show it instead.")]
    [InlineData("You can add up to 50 items to this list.")]
    public void Add_Conflict_ShowsTheApisExplanationOnThatTab(string title)
    {
        var cut = RenderPage(_ => Conflict(title));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-transmissions-name")));
        cut.Find("input#lookup-transmissions-name").Change("Manual");

        cut.Find("form[data-testid='add-lookup-transmissions']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(title, cut.Find("#lookup-transmissions-error").TextContent.Trim());
            Assert.Equal(["lookup-transmissions"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Fact]
    public void Toggle_SendsTheInverseVisibilityAndReturnsToThatTab()
    {
        var diesel = Item("Diesel");
        Lookups = new LookupsDto { VehicleTypes = [], FuelTypes = [Item("Gasoline"), diesel], Transmissions = [], Drivetrains = [] };
        var cut = RenderPage(_ => Json(new LookupItemDto { Id = diesel.Id, Name = "Diesel", IsDefault = true, IsHidden = true }));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"form[data-testid='toggle-fuel-types-{diesel.Id}']")));

        cut.Find($"form[data-testid='toggle-fuel-types-{diesel.Id}']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/preferences?tab=fuel-types", CurrentUri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/lookups/fuel-types/{diesel.Id}/visibility", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Toggle_ItemNoLongerThere_ShowsANeutralNotFoundMessageOnThatTab()
    {
        var gasoline = Item("Gasoline");
        Lookups = new LookupsDto { VehicleTypes = [], FuelTypes = [gasoline], Transmissions = [], Drivetrains = [] };
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"form[data-testid='toggle-fuel-types-{gasoline.Id}']")));

        cut.Find($"form[data-testid='toggle-fuel-types-{gasoline.Id}']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Find("#lookup-fuel-types [data-testid='tab-message']").TextContent);
            Assert.Equal(["lookup-fuel-types"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Add_WhenTheApiRejectsTheToken_ShowsSignInAgain(HttpStatusCode status)
    {
        var cut = RenderPage(_ => new HttpResponseMessage(status));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-vehicle-types-name")));

        cut.Find("form[data-testid='add-lookup-vehicle-types']").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Find("#lookup-vehicle-types").TextContent));
    }

    [Fact]
    public void Add_WhenTheApiIsDown_ShowsTheUnavailableMessage()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-vehicle-types-name")));

        cut.Find("form[data-testid='add-lookup-vehicle-types']").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.Unavailable, cut.Find("#lookup-vehicle-types").TextContent));
    }
}

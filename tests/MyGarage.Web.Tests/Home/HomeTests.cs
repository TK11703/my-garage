using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Home;

public class HomeTests : VehicleTestContext
{
    private readonly BunitAuthorizationContext auth;

    public HomeTests()
    {
        auth = AddAuthorization();
        auth.SetAuthorized("ignored");
        RegisterApi(_ => Json(Page([Summary("Daily")])));
    }

    private IRenderedComponent<MyGarage.Web.Home.Home> RenderHome() => Render<MyGarage.Web.Home.Home>();

    [Fact]
    public void ShowsPageHeaderWithHomeAsTheCurrentPageAndGreetingTitle()
    {
        auth.SetClaims(new Claim("name", "Alice Garage"));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("Welcome, Alice Garage", cut.Find("h1").TextContent);
            var item = Assert.Single(cut.FindAll("nav[aria-label='Breadcrumb'] li"));
            Assert.Equal("Home", item.TextContent.Trim());
            Assert.Equal("page", item.GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void ShowsNameClaim()
    {
        auth.SetClaims(new Claim("name", "Alice Garage"), new Claim("preferred_username", "alice@contoso.test"));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alice Garage", cut.Markup);
            Assert.DoesNotContain("alice@contoso.test", cut.Markup);
        });
    }

    [Fact]
    public void FallsBackToPreferredUsername()
    {
        auth.SetClaims(new Claim("preferred_username", "alice@contoso.test"));

        var cut = RenderHome();

        cut.WaitForAssertion(() => Assert.Contains("alice@contoso.test", cut.Markup));
    }

    [Fact]
    public void FallsBackToNeutralLabel()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() => Assert.Contains("Signed-in user", cut.Markup));
    }

    [Fact]
    public void DescribesTheProduct()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() => Assert.Contains("private list of your vehicles", cut.Find("p.lead").TextContent));
    }

    [Fact]
    public void WithVehicles_OffersMyVehiclesAsThePrimaryActionAndAddVehicle()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            var vehicles = cut.Find("a[href='/vehicles']");
            Assert.Equal("My vehicles", vehicles.TextContent.Trim());
            Assert.Contains("btn-primary", vehicles.ClassName);
            var add = cut.Find("a[href='/vehicles/new']");
            Assert.Equal("Add vehicle", add.TextContent.Trim());
            Assert.Contains("btn-outline-primary", add.ClassName);
        });
    }

    [Fact]
    public void WithoutVehicles_EmphasizesAddYourFirstVehicle()
    {
        RegisterApiReplacement();

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            var add = cut.Find("a[href='/vehicles/new']");
            Assert.Equal("Add your first vehicle", add.TextContent.Trim());
            Assert.Contains("btn-primary", add.ClassName);
            var vehicles = cut.Find("a[href='/vehicles']");
            Assert.Equal("My vehicles", vehicles.TextContent.Trim());
            Assert.Contains("btn-outline-secondary", vehicles.ClassName);
        });
    }

    [Fact]
    public void WhenTheApiIsUnavailable_StillOffersBothEntryPointsWithoutAnAlert()
    {
        RegisterApiReplacement(_ => throw new HttpRequestException("boom"));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("My vehicles", cut.Find("a[href='/vehicles']").TextContent.Trim());
            Assert.Equal("Add vehicle", cut.Find("a[href='/vehicles/new']").TextContent.Trim());
            Assert.Empty(cut.FindAll(".alert"));
        });
    }

    // The constructor registered a default API; swap it for one scenario.
    private void RegisterApiReplacement(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        Services.Remove(Services.Single(descriptor => descriptor.ServiceType == typeof(MyGarage.Web.Vehicles.VehiclesApiClient)));
        RegisterApi(respond ?? (_ => Json(Page([]))));
    }
}

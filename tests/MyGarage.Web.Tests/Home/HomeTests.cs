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
    public void ShowsPageHeaderWithGreetingTitleAndNoBreadcrumb()
    {
        auth.SetClaims(new Claim("name", "Alice Garage"));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("Welcome, Alice Garage", cut.Find("h1").TextContent);
            Assert.Empty(cut.FindAll("nav[aria-label='Breadcrumb']"));
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
    public void Hero_ShowsTheVehicleCountAndThatOnlyTheUserSeesTheGarage()
    {
        RegisterApiReplacement(_ => Json(Page([Summary("A"), Summary("B")], totalCount: 7)));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("7 vehicles", cut.Find("[data-testid='vehicle-count']").TextContent.Trim());
            Assert.Contains("Only you", cut.Find("[data-testid='hero-stats']").TextContent);
        });
    }

    [Fact]
    public void Hero_UsesTheSingularForOneVehicle()
    {
        RegisterApiReplacement(_ => Json(Page([Summary("A")], totalCount: 1)));

        var cut = RenderHome();

        cut.WaitForAssertion(() => Assert.Equal("1 vehicle", cut.Find("[data-testid='vehicle-count']").TextContent.Trim()));
    }

    [Fact]
    public void Hero_OmitsTheCountWhenTheApiFails()
    {
        RegisterApiReplacement(_ => throw new HttpRequestException("boom"));

        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='vehicle-count']"));
            Assert.Contains("Only you", cut.Find("[data-testid='hero-stats']").TextContent);
        });
    }

    [Fact]
    public void Hero_HasADecorativeGaragePhotoBehindTheText_HiddenFromAssistiveTechnology()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            var hero = cut.Find("[data-testid='hero']");
            var photo = hero.QuerySelector("img.mg-hero-photo")!;
            Assert.Equal(string.Empty, photo.GetAttribute("alt"));
            Assert.Contains("home-garage", photo.GetAttribute("src"));
            Assert.Equal("true", hero.QuerySelector(".mg-hero-shade")!.GetAttribute("aria-hidden"));
            Assert.NotNull(hero.QuerySelector(".mg-hero-content h2#hero-heading"));
            Assert.Empty(hero.QuerySelectorAll("svg.mg-hero-car"));
            Assert.Equal("hero-heading", hero.GetAttribute("aria-labelledby"));
        });
    }

    [Fact]
    public void AboutSection_ListsEightFeaturesEachWithAnIconAndAHeading()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("About My Garage", cut.Find("h2#about-heading").TextContent.Trim());
            var features = cut.FindAll("[data-testid='about'] [data-testid='feature']");
            Assert.Equal(8, features.Count);
            Assert.All(features, feature =>
            {
                Assert.NotNull(feature.QuerySelector("svg.bi[aria-hidden='true']"));
                Assert.False(string.IsNullOrWhiteSpace(feature.QuerySelector("h3")!.TextContent));
                Assert.False(string.IsNullOrWhiteSpace(feature.QuerySelector("p")!.TextContent));
            });
            Assert.Contains("Maintenance you can trust", cut.Find("[data-testid='about']").TextContent);
        });
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
    public void WithVehicles_OffersAddMaintenanceAsASecondaryAction()
    {
        var cut = RenderHome();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("[data-testid='hero'] a[href='/drafts/upload']");
            Assert.Equal("Add Maintenance", link.TextContent.Trim());
            Assert.Contains("btn-outline-primary", link.ClassName);
        });
    }

    [Fact]
    public void WithoutVehicles_StillOffersAddMaintenance()
    {
        RegisterApiReplacement();

        var cut = RenderHome();

        cut.WaitForAssertion(() => Assert.Equal("Add Maintenance", cut.Find("[data-testid='hero'] a[href='/drafts/upload']").TextContent.Trim()));
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

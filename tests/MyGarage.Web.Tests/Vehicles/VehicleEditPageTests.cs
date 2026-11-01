using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleEdit> RenderEdit() =>
        Render<VehicleEdit>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

    [Fact]
    public void Form_IsPrefilledWithTheCurrentDetails()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily", cut.Find("input#nickname").GetAttribute("value"));
            Assert.Equal("Ford", cut.Find("input#make").GetAttribute("value"));
            Assert.Equal("F-150", cut.Find("input#model").GetAttribute("value"));
            Assert.Equal("XLT", cut.Find("input#trim").GetAttribute("value"));
            Assert.Equal("Blue", cut.Find("input#color").GetAttribute("value"));
            Assert.Equal("42000", cut.Find("input#mileage").GetAttribute("value"));
            Assert.Equal("4", cut.Find("input#doors").GetAttribute("value"));
        });
    }

    [Fact]
    public void ShowsTrailAndTitleWithTheStoredNickname()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("Edit Daily", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "Daily", "Edit"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}", items[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("page", items[3].GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void UnknownVehicle_ShowsStandardNotFoundHeader()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void CancelLink_ReturnsToTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
            Assert.Equal($"/vehicles/{vehicleId}", cut.Find("a.btn-outline-secondary").GetAttribute("href")));
    }

    [Fact]
    public void ApiValidationErrors_AreShownPerField()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Doors", "Doors must be between 1 and 6."))
            : Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Doors must be between 1 and 6.", cut.Find("#doors-error").TextContent));
    }

    [Fact]
    public void Success_PutsTheFormValuesAndRedirectsToTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));
        cut.Find("input#nickname").Change("Renamed");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void UnknownVehicle_ShowsNotFound()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.NotFound, cut.Markup);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));

        var cut = RenderEdit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }
}

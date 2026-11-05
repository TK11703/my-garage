using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCreatePageTests : VehicleTestContext
{
    [Fact]
    public void ShowsTrailAndTitle()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("Add Vehicle", cut.Find("h1").TextContent);
        var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
        Assert.Equal(["Home", "Vehicles", "New vehicle"], items.Select(item => item.TextContent.Trim()));
        Assert.Equal("/vehicles", items[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("page", items[2].GetAttribute("aria-current"));
    }

    [Fact]
    public void PrimaryButtonIsSaveWithASaveIcon()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        var save = cut.Find("button[type='submit']");
        Assert.Equal("Save", save.TextContent.Trim());
        Assert.NotNull(save.QuerySelector("svg.bi[aria-hidden='true']"));
    }

    [Fact]
    public void Form_HasLabeledInputsForEveryField()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        foreach (var field in new[]
                 {
                     "year", "make", "model", "trim", "vin", "vehicle-type", "ownership", "purchased-on", "lease-ends-on",
                     "engine", "horsepower", "drivetrain", "color", "fuel-type", "transmission", "seats", "doors", "wheel-size", "tire-size", "mileage",
                 })
        {
            Assert.NotNull(cut.Find($"#{field}"));
            Assert.NotNull(cut.Find($"label[for='{field}']"));
        }

        Assert.Empty(cut.FindAll("#nickname, label[for='nickname']"));
    }

    [Fact]
    public void Form_HasTheImportAndDetailsTabsOpenOnImportWithTheFieldsOnDetails()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Import from sticker", "Details"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["tab-import"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Empty(cut.FindAll("#tab-media, #tab-description"));
            Assert.Equal(
                ["year", "make", "model", "trim", "vin", "vehicle-type", "ownership", "purchased-on", "lease-ends-on", "mileage", "latest-odometer-reading"],
                cut.Find("#group-basic").QuerySelectorAll("input[id], select[id]").Select(field => field.Id));
            Assert.Equal(
                ["engine", "horsepower", "drivetrain", "color", "fuel-type", "transmission", "seats", "doors", "wheel-size", "tire-size"],
                cut.Find("#group-specifications").QuerySelectorAll("input[id], select[id]").Select(field => field.Id));
        });
    }

    [Fact]
    public void ARejectedSaveOpensTheTabHoldingTheFirstError()
    {
        RegisterApi(_ => ValidationProblem(("Horsepower", "Horsepower must be between 1 and 3,000.")));
        var cut = Render<VehicleCreate>();
        cut.Find("input#horsepower").Change("0");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Horsepower must be between", cut.Find("#horsepower-error").TextContent);
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Fact]
    public void ARejectedMileageOpensTheBasicInfoTab()
    {
        RegisterApi(_ => ValidationProblem(("Mileage", "Mileage must be between 0 and 9,999,999.")));
        var cut = Render<VehicleCreate>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mileage must be between", cut.Find("#group-basic #mileage-error").TextContent);
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Fact]
    public void SelectsOfferTheUsersShownLookupItems()
    {
        RegisterApi(_ => Json(Detail()));
        Lookups = new MyGarage.Shared.Lookups.LookupsDto
        {
            VehicleTypes = [Item("Sedan"), Item("Hidden one", isHidden: true), Item("Mine", isDefault: false)],
            FuelTypes = [Item("Diesel")],
            Transmissions = [],
            Drivetrains = [Item("AWD")],
        };

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Not set", "Sedan", "Mine"], cut.FindAll("select#vehicle-type option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Not set", "Diesel"], cut.FindAll("select#fuel-type option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Not set"], cut.FindAll("select#transmission option").Select(option => option.TextContent.Trim()));
        });
    }

    [Fact]
    public void LookupsUnavailable_ShowsTheUnavailableMessage()
    {
        RegisterApi(_ => Json(Detail()));
        LookupsFailure = System.Net.HttpStatusCode.ServiceUnavailable;

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() => Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.Unavailable, cut.Markup));
    }

    [Fact]
    public void ApiValidationErrors_AreShownPerFieldAndInputsKeepTheirValues()
    {
        RegisterApi(_ => ValidationProblem(("Year", "Year is required."), ("Mileage", "Mileage must be between 0 and 9,999,999.")));
        var cut = Render<VehicleCreate>();
        cut.Find("input#make").Change("Ford");
        cut.Find("input#model").Change("F-150");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Year is required.", cut.Find("#year-error").TextContent);
            Assert.Contains("Mileage must be between", cut.Find("#mileage-error").TextContent);
        });
        Assert.Equal("Ford", cut.Find("input#make").GetAttribute("value"));
        Assert.Equal("true", cut.Find("input#year").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Success_PostsTrimmedValuesAndRedirectsToTheNewVehicle()
    {
        var created = Detail();
        RegisterApi(_ => Json(created, System.Net.HttpStatusCode.Created));
        var cut = Render<VehicleCreate>();
        cut.Find("input#make").Change("Ford");
        cut.Find("input#model").Change("F-150");
        cut.Find("input#year").Change("2019");
        cut.Find("input#mileage").Change("1200");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{created.Id}", Services.GetRequiredService<NavigationManager>().Uri));
        var request = Requests.Single(r => r.Method == System.Net.Http.HttpMethod.Post);
        Assert.Equal("/api/vehicles", request.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(System.Net.HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));
        var cut = Render<VehicleCreate>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.NotAccepted, cut.Markup));
    }
}

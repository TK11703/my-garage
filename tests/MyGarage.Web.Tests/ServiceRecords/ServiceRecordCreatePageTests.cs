using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class ServiceRecordCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<ServiceRecordCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        return Render<ServiceRecordCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsTheTitleAndTrail()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add service record", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Garage", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records", items[3].QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void SaveAndCancelButtons_HaveTitlesAndTheSaveButtonHasAnIcon()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var save = cut.Find("button[type='submit']");
            Assert.Equal("Save", save.TextContent.Trim());
            Assert.Equal("Save this service record", save.GetAttribute("title"));
            Assert.NotNull(save.QuerySelector("svg.bi[aria-hidden='true']"));

            var cancel = cut.Find("a.btn-outline-secondary");
            Assert.Equal("Cancel", cancel.TextContent.Trim());
            Assert.False(string.IsNullOrWhiteSpace(cancel.GetAttribute("title")));
        });
    }

    [Fact]
    public void SaveAndCancelSitBelowTheCard_NotInItsFooter()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("div.card");
            Assert.Empty(card.QuerySelectorAll(".card-footer"));
            Assert.Empty(card.QuerySelectorAll("button[type='submit']"));
            var actions = cut.Find("[data-testid='form-actions']");
            Assert.Same(card.ParentElement, actions.ParentElement);
            Assert.Contains("Save", actions.QuerySelector("button[type='submit']")!.TextContent);
            Assert.Equal("Cancel", actions.QuerySelector("a.btn")!.TextContent.Trim());
        });
    }

    [Fact]
    public void Form_HasLabeledInputsWithRequiredMarkers()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var date = cut.Find("input#date-serviced");
            Assert.Equal("date", date.GetAttribute("type"));
            Assert.Equal("true", date.GetAttribute("aria-required"));
            Assert.NotNull(cut.Find("label[for='date-serviced']"));

            var odometer = cut.Find("input#odometer-reading");
            Assert.Equal("number", odometer.GetAttribute("type"));
            Assert.Equal("numeric", odometer.GetAttribute("inputmode"));
            Assert.Equal("true", odometer.GetAttribute("aria-required"));
            Assert.Equal("9999999", odometer.GetAttribute("max"));
            Assert.NotNull(cut.Find("label[for='odometer-reading']"));

            var shop = cut.Find("input#shop-or-person");
            Assert.Equal("100", shop.GetAttribute("maxlength"));
            Assert.Null(shop.GetAttribute("aria-required"));
            Assert.NotNull(cut.Find("label[for='shop-or-person']"));
        });
    }

    [Fact]
    public void Form_HasAnOptionalTaxFieldAfterTheShop()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var tax = cut.Find("input#tax");
            Assert.Equal("number", tax.GetAttribute("type"));
            Assert.Equal("0.01", tax.GetAttribute("step"));
            Assert.Equal("0", tax.GetAttribute("min"));
            Assert.Equal("9999999.99", tax.GetAttribute("max"));
            Assert.Null(tax.GetAttribute("aria-required"));
            Assert.Equal("Tax ($)", cut.Find("label[for='tax']").TextContent.Trim());
            Assert.Equal(
                ["date-serviced", "odometer-reading", "shop-or-person", "tax"],
                cut.FindAll("form input").Select(input => input.Id).Where(id => !string.IsNullOrEmpty(id)));
        });
    }

    private static string ReadBody(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().Result;

    [Fact]
    public void ATaxIsPostedWithTheRecord()
    {
        var created = RecordDetail(vehicleId);
        string? body = null;
        var cut = RenderPage(request =>
        {
            body = ReadBody(request);
            return Json(created, HttpStatusCode.Created);
        });
        cut.WaitForElement("input#odometer-reading").Change("45000");
        cut.Find("input#tax").Change("8.25");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(body));
        Assert.Equal(8.25m, System.Text.Json.JsonDocument.Parse(body!).RootElement.GetProperty("tax").GetDecimal());
    }

    [Fact]
    public void ARejectedTax_ShowsTheMessageBesideTheField()
    {
        var cut = RenderPage(_ => ValidationProblem(("Tax", "Tax must be between 0.00 and 9,999,999.99.")));
        cut.WaitForElement("input#odometer-reading").Change("12");
        cut.Find("input#tax").Change("-1");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Tax must be between", cut.Find("#tax-error").TextContent);
            Assert.Equal("tax-error", cut.Find("#tax").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheValues()
    {
        var cut = RenderPage(_ => ValidationProblem(
            ("DateServiced", "Date serviced cannot be in the future."),
            ("OdometerReading", "Odometer reading must be between 0 and 9,999,999.")));
        cut.WaitForElement("input#odometer-reading").Change("12");
        cut.Find("input#shop-or-person").Change("Joe's Garage");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("cannot be in the future", cut.Find("#dateserviced-error").TextContent);
            Assert.Contains("must be between 0 and 9,999,999", cut.Find("#odometerreading-error").TextContent);
            Assert.Equal("dateserviced-error", cut.Find("#date-serviced").GetAttribute("aria-describedby"));
            Assert.Equal("12", cut.Find("#odometer-reading").GetAttribute("value"));
            Assert.Equal("Joe's Garage", cut.Find("#shop-or-person").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheNewRecord()
    {
        var created = RecordDetail(vehicleId);
        var cut = RenderPage(_ => Json(created, HttpStatusCode.Created));
        cut.WaitForElement("input#odometer-reading").Change("45000");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{created.Id}", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/service-records", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<ServiceRecordCreate>(parameters => parameters.Add(page => page.VehicleId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Markup);
            Assert.Equal("/vehicles", cut.Find("a.btn").GetAttribute("href"));
            Assert.Empty(cut.FindAll("form"));
        });
    }
}

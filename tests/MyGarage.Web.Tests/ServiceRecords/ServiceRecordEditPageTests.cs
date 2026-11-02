using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class ServiceRecordEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();

    private IRenderedComponent<ServiceRecordEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToRecord)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToRecord(request));
        return Render<ServiceRecordEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId));
    }

    private HttpResponseMessage StoredRecord(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(RecordDetail(vehicleId, recordId, new DateOnly(2026, 5, 1), 45_000, "Joe's Garage"))
            : Json(RecordDetail(vehicleId, recordId));

    [Fact]
    public void PrefillsTheThreeFieldsAndShowsTheTrail()
    {
        var cut = RenderPage(StoredRecord);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit service record", cut.Find("h1").TextContent);
            Assert.Equal("2026-05-01", cut.Find("#date-serviced").GetAttribute("value"));
            Assert.Equal("45000", cut.Find("#odometer-reading").GetAttribute("value"));
            Assert.Equal("Joe's Garage", cut.Find("#shop-or-person").GetAttribute("value"));
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(
                ["Home", "Vehicles", "2020 Ford F-150", "Service record May 1, 2026", "Edit"],
                items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}", items[3].QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Get
            ? StoredRecord(request)
            : ValidationProblem(("OdometerReading", "Odometer reading must be between 0 and 9,999,999.")));
        cut.WaitForElement("input#odometer-reading").Change("-5");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("must be between 0 and 9,999,999", cut.Find("#odometerreading-error").TextContent);
            Assert.Equal("-5", cut.Find("#odometer-reading").GetAttribute("value"));
            Assert.Equal("Joe's Garage", cut.Find("#shop-or-person").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PutsTheValuesAndRedirectsToTheRecord()
    {
        var cut = RenderPage(StoredRecord);
        cut.WaitForElement("input#odometer-reading").Change("46000");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/service-records/{recordId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void AnUnknownRecord_ShowsTheNotFoundState()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Markup);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void ARecordDeletedMeanwhile_ShowsTheNotFoundStateOnSave()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Get ? StoredRecord(request) : new HttpResponseMessage(HttpStatusCode.NotFound));
        cut.WaitForElement("form");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("could not be found", cut.Markup));
    }
}

using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class ServiceRecordDetailPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();

    private IRenderedComponent<ServiceRecordDetail> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}")
            {
                return Json(Detail(vehicleId));
            }

            return respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        return Render<ServiceRecordDetail>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId));
    }

    [Fact]
    public void ShowsTheMetadataAndAZeroTotalWithTheEmptyLineItemState()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId, new DateOnly(2026, 5, 1), 45_000, "Joe's Garage")));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Service record May 1, 2026", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Service record May 1, 2026"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records", items[2].QuerySelector("a")!.GetAttribute("href"));

            Assert.Equal(
                ["Date serviced", "Odometer reading", "Shop name or person"],
                cut.Find("dl[aria-label='Service record']").QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(
                ["May 1, 2026", "45,000 mi", "Joe's Garage"],
                cut.Find("dl[aria-label='Service record']").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
            Assert.Contains("No line items yet", cut.Markup);
            Assert.Equal("$0.00", cut.Find("[data-testid='total-cost']").TextContent.Trim());
        });
    }

    [Fact]
    public void TheBackButtonReadsBackToMaintenanceAndReturnsToTheTab()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId)));

        cut.WaitForAssertion(() =>
        {
            var back = cut.FindAll("a.btn-outline-secondary").Single(link => link.TextContent.Trim() == "Back to Maintenance");
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records", back.GetAttribute("href"));
        });
    }

    [Fact]
    public void AnEmptyShopReadsNotSet()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId, shop: null)));

        cut.WaitForAssertion(() =>
            Assert.Equal("Not set", cut.Find("dl[aria-label='Service record']").QuerySelectorAll("dd")[2].TextContent.Trim()));
    }

    [Fact]
    public void UserTextIsEncoded()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId, shop: "<i>Italic</i>")));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("dl i"));
            Assert.Contains("<i>Italic</i>", cut.Find("dl[aria-label='Service record']").TextContent);
        });
    }

    [Fact]
    public void AnUnknownRecord_ShowsTheNotFoundStateWithALinkBackToTheTab()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Markup);
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records", cut.Find("a.btn").GetAttribute("href"));
        });
    }

    [Fact]
    public void AnApiFailure_ShowsTheAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }

    [Fact]
    public void OffersAnEditLinkAndADeleteConfirmationPanel()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId)));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}/edit", cut.Find("[data-testid='edit-service-record']").GetAttribute("href"));
            var panel = cut.Find("details[data-testid='delete-service-record']");
            Assert.Contains("Delete this service record? This also removes its line items and cannot be undone.", panel.TextContent);
            Assert.Equal("Yes, delete this service record", panel.QuerySelector("button[type='submit']")!.TextContent.Trim());
            Assert.Equal("Keep service record", panel.QuerySelector("a.btn")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndReturnsToTheServiceRecordsTab()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Json(RecordDetail(vehicleId, recordId)));
        cut.WaitForElement("details[data-testid='delete-service-record'] form");

        cut.Find("details[data-testid='delete-service-record'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/service-records/{recordId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteOfAnAlreadyDeletedRecord_CountsAsDone()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(RecordDetail(vehicleId, recordId)));
        cut.WaitForElement("details[data-testid='delete-service-record'] form");

        cut.Find("details[data-testid='delete-service-record'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void CancellingTheDelete_LeavesTheRecordAndCallsNoDelete()
    {
        var cut = RenderPage(_ => Json(RecordDetail(vehicleId, recordId)));

        cut.WaitForElement("details[data-testid='delete-service-record']");

        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}", cut.Find("details[data-testid='delete-service-record'] a.btn").GetAttribute("href"));
    }
}

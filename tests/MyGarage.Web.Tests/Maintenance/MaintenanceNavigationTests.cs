using System.Net;
using Bunit;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Maintenance;

// The vehicle page decides which Maintenance item opens and which page each list shows from its query values.
public class MaintenanceNavigationTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleDetail> RenderDetail(string query)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        NavigateTo($"/vehicles/{vehicleId}?tab=service-records{query}");
        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    private string PageRequested(string segment) =>
        Requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/" + segment, StringComparison.Ordinal)).RequestUri!.Query;

    [Theory]
    [InlineData("", "maintenance-service-records")]
    [InlineData("&section=records", "maintenance-service-records")]
    [InlineData("&section=fuel", "maintenance-fuel")]
    [InlineData("&section=compliance", "maintenance-compliance")]
    [InlineData("&section=bogus", "maintenance-service-records")]
    public void TheSectionQueryValue_OpensExactlyThatAccordionItem(string query, string openId)
    {
        var cut = RenderDetail(query);

        cut.WaitForAssertion(() =>
        {
            var open = cut.FindAll("#tab-service-records .accordion-collapse.show");
            Assert.Single(open);
            Assert.Equal(openId, open[0].Id);
        });
    }

    [Fact]
    public void EachListPagesOnItsOwnQueryValue_AndTheOthersStayOnPageOne()
    {
        var cut = RenderDetail("&section=compliance&inspectionsPage=2&taxesPage=3&fuelPage=4");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#maintenance-compliance")));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("page=2", PageRequested("inspections"));
            Assert.Contains("page=3", PageRequested("taxes"));
            Assert.Contains("page=4", PageRequested("fuel-energy-entries"));
            Assert.Contains("page=1", PageRequested("registrations"));
            Assert.Contains("page=1", PageRequested("insurance"));
            Assert.Contains("page=1", PageRequested("service-records"));
        });
    }

    [Theory]
    [InlineData("&fuelPage=abc")]
    [InlineData("&fuelPage=0")]
    [InlineData("&fuelPage=-3")]
    public void ABadPageValue_MeansThePageOne(string query)
    {
        var cut = RenderDetail(query);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#maintenance-fuel")));
        cut.WaitForAssertion(() => Assert.Contains("page=1", PageRequested("fuel-energy-entries")));
    }
}

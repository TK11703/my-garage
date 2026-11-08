using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Taxes;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Tests.Taxes;

namespace MyGarage.Web.Tests.Vehicles;

// A change page reached by address for an archived vehicle refuses the save with the archived notice and keeps what was typed.
public class ArchivedVehicleWritePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static HttpResponseMessage ArchivedConflict() =>
        new(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"type":"urn:mygarage:problem:vehicle-archived","title":"This vehicle is archived. Restore it to change it.","status":409}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        };

    [Fact]
    public void ASaveOnAnArchivedVehicle_ShowsTheArchivedNotice_WithARestoreLink_AndSendsNothingElse()
    {
        RegisterApi(request => request.Method == HttpMethod.Get ? Json(Detail(vehicleId)) : ArchivedConflict());
        var cut = Render<TaxCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
        cut.WaitForElement("select#tax-type").Change("Lease");
        cut.Find("input#amount").Change("120.5");
        cut.Find("select#recurrence").Change("Monthly");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("This vehicle is archived and cannot be changed.", cut.Find("[data-testid='archived-notice']").TextContent);
            Assert.Equal($"/vehicles/{vehicleId}", cut.Find("[data-testid='restore-link']").GetAttribute("href"));
            Assert.DoesNotContain(VehicleMessages.Unavailable, cut.Markup);
        });
        Assert.Single(Requests, request => request.Method == HttpMethod.Post);
        Assert.DoesNotContain("saved", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public void TheAnotherUsersOrUnknownVehicleSave_IsStillTheNormalNotFoundPath()
    {
        RegisterApi(request => request.Method == HttpMethod.Get ? Json(Detail(vehicleId)) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = Render<TaxCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
        cut.WaitForElement("select#tax-type").Change("Lease");
        cut.Find("input#amount").Change("120.5");
        cut.Find("select#recurrence").Change("Monthly");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='archived-notice']")));
    }
}

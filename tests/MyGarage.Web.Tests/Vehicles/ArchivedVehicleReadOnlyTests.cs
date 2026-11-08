using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Common;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Taxes;
using MyGarage.Web.Tests.Taxes;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Tests.FuelEnergyEntries;
using MyGarage.Web.Tests.Registrations;

namespace MyGarage.Web.Tests.Vehicles;

// An archived vehicle shows its history without any control that changes it, and a refused change says why (specs/014-archive-vehicle).
public class ArchivedVehicleReadOnlyTests : VehicleTestContext
{
    private const string ArchivedType = "urn:mygarage:problem:vehicle-archived";

    private readonly Guid vehicleId = Guid.NewGuid();

    private VehicleDetailDto Vehicle(bool archived)
    {
        var basis = Detail(vehicleId);
        return new VehicleDetailDto
        {
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Trim = basis.Trim, Color = basis.Color, Year = basis.Year, Mileage = basis.Mileage,
            Doors = basis.Doors, Tags = [], Images = [], CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
            ArchivedAt = archived ? new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero) : null,
        };
    }

    private static HttpResponseMessage ArchivedConflict() =>
        new(HttpStatusCode.Conflict)
        {
            Content = new StringContent($$"""{"type":"{{ArchivedType}}","title":"This vehicle is archived. Restore it to change it.","status":409}""", System.Text.Encoding.UTF8, "application/problem+json"),
        };

    private IRenderedComponent<VehicleDetail> RenderTab(string tab)
    {
        NavigateTo($"/vehicles/{vehicleId}?tab={tab}");
        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    private void SeedMaintenance()
    {
        MaintenanceLists["taxes"] = TaxTestData.TaxPage([TaxTestData.Entry(vehicleId)]);
        MaintenanceLists["registrations"] = RegistrationTestData.RegistrationPage([RegistrationTestData.Entry(vehicleId)]);
        MaintenanceLists["fuel-energy-entries"] = FuelEnergyEntryTestData.FuelPage([FuelEnergyEntryTestData.Entry(vehicleId)]);
        MaintenanceLists["documents"] = new
        {
            items = new[] { new { id = Guid.NewGuid(), fileName = "invoice.pdf", contentType = "application/pdf", sizeBytes = 1000, createdAt = DateTimeOffset.UtcNow } },
            page = 1, pageSize = 10, totalCount = 1, totalPages = 1,
        };
    }

    [Fact]
    public async Task A409WithTheArchivedType_BecomesAnArchivedFailure_WhileAnyOther409StaysAConflict()
    {
        var archived = true;
        RegisterApi(_ => archived ? ArchivedConflict() : Problem(HttpStatusCode.Conflict, "This vehicle already has that feature."));
        var api = Services.GetRequiredService<VehiclesApiClient>();

        var thrown = await Assert.ThrowsAsync<GarageApiException>(() => api.AddTagAsync(vehicleId, "sunroof"));
        archived = false;
        var other = await api.AddTagAsync(vehicleId, "sunroof");

        Assert.Equal(GarageApiFailure.VehicleArchived, thrown.Failure);
        Assert.Equal(ApiOutcome.Conflict, other.Outcome);
        Assert.Equal("This vehicle already has that feature.", other.Title);
    }

    [Fact]
    public async Task AnImageUpload_ToAnArchivedVehicle_IsAnArchivedFailure_NotALimitReached()
    {
        var archived = true;
        RegisterApi(_ => archived ? ArchivedConflict() : Problem(HttpStatusCode.Conflict, "limit"));
        var api = Services.GetRequiredService<VehiclesApiClient>();

        var thrown = await Assert.ThrowsAsync<GarageApiException>(() => api.UploadImageAsync(vehicleId, new MemoryStream([1, 2, 3]), "a.png", "image/png", null));
        archived = false;
        var limit = await api.UploadImageAsync(vehicleId, new MemoryStream([1, 2, 3]), "a.png", "image/png", null);

        Assert.Equal(GarageApiFailure.VehicleArchived, thrown.Failure);
        Assert.Equal(ImageUploadOutcome.LimitReached, limit);
    }

    [Fact]
    public void TheArchivedNotice_SaysSo_AndOffersRestoreAndBackLinksToTheVehicle()
    {
        var cut = Render<ArchivedVehicleNotice>(parameters => parameters.Add(notice => notice.VehicleId, vehicleId));

        Assert.Equal("alert", cut.Find("[data-testid='archived-notice']").GetAttribute("role"));
        Assert.Contains("This vehicle is archived and cannot be changed.", cut.Markup);
        Assert.Equal($"/vehicles/{vehicleId}", cut.Find("[data-testid='restore-link']").GetAttribute("href"));
        Assert.Equal("Restore this vehicle", cut.Find("[data-testid='restore-link']").TextContent.Trim());
        Assert.Equal($"/vehicles/{vehicleId}", cut.Find("[data-testid='back-link']").GetAttribute("href"));
        Assert.Equal("Back to the vehicle", cut.Find("[data-testid='back-link']").TextContent.Trim());
    }

    [Fact]
    public void TheArchivedNotice_FindsTheVehicleInAnAddress_AndFallsBackToTheArchivedList()
    {
        var fromAddress = Render<ArchivedVehicleNotice>(parameters => parameters.Add(notice => notice.ReturnUrl, $"/vehicles/{vehicleId}/service-records/new"));
        var none = Render<ArchivedVehicleNotice>(parameters => parameters.Add(notice => notice.ReturnUrl, "/drafts/upload"));

        Assert.Equal($"/vehicles/{vehicleId}", fromAddress.Find("[data-testid='restore-link']").GetAttribute("href"));
        Assert.Empty(none.FindAll("[data-testid='restore-link']"));
        Assert.Equal("/vehicles?status=archived", none.Find("[data-testid='archived-link']").GetAttribute("href"));
    }

    [Fact]
    public void TheFailureAlert_ForAnArchivedVehicle_ShowsTheNotice_NotTheOutageText()
    {
        var cut = Render<ApiFailureAlert>(parameters => parameters
            .Add(alert => alert.Failure, GarageApiFailure.VehicleArchived)
            .Add(alert => alert.ReturnUrl, $"/vehicles/{vehicleId}/taxes/new")
            .Add(alert => alert.NotAcceptedMessage, VehicleMessages.NotAccepted)
            .Add(alert => alert.UnavailableMessage, VehicleMessages.Unavailable));

        Assert.Contains("archived and cannot be changed", cut.Markup);
        Assert.DoesNotContain(VehicleMessages.Unavailable, cut.Markup);
        Assert.Equal($"/vehicles/{vehicleId}", cut.Find("[data-testid='restore-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("a[href^='/authentication/login']"));
    }

    [Theory]
    [InlineData(GarageApiFailure.NotAccepted, "The service did not accept your sign-in. Please sign in again.")]
    [InlineData(GarageApiFailure.Unavailable, "The My Garage service is not reachable right now. Please try again shortly.")]
    [InlineData(GarageApiFailure.VehicleArchived, "This vehicle is archived and cannot be changed.")]
    public void TheMessageForAFailure_NamesTheRightCause(GarageApiFailure failure, string expected) =>
        Assert.Equal(expected, VehicleMessages.For(failure));

    [Fact]
    public void TheMaintenanceTabOfAnArchivedVehicle_ShowsTheHistoryWithoutAnyChangeControl()
    {
        SeedMaintenance();
        RegisterApi(_ => Json(Vehicle(archived: true)));

        var cut = RenderTab("service-records");

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='tax']"));
            Assert.NotEmpty(cut.FindAll("[data-testid='fuel-energy-entry']"));
            Assert.Empty(cut.FindAll("[data-testid='import-record']"));
            Assert.Empty(cut.FindAll("[data-testid='edit-entry']"));
            Assert.Empty(cut.FindAll("[data-testid='delete-document']"));
            Assert.Empty(cut.FindAll("[data-testid='add-maintenance-menu']"));
            Assert.NotNull(cut.Find("[data-testid='view-document']"));
        });
    }

    [Fact]
    public void TheMaintenanceTabOfAnActiveVehicle_KeepsItsControls()
    {
        SeedMaintenance();
        RegisterApi(_ => Json(Vehicle(archived: false)));

        var cut = RenderTab("service-records");

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='import-record']"));
            Assert.NotEmpty(cut.FindAll("[data-testid='edit-entry']"));
            Assert.NotEmpty(cut.FindAll("[data-testid='delete-document']"));
            Assert.NotNull(cut.Find("[data-testid='add-maintenance-menu']"));
        });
    }

    [Fact]
    public void TheRemindersTabOfAnArchivedVehicle_ListsTheDisabledRemindersWithoutAddActions()
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath.EndsWith("/reminders", StringComparison.Ordinal)
            ? Json(new[] { Reminders.ReminderTestData.Reminder(vehicleId, name: "Registration due", status: MyGarage.Shared.Reminders.ReminderStatus.Archived, vehicleArchived: true) })
            : Json(Vehicle(archived: true)));

        var cut = RenderTab("reminders");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Archived", cut.Find("#tab-reminders [data-testid='reminder-status']").TextContent.Trim());
            Assert.Equal("Disabled, vehicle archived", cut.Find("#tab-reminders [data-testid='reminder-archived']").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='add-reminder']"));
        });
    }
}

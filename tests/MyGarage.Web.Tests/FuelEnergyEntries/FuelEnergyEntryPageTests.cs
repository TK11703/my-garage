using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Web.FuelEnergyEntries;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.FuelEnergyEntries.FuelEnergyEntryTestData;

namespace MyGarage.Web.Tests.FuelEnergyEntries;

public class FuelEnergyEntryCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;

    private HttpResponseMessage CaptureAndCreate(HttpRequestMessage request)
    {
        postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Json(Entry(vehicleId), HttpStatusCode.Created);
    }

    private IRenderedComponent<FuelEnergyEntryCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond, string? kind = null)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        if (kind is not null)
        {
            NavigateTo($"/vehicles/{vehicleId}/fuel-energy-entries/new?kind={kind}");
        }

        return Render<FuelEnergyEntryCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void Fuel_ShowsTheFuelLabelsHelperTextAndInputLimits()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "fuel");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add fuel", cut.Find("h1").TextContent);
            Assert.Equal("Date", cut.Find("label[for='entry-date']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Price per gallon", cut.Find("label[for='unit-price']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Total gallons", cut.Find("label[for='quantity']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Total", cut.Find("label[for='total-cost']").TextContent.Trim());
            Assert.Equal("Leave empty to calculate it from the price and gallons.", cut.Find("#total-help").TextContent.Trim());
            Assert.Equal("0.0001", cut.Find("#unit-price").GetAttribute("step"));
            Assert.Equal("0.001", cut.Find("#quantity").GetAttribute("step"));
            Assert.Equal("0.01", cut.Find("#total-cost").GetAttribute("step"));
            Assert.Equal("fuel", cut.Find("input[type='hidden']").GetAttribute("value"));
            Assert.Null(cut.FindAll("[data-testid='entry-kind']").FirstOrDefault());
        });
    }

    [Fact]
    public void Energy_ShowsTheChargingLabelsAndHelperText()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "energy");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add charge", cut.Find("h1").TextContent);
            Assert.Equal("Price per kWh", cut.Find("label[for='unit-price']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("kWh delivered", cut.Find("label[for='quantity']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Total session cost", cut.Find("label[for='total-cost']").TextContent.Trim());
            Assert.Equal("Leave empty to calculate it from the price and kWh.", cut.Find("#total-help").TextContent.Trim());
            Assert.Equal("energy", cut.Find("input[type='hidden']").GetAttribute("value"));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("diesel")]
    public void AMissingOrUnknownKind_MeansFuel(string? kind)
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound), kind);

        cut.WaitForAssertion(() => Assert.Equal("Add fuel", cut.Find("h1").TextContent));
    }

    [Fact]
    public void RequiredFieldsAreMarked_AndTheTotalIsOptional()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("date", cut.Find("#entry-date").GetAttribute("type"));
            Assert.Equal("true", cut.Find("#entry-date").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#unit-price").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#quantity").GetAttribute("aria-required"));
            Assert.Null(cut.Find("#total-cost").GetAttribute("aria-required"));
        });
    }

    [Fact]
    public void TheTrailLinksBackToTheMaintenanceTabsFuelSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "energy");

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Garage", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=fuel", items[3].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=fuel", cut.Find("a.btn-outline-secondary").GetAttribute("href"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheValues()
    {
        var cut = RenderPage(_ => ValidationProblem(
            ("EntryDate", "Entry date cannot be in the future."),
            ("UnitPrice", "Price can have at most 4 decimal places.")));
        cut.WaitForElement("input#unit-price").Change("3.45678");
        cut.Find("input#quantity").Change("12.5");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("cannot be in the future", cut.Find("#entrydate-error").TextContent);
            Assert.Contains("at most 4 decimal places", cut.Find("#unitprice-error").TextContent);
            Assert.Equal("entrydate-error", cut.Find("#entry-date").GetAttribute("aria-describedby"));
            Assert.Equal("unitprice-error", cut.Find("#unit-price").GetAttribute("aria-describedby"));
            Assert.Equal("3.45678", cut.Find("#unit-price").GetAttribute("value"));
            Assert.Equal("12.5", cut.Find("#quantity").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheFuelSection()
    {
        var cut = RenderPage(CaptureAndCreate, "energy");
        cut.WaitForElement("input#entry-date").Change("2026-05-01");
        cut.Find("input#unit-price").Change("0.1342");
        cut.Find("input#quantity").Change("28.5");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=fuel", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/fuel-energy-entries", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"kind\":\"Energy\"", postedBody);
        Assert.Contains("0.1342", postedBody);
        Assert.Contains("28.5", postedBody);
        Assert.Contains("\"totalCost\":null", postedBody);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithALinkBack()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<FuelEnergyEntryCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void AnApiOutage_ShowsAnAlert()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = Render<FuelEnergyEntryCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class FuelEnergyEntryEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid entryId = Guid.NewGuid();

    private IRenderedComponent<FuelEnergyEntryEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToEntry)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToEntry(request));
        return Render<FuelEnergyEntryEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.EntryId, entryId));
    }

    private HttpResponseMessage Stored(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(Entry(vehicleId, entryId, FuelEnergyKind.Energy, new DateOnly(2026, 5, 1), 0.1342m, 28.5m, 3.82m))
            : Json(Entry(vehicleId, entryId));

    [Fact]
    public void PrefillsEveryFieldKeepsTheKindReadOnlyAndShowsTheTrail()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit charge", cut.Find("h1").TextContent);
            Assert.Equal("2026-05-01", cut.Find("#entry-date").GetAttribute("value"));
            Assert.Equal("0.1342", cut.Find("#unit-price").GetAttribute("value"));
            Assert.Equal("28.5", cut.Find("#quantity").GetAttribute("value"));
            Assert.Equal("3.82", cut.Find("#total-cost").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='entry-kind'], .badge"));
            Assert.Equal("energy", cut.Find("input[type='hidden']").GetAttribute("value"));
            Assert.Equal("Price per kWh", cut.Find("label[for='unit-price']").TextContent.Replace("*", string.Empty).Trim());
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Garage", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Get
            ? Stored(request)
            : ValidationProblem(("UnitPrice", "Price must be between 0 and 9,999.9999.")));
        cut.WaitForElement("input#unit-price").Change("-5");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("must be between 0 and 9,999.9999", cut.Find("#unitprice-error").TextContent);
            Assert.Equal("-5", cut.Find("#unit-price").GetAttribute("value"));
            Assert.Equal("28.5", cut.Find("#quantity").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PutsTheValuesAndRedirectsToTheFuelSection()
    {
        var cut = RenderPage(Stored);
        cut.WaitForElement("input#total-cost").Change("4.00");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=fuel", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/fuel-energy-entries/{entryId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Delete_UsesTheConfirmationPanelNamingTheEntryAndKeepLinksBackToTheEditPage()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-fuel-energy-entry']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Contains("Delete this charging entry? This cannot be undone.", panel.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/fuel-energy-entries/{entryId}/edit", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndRedirectsToTheFuelSection()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-fuel-energy-entry'] form");

        cut.Find("details[data-testid='delete-fuel-energy-entry'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=fuel", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/fuel-energy-entries/{entryId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteThatFindsNothing_CountsAsSuccess()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-fuel-energy-entry'] form");

        cut.Find("details[data-testid='delete-fuel-energy-entry'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=fuel", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void AnUnknownEntry_ShowsTheNotFoundStateWithALinkBackToTheFuelSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Entry not found", cut.Find("h1").TextContent);
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=fuel", cut.Find("a.btn").GetAttribute("href"));
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void AnApiOutage_ShowsAnAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Registrations;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Registrations.RegistrationTestData;

namespace MyGarage.Web.Tests.Registrations;

public class RegistrationCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;

    private HttpResponseMessage CaptureAndCreate(HttpRequestMessage request)
    {
        postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Json(Entry(vehicleId), HttpStatusCode.Created);
    }

    private IRenderedComponent<RegistrationCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        return Render<RegistrationCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsTheLabelsTheStateOptionsAndTheInputLimits()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add registration", cut.Find("h1").TextContent);
            Assert.Equal("State", cut.Find("label[for='state']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Cost", cut.Find("label[for='cost']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Expiration date", cut.Find("label[for='expiration-date']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("0.01", cut.Find("#cost").GetAttribute("step"));
            Assert.Equal("0", cut.Find("#cost").GetAttribute("min"));
            Assert.Equal("9999999.99", cut.Find("#cost").GetAttribute("max"));
            Assert.Equal("decimal", cut.Find("#cost").GetAttribute("inputmode"));

            var options = cut.FindAll("#state option");
            Assert.Equal(52, options.Count);
            Assert.Equal("Choose a state", options[0].TextContent.Trim());
            Assert.Equal("", options[0].GetAttribute("value"));
            Assert.Contains(options, option => option.TextContent.Trim() == "Washington (WA)" && option.GetAttribute("value") == "WA");
            Assert.Contains(options, option => option.TextContent.Trim() == "District of Columbia (DC)" && option.GetAttribute("value") == "DC");
        });
    }

    [Fact]
    public void AllThreeFieldsAreMarkedRequired()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#state").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#cost").GetAttribute("aria-required"));
            Assert.Equal("date", cut.Find("#expiration-date").GetAttribute("type"));
            Assert.Equal("true", cut.Find("#expiration-date").GetAttribute("aria-required"));
            Assert.Equal(3, cut.FindAll("label .text-danger").Count);
        });
    }

    [Fact]
    public void TheTrailLinksBackToTheMaintenanceTabsComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Garage", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", items[3].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", cut.Find("a.btn-outline-secondary").GetAttribute("href"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheValues()
    {
        var cut = RenderPage(_ => ValidationProblem(
            ("State", "Choose one of the 50 states or DC."),
            ("Cost", "Cost can have at most 2 decimal places."),
            ("ExpirationDate", "Expiration date must be between 1900-01-01 and 2100-12-31.")));
        cut.WaitForElement("select#state").Change("WA");
        cut.Find("input#cost").Change("12.345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose one of the 50 states or DC.", cut.Find("#state-error").TextContent);
            Assert.Contains("at most 2 decimal places", cut.Find("#cost-error").TextContent);
            Assert.Contains("must be between 1900-01-01 and 2100-12-31", cut.Find("#expirationdate-error").TextContent);
            Assert.Equal("state-error", cut.Find("#state").GetAttribute("aria-describedby"));
            Assert.Equal("cost-error", cut.Find("#cost").GetAttribute("aria-describedby"));
            Assert.Equal("expirationdate-error", cut.Find("#expiration-date").GetAttribute("aria-describedby"));
            Assert.Equal("12.345", cut.Find("#cost").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#state option[value='WA']").OuterHtml);
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(CaptureAndCreate);
        cut.WaitForElement("select#state").Change("WA");
        cut.Find("input#cost").Change("78.75");
        cut.Find("input#expiration-date").Change("2027-05-01");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/registrations", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"state\":\"WA\"", postedBody);
        Assert.Contains("78.75", postedBody);
        Assert.Contains("2027-05-01", postedBody);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithoutAForm()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<RegistrationCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

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

        var cut = Render<RegistrationCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class RegistrationEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid entryId = Guid.NewGuid();

    private IRenderedComponent<RegistrationEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToEntry)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToEntry(request));
        return Render<RegistrationEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.EntryId, entryId));
    }

    private HttpResponseMessage Stored(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(Entry(vehicleId, entryId, "OR", 78.75m, new DateOnly(2027, 5, 1)))
            : Json(Entry(vehicleId, entryId));

    [Fact]
    public void PrefillsEveryFieldAndShowsTheTrail()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit registration", cut.Find("h1").TextContent);
            Assert.Contains("selected", cut.Find("#state option[value='OR']").OuterHtml);
            Assert.Equal("78.75", cut.Find("#cost").GetAttribute("value"));
            Assert.Equal("2027-05-01", cut.Find("#expiration-date").GetAttribute("value"));
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Garage", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Get
            ? Stored(request)
            : ValidationProblem(("Cost", "Cost must be between 0.00 and 9,999,999.99.")));
        cut.WaitForElement("input#cost").Change("-5");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("must be between 0.00 and 9,999,999.99", cut.Find("#cost-error").TextContent);
            Assert.Equal("-5", cut.Find("#cost").GetAttribute("value"));
            Assert.Equal("2027-05-01", cut.Find("#expiration-date").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PutsTheValuesAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(Stored);
        cut.WaitForElement("input#cost").Change("30");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/registrations/{entryId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Delete_UsesTheConfirmationPanelNamingTheRegistrationAndKeepLinksBackToTheEditPage()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-registration']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Contains("Delete this registration? This cannot be undone.", panel.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/registrations/{entryId}/edit", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-registration'] form");

        cut.Find("details[data-testid='delete-registration'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/registrations/{entryId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteThatFindsNothing_CountsAsSuccess()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-registration'] form");

        cut.Find("details[data-testid='delete-registration'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void AnUnknownRegistration_ShowsTheNotFoundStateWithALinkBackToTheComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Registration not found", cut.Find("h1").TextContent);
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", cut.Find("a.btn").GetAttribute("href"));
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

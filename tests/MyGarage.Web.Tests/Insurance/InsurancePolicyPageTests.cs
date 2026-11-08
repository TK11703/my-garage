using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Insurance;
using MyGarage.Web.Insurance;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Insurance.InsurancePolicyTestData;

namespace MyGarage.Web.Tests.Insurance;

public class InsurancePolicyCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;

    private HttpResponseMessage CaptureAndCreate(HttpRequestMessage request)
    {
        postedBody = ReadBody(request);
        return Json(Entry(vehicleId), HttpStatusCode.Created);
    }

    private static string ReadBody(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    private IRenderedComponent<InsurancePolicyCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        return Render<InsurancePolicyCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void TheCardHasATitle_AndSaveAndCancelSitBelowIt()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("div.card");
            Assert.Equal("Insurance Policy", card.QuerySelector(":scope > .card-body")!.FirstElementChild!.TextContent.Trim());
            Assert.Empty(card.QuerySelectorAll(".card-footer"));
            var actions = cut.Find("[data-testid='form-actions']");
            Assert.Same(card.ParentElement, actions.ParentElement);
            Assert.Equal(["Save", "Cancel"], actions.QuerySelectorAll("button, a.btn").Select(control => control.TextContent.Trim()));
        });
    }

    [Fact]
    public void ShowsTheLabelsTheDurationOptionsTheHelperTextAndTheInputLimits()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add insurance", cut.Find("h1").TextContent);
            Assert.Equal("Start date", cut.Find("label[for='start-date']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("End date", cut.Find("label[for='end-date']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Duration", cut.Find("label[for='duration']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Cost", cut.Find("label[for='cost']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("0.01", cut.Find("#cost").GetAttribute("step"));
            Assert.Equal("0", cut.Find("#cost").GetAttribute("min"));
            Assert.Equal("9999999.99", cut.Find("#cost").GetAttribute("max"));
            Assert.Equal("decimal", cut.Find("#cost").GetAttribute("inputmode"));

            var options = cut.FindAll("#duration option");
            Assert.Equal(["Choose how often", "Monthly", "Bi-Annual (twice a year)", "Annual"], options.Select(option => option.TextContent.Trim()));
            Assert.Equal(["", "Monthly", "BiAnnual", "Annual"], options.Select(option => option.GetAttribute("value")));

            Assert.Equal("How often the premium is paid.", cut.Find("#duration-help").TextContent.Trim());
            Assert.Equal("The premium paid each time, at the duration above.", cut.Find("#cost-help").TextContent.Trim());
            Assert.Equal("duration-help", cut.Find("#duration").GetAttribute("aria-describedby"));
            Assert.Equal("cost-help", cut.Find("#cost").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void AllFourFieldsAreMarkedRequired()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("date", cut.Find("#start-date").GetAttribute("type"));
            Assert.Equal("true", cut.Find("#start-date").GetAttribute("aria-required"));
            Assert.Equal("date", cut.Find("#end-date").GetAttribute("type"));
            Assert.Equal("true", cut.Find("#end-date").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#duration").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#cost").GetAttribute("aria-required"));
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
            ("StartDate", "Start date is required."),
            ("EndDate", "End date must be later than the start date."),
            ("Duration", "Choose how often the premium is paid."),
            ("Cost", "Cost can have at most 2 decimal places.")));
        cut.WaitForElement("select#duration").Change("BiAnnual");
        cut.Find("input#cost").Change("12.345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Start date is required.", cut.Find("#startdate-error").TextContent);
            Assert.Contains("must be later than the start date", cut.Find("#enddate-error").TextContent);
            Assert.Contains("Choose how often", cut.Find("#duration-error").TextContent);
            Assert.Contains("at most 2 decimal places", cut.Find("#cost-error").TextContent);
            Assert.Equal("startdate-error", cut.Find("#start-date").GetAttribute("aria-describedby"));
            Assert.Equal("enddate-error", cut.Find("#end-date").GetAttribute("aria-describedby"));
            Assert.Equal("duration-help duration-error", cut.Find("#duration").GetAttribute("aria-describedby"));
            Assert.Equal("cost-help cost-error", cut.Find("#cost").GetAttribute("aria-describedby"));
            Assert.Equal("12.345", cut.Find("#cost").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#duration option[value='BiAnnual']").OuterHtml);
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(CaptureAndCreate);
        cut.WaitForElement("input#start-date").Change("2026-05-01");
        cut.Find("input#end-date").Change("2027-05-01");
        cut.Find("select#duration").Change("Monthly");
        cut.Find("input#cost").Change("120.5");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/insurance", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"startDate\":\"2026-05-01\"", postedBody);
        Assert.Contains("\"endDate\":\"2027-05-01\"", postedBody);
        Assert.Contains("\"duration\":\"Monthly\"", postedBody);
        Assert.Contains("120.5", postedBody);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithoutAForm()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<InsurancePolicyCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

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

        var cut = Render<InsurancePolicyCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class InsurancePolicyEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid entryId = Guid.NewGuid();

    private IRenderedComponent<InsurancePolicyEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToEntry)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToEntry(request));
        return Render<InsurancePolicyEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.EntryId, entryId));
    }

    private HttpResponseMessage Stored(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(Entry(vehicleId, entryId, new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1), InsuranceDuration.BiAnnual, 640.5m))
            : Json(Entry(vehicleId, entryId));

    [Fact]
    public void PrefillsEveryFieldAndShowsTheTrail()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit insurance", cut.Find("h1").TextContent);
            Assert.Equal("2026-05-01", cut.Find("#start-date").GetAttribute("value"));
            Assert.Equal("2027-05-01", cut.Find("#end-date").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#duration option[value='BiAnnual']").OuterHtml);
            Assert.Equal("640.5", cut.Find("#cost").GetAttribute("value"));
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
            Assert.Equal("2026-05-01", cut.Find("#start-date").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PutsTheValuesAndRedirectsToTheComplianceSection()
    {
        string putBody = string.Empty;
        var cut = RenderPage(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                putBody = ReadBody(request);
            }

            return Stored(request);
        });
        cut.WaitForElement("input#cost").Change("30");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/insurance/{entryId}", put.RequestUri!.AbsolutePath);
        Assert.Contains("\"duration\":\"BiAnnual\"", putBody);
        Assert.Contains("\"cost\":30", putBody);
    }

    private static string ReadBody(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    [Fact]
    public void Delete_UsesTheConfirmationPanelNamingTheInsuranceEntryAndKeepLinksBackToTheEditPage()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-insurance-policy']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Contains("Delete this insurance entry? This cannot be undone.", panel.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/insurance/{entryId}/edit", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-insurance-policy'] form");

        cut.Find("details[data-testid='delete-insurance-policy'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/insurance/{entryId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteThatFindsNothing_CountsAsSuccess()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-insurance-policy'] form");

        cut.Find("details[data-testid='delete-insurance-policy'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void AnUnknownEntry_ShowsTheNotFoundStateWithALinkBackToTheComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Insurance not found", cut.Find("h1").TextContent);
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

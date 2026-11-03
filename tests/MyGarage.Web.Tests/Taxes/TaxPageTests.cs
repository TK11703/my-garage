using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Taxes;
using MyGarage.Web.Taxes;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Taxes.TaxTestData;

namespace MyGarage.Web.Tests.Taxes;

public class TaxCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;

    private HttpResponseMessage CaptureAndCreate(HttpRequestMessage request)
    {
        postedBody = ReadBody(request);
        return Json(Entry(vehicleId), HttpStatusCode.Created);
    }

    private static string ReadBody(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    private IRenderedComponent<TaxCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        return Render<TaxCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsTheLabelsTheSelectOptionsAndTheInputLimits()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add tax", cut.Find("h1").TextContent);
            Assert.Equal("Type", cut.Find("label[for='tax-type']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Amount", cut.Find("label[for='amount']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Recurrence", cut.Find("label[for='recurrence']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("0.01", cut.Find("#amount").GetAttribute("step"));
            Assert.Equal("0.01", cut.Find("#amount").GetAttribute("min"));
            Assert.Equal("9999999.99", cut.Find("#amount").GetAttribute("max"));
            Assert.Equal("decimal", cut.Find("#amount").GetAttribute("inputmode"));

            var types = cut.FindAll("#tax-type option");
            Assert.Equal(["Choose a type", "Personal Property", "Lease", "Purchase"], types.Select(option => option.TextContent.Trim()));
            Assert.Equal(["", "PersonalProperty", "Lease", "Purchase"], types.Select(option => option.GetAttribute("value")));

            var recurrences = cut.FindAll("#recurrence option");
            Assert.Equal(["Choose how often", "Once", "Monthly", "Bi-Annual (twice a year)", "Annual"], recurrences.Select(option => option.TextContent.Trim()));
            Assert.Equal(["", "Once", "Monthly", "BiAnnual", "Annual"], recurrences.Select(option => option.GetAttribute("value")));
        });
    }

    [Fact]
    public void AllThreeFieldsAreMarkedRequired()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#tax-type").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#amount").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#recurrence").GetAttribute("aria-required"));
        });
    }

    [Fact]
    public void TheTrailLinksBackToTheMaintenanceTabsComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Add tax"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", items[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", cut.Find("a.btn-outline-secondary").GetAttribute("href"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheValues()
    {
        var cut = RenderPage(_ => ValidationProblem(
            ("Type", "Choose a tax type."),
            ("Amount", "Amount can have at most 2 decimal places."),
            ("Recurrence", "Choose how often the tax recurs.")));
        cut.WaitForElement("select#recurrence").Change("BiAnnual");
        cut.Find("input#amount").Change("12.345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose a tax type.", cut.Find("#type-error").TextContent);
            Assert.Contains("at most 2 decimal places", cut.Find("#amount-error").TextContent);
            Assert.Contains("Choose how often", cut.Find("#recurrence-error").TextContent);
            Assert.Equal("type-error", cut.Find("#tax-type").GetAttribute("aria-describedby"));
            Assert.Equal("amount-error", cut.Find("#amount").GetAttribute("aria-describedby"));
            Assert.Equal("recurrence-error", cut.Find("#recurrence").GetAttribute("aria-describedby"));
            Assert.Equal("12.345", cut.Find("#amount").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#recurrence option[value='BiAnnual']").OuterHtml);
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(CaptureAndCreate);
        cut.WaitForElement("select#tax-type").Change("Lease");
        cut.Find("input#amount").Change("120.5");
        cut.Find("select#recurrence").Change("Monthly");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/taxes", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"type\":\"Lease\"", postedBody);
        Assert.Contains("\"recurrence\":\"Monthly\"", postedBody);
        Assert.Contains("120.5", postedBody);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithoutAForm()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<TaxCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

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

        var cut = Render<TaxCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class TaxEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid entryId = Guid.NewGuid();

    private IRenderedComponent<TaxEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToEntry)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToEntry(request));
        return Render<TaxEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.EntryId, entryId));
    }

    private HttpResponseMessage Stored(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(Entry(vehicleId, entryId, TaxType.Purchase, 640.5m, TaxRecurrence.BiAnnual))
            : Json(Entry(vehicleId, entryId));

    private static string ReadBody(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    [Fact]
    public void PrefillsEveryFieldAndShowsTheTrail()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit tax", cut.Find("h1").TextContent);
            Assert.Contains("selected", cut.Find("#tax-type option[value='Purchase']").OuterHtml);
            Assert.Equal("640.5", cut.Find("#amount").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#recurrence option[value='BiAnnual']").OuterHtml);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Edit tax"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Get
            ? Stored(request)
            : ValidationProblem(("Amount", "Amount must be between 0.01 and 9,999,999.99.")));
        cut.WaitForElement("input#amount").Change("-5");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("must be between 0.01 and 9,999,999.99", cut.Find("#amount-error").TextContent);
            Assert.Equal("-5", cut.Find("#amount").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#tax-type option[value='Purchase']").OuterHtml);
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
        cut.WaitForElement("input#amount").Change("30");

        cut.Find("form.vstack").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/taxes/{entryId}", put.RequestUri!.AbsolutePath);
        Assert.Contains("\"type\":\"Purchase\"", putBody);
        Assert.Contains("\"recurrence\":\"BiAnnual\"", putBody);
        Assert.Contains("\"amount\":30", putBody);
    }

    [Fact]
    public void Delete_UsesTheConfirmationPanelNamingTheTaxAndKeepLinksBackToTheEditPage()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-tax']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Contains("Delete this tax? This cannot be undone.", panel.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/taxes/{entryId}/edit", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-tax'] form");

        cut.Find("details[data-testid='delete-tax'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/taxes/{entryId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteThatFindsNothing_CountsAsSuccess()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-tax'] form");

        cut.Find("details[data-testid='delete-tax'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void AnUnknownEntry_ShowsTheNotFoundStateWithALinkBackToTheComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Tax not found", cut.Find("h1").TextContent);
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

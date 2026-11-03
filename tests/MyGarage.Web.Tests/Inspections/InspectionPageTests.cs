using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Inspections;
using MyGarage.Web.Inspections;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Inspections.InspectionTestData;

namespace MyGarage.Web.Tests.Inspections;

public class InspectionCreatePageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;

    private HttpResponseMessage CaptureAndCreate(HttpRequestMessage request)
    {
        postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Json(Entry(vehicleId), HttpStatusCode.Created);
    }

    private IRenderedComponent<InspectionCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respond(request));
        return Render<InspectionCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsTheLabelsTheTypeOptionsAndTheInputLimits()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add inspection", cut.Find("h1").TextContent);
            Assert.Equal("Type", cut.Find("label[for='inspection-type']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Date", cut.Find("label[for='inspection-date']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Cost", cut.Find("label[for='cost']").TextContent.Replace("*", string.Empty).Trim());
            Assert.Equal("Next due date", cut.Find("label[for='next-due-date']").TextContent.Trim());
            Assert.Equal("0.01", cut.Find("#cost").GetAttribute("step"));
            Assert.Equal("0", cut.Find("#cost").GetAttribute("min"));
            Assert.Equal("9999999.99", cut.Find("#cost").GetAttribute("max"));
            Assert.Equal("decimal", cut.Find("#cost").GetAttribute("inputmode"));

            var options = cut.FindAll("#inspection-type option");
            Assert.Equal(["Choose a type", "Safety", "Emissions"], options.Select(option => option.TextContent.Trim()));
            Assert.Equal(["", "Safety", "Emissions"], options.Select(option => option.GetAttribute("value")));
        });
    }

    [Fact]
    public void TheFormIsInACardBody_WithTheRequiredNoteFirst_AndSaveAndCancelInTheFooter()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("div.card");
            var body = card.QuerySelector(":scope > .card-body")!;
            Assert.Equal("* Required", body.FirstElementChild!.TextContent.Trim());
            Assert.NotNull(body.QuerySelector("form #inspection-type"));

            var footer = card.QuerySelector(":scope > .card-footer")!;
            Assert.DoesNotContain("Required", footer.TextContent);
            Assert.Equal(["Save", "Cancel"], footer.QuerySelectorAll("button, a.btn").Select(control => control.TextContent.Trim()));
            var save = footer.QuerySelector("button[type='submit']")!;
            Assert.Equal(body.QuerySelector("form")!.Id, save.GetAttribute("form"));
            Assert.NotNull(save.QuerySelector("svg.bi[aria-hidden='true']"));
            Assert.Null(footer.QuerySelector("details"));
        });
    }

    [Fact]
    public void RequiredFieldsAreMarked_AndTheNextDueDateIsOptional()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#inspection-type").GetAttribute("aria-required"));
            Assert.Equal("date", cut.Find("#inspection-date").GetAttribute("type"));
            Assert.Equal("true", cut.Find("#inspection-date").GetAttribute("aria-required"));
            Assert.Equal("true", cut.Find("#cost").GetAttribute("aria-required"));
            Assert.Equal("date", cut.Find("#next-due-date").GetAttribute("type"));
            Assert.Null(cut.Find("#next-due-date").GetAttribute("aria-required"));
        });
    }

    [Fact]
    public void TheTrailLinksBackToTheMaintenanceTabsComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Add inspection"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", items[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=compliance", cut.Find("a.btn-outline-secondary").GetAttribute("href"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheValues()
    {
        var cut = RenderPage(_ => ValidationProblem(
            ("Type", "Choose a type."),
            ("InspectionDate", "Inspection date cannot be in the future."),
            ("Cost", "Cost can have at most 2 decimal places."),
            ("NextDueDate", "Next due date cannot be before the inspection date.")));
        cut.WaitForElement("select#inspection-type").Change("Emissions");
        cut.Find("input#cost").Change("12.345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose a type.", cut.Find("#type-error").TextContent);
            Assert.Contains("cannot be in the future", cut.Find("#inspectiondate-error").TextContent);
            Assert.Contains("at most 2 decimal places", cut.Find("#cost-error").TextContent);
            Assert.Contains("cannot be before the inspection date", cut.Find("#nextduedate-error").TextContent);
            Assert.Equal("type-error", cut.Find("#inspection-type").GetAttribute("aria-describedby"));
            Assert.Equal("inspectiondate-error", cut.Find("#inspection-date").GetAttribute("aria-describedby"));
            Assert.Equal("cost-error", cut.Find("#cost").GetAttribute("aria-describedby"));
            Assert.Equal("nextduedate-error", cut.Find("#next-due-date").GetAttribute("aria-describedby"));
            Assert.Equal("12.345", cut.Find("#cost").GetAttribute("value"));
            Assert.Contains("selected", cut.Find("#inspection-type option[value='Emissions']").OuterHtml);
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(CaptureAndCreate);
        cut.WaitForElement("select#inspection-type").Change("Emissions");
        cut.Find("input#inspection-date").Change("2026-05-01");
        cut.Find("input#cost").Change("25.5");
        cut.Find("input#next-due-date").Change("2027-05-01");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/inspections", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"type\":\"Emissions\"", postedBody);
        Assert.Contains("2026-05-01", postedBody);
        Assert.Contains("25.5", postedBody);
        Assert.Contains("2027-05-01", postedBody);
    }

    [Fact]
    public void ASaveWithoutANextDueDate_PostsNull()
    {
        var cut = RenderPage(CaptureAndCreate);
        cut.WaitForElement("select#inspection-type").Change("Safety");
        cut.Find("input#inspection-date").Change("2026-05-01");
        cut.Find("input#cost").Change("0");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("\"nextDueDate\":null", postedBody));
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithALinkBack()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<InspectionCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

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

        var cut = Render<InspectionCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class InspectionEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid entryId = Guid.NewGuid();

    private IRenderedComponent<InspectionEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respondToEntry)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : respondToEntry(request));
        return Render<InspectionEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.EntryId, entryId));
    }

    private HttpResponseMessage Stored(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json(Entry(vehicleId, entryId, InspectionType.Emissions, new DateOnly(2026, 5, 1), 25.5m, new DateOnly(2027, 5, 1)))
            : Json(Entry(vehicleId, entryId));

    [Fact]
    public void PrefillsEveryFieldAndShowsTheTrail()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit inspection", cut.Find("h1").TextContent);
            Assert.Contains("selected", cut.Find("#inspection-type option[value='Emissions']").OuterHtml);
            Assert.Equal("2026-05-01", cut.Find("#inspection-date").GetAttribute("value"));
            Assert.Equal("25.5", cut.Find("#cost").GetAttribute("value"));
            Assert.Equal("2027-05-01", cut.Find("#next-due-date").GetAttribute("value"));
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Edit inspection"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void Delete_SitsInTheCardFooterAfterSaveAndCancel()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var footer = cut.Find("div.card > .card-footer");
            Assert.Equal(["Save changes", "Cancel", "Delete"],
                footer.QuerySelectorAll(":scope > button, :scope > a.btn, :scope > details > summary").Select(control => control.TextContent.Trim()));
            Assert.NotNull(footer.QuerySelector("details[data-testid='delete-inspection']"));
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
            Assert.Equal("2026-05-01", cut.Find("#inspection-date").GetAttribute("value"));
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
        Assert.Equal($"/api/vehicles/{vehicleId}/inspections/{entryId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Delete_UsesTheConfirmationPanelNamingTheInspectionAndKeepLinksBackToTheEditPage()
    {
        var cut = RenderPage(Stored);

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-inspection']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Contains("Delete this inspection? This cannot be undone.", panel.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/inspections/{entryId}/edit", panel.QuerySelector("a.btn")!.GetAttribute("href"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingTheDelete_CallsTheApiAndRedirectsToTheComplianceSection()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-inspection'] form");

        cut.Find("details[data-testid='delete-inspection'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/inspections/{entryId}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ADeleteThatFindsNothing_CountsAsSuccess()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Stored(request));
        cut.WaitForElement("details[data-testid='delete-inspection'] form");

        cut.Find("details[data-testid='delete-inspection'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=compliance", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void AnUnknownInspection_ShowsTheNotFoundStateWithALinkBackToTheComplianceSection()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Inspection not found", cut.Find("h1").TextContent);
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

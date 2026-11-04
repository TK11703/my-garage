using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class ServiceLineItemFormTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();
    private readonly List<string> bodies = [];

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private HttpResponseMessage Respond(HttpRequestMessage request, ServiceRecordDto record, Func<HttpResponseMessage>? onWrite)
    {
        if (request.Method == HttpMethod.Get)
        {
            return request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}" ? Json(Detail(vehicleId)) : Json(record);
        }

        bodies.Add(request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().Result);
        return onWrite?.Invoke() ?? Json(record, HttpStatusCode.Created);
    }

    private IRenderedComponent<ServiceLineItemCreate> RenderCreate(Func<HttpResponseMessage>? onWrite = null, HttpContext? httpContext = null)
    {
        var record = RecordDetail(vehicleId, recordId);
        RegisterApi(request => Respond(request, record, onWrite));
        return Render<ServiceLineItemCreate>(parameters =>
        {
            parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.RecordId, recordId);
            if (httpContext is not null)
            {
                parameters.AddCascadingValue<HttpContext>(httpContext);
            }
        });
    }

    private IRenderedComponent<ServiceLineItemEdit> RenderEdit(ServiceLineItemDto line, Func<HttpResponseMessage>? onWrite = null)
    {
        var record = RecordDetail(vehicleId, recordId, lineItems: [line]);
        RegisterApi(request => Respond(request, record, onWrite));
        return Render<ServiceLineItemEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId)
            .Add(page => page.LineItemId, line.Id));
    }

    private static HttpContext AddRowsPost()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["add-part-rows"] = "" });
        return context;
    }

    private JsonElement LastBody() => JsonDocument.Parse(bodies.Last()).RootElement;

    [Fact]
    public void Add_ShowsTheTitleTrailAndFormFields()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add line item", cut.Find("h1").TextContent);
            Assert.Equal(
                ["Home", "Vehicles", "2020 Ford F-150", "Service record May 1, 2026", "Add line item"],
                cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()));

            var description = cut.Find("input#line-description");
            Assert.Equal("200", description.GetAttribute("maxlength"));
            Assert.Equal("true", description.GetAttribute("aria-required"));
            Assert.NotNull(cut.Find("label[for='line-description']"));

            Assert.True(cut.Find("input#pricing-flat").HasAttribute("checked"));
            Assert.False(cut.Find("input#pricing-breakdown").HasAttribute("checked"));
            Assert.Equal("radio", cut.Find("input#pricing-flat").GetAttribute("type"));

            var flat = cut.Find("input#flat-cost");
            Assert.Equal("0.01", flat.GetAttribute("step"));
            Assert.Equal("-9999999.99", flat.GetAttribute("min"));
            Assert.NotNull(cut.Find("label[for='flat-cost']"));
            Assert.NotNull(cut.Find("input#labor-hours"));
            Assert.NotNull(cut.Find("input#labor-rate"));
            Assert.NotNull(cut.Find("label[for='labor-hours']"));
            Assert.NotNull(cut.Find("label[for='labor-rate']"));
        });
    }

    [Fact]
    public void Add_ShowsOnlyTheFlatCostSectionByDefault_InOneSharedCard()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("form[data-testid='line-item-form'] section.card");
            var visible = card.QuerySelectorAll("[data-pricing-section]:not([hidden])").ToList();
            var shown = Assert.Single(visible);
            Assert.Equal("Flat Cost", shown.QuerySelector("h3")!.TextContent.Trim());
            Assert.Contains("flat-cost", shown.InnerHtml);
            Assert.Contains("did not get a breakdown", shown.TextContent);

            var hidden = Assert.Single(card.QuerySelectorAll("[data-pricing-section][hidden]"));
            Assert.Equal("Labor and Parts", hidden.QuerySelector("h3")!.TextContent.Trim());
            Assert.NotNull(hidden.QuerySelector("input#labor-hours"));

            Assert.Empty(card.QuerySelectorAll(".card-header"));
            Assert.Single(cut.FindAll(".card-footer"));
            Assert.Equal(["Flat Cost", "Labor and Parts"], cut.FindAll("fieldset .form-check-label").Select(label => label.TextContent.Trim()));
        });
    }

    [Fact]
    public void Add_RequiredNoteIsTheFirstThingInTheCardBody_NotInTheFooter()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find("section.card > .card-body");
            Assert.Equal("* Required", body.FirstElementChild!.TextContent.Trim());
            Assert.DoesNotContain("Required", cut.Find("section.card > .card-footer").TextContent);
        });
    }

    [Fact]
    public void Add_WholeFormIsInOneCardBody_AndWhatWasDoneIsFullWidth()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find("section.card > .card-body");
            Assert.NotNull(body.QuerySelector("input#line-description"));
            Assert.NotNull(body.QuerySelector("fieldset input#pricing-flat"));
            Assert.NotNull(body.QuerySelector("input#flat-cost"));
            Assert.Empty(cut.FindAll("form [class*='col-lg-8']"));
        });
    }

    [Fact]
    public void Add_FooterHoldsSaveLineItemWithAnIconAndCancelEachWithATitle()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var footer = cut.Find("section.card > .card-footer");
            var save = footer.QuerySelector("button[type='submit']")!;
            Assert.Equal("Save Line Item", save.TextContent.Trim());
            Assert.Equal("Save this line item", save.GetAttribute("title"));
            Assert.NotNull(save.QuerySelector("svg.bi[aria-hidden='true']"));

            var cancel = footer.QuerySelector("a.btn")!;
            Assert.Equal("Cancel", cancel.TextContent.Trim());
            Assert.False(string.IsNullOrWhiteSpace(cancel.GetAttribute("title")));
        });
    }

    [Fact]
    public void Add_ChoosingLaborAndPartsMakesThatSectionTheVisibleOne()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#pricing-breakdown").Change("Breakdown");

        cut.WaitForAssertion(() =>
        {
            var visible = Assert.Single(cut.FindAll("section.card [data-pricing-section]:not([hidden])"));
            Assert.Equal("Labor and Parts", visible.QuerySelector("h3")!.TextContent.Trim());
            Assert.NotNull(visible.QuerySelector("input#labor-hours"));
        });
    }

    [Fact]
    public void Add_ShowsThreeBlankPartRowsWithLabels()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll("[data-testid='part-row']").Count);
            for (var row = 0; row < 3; row++)
            {
                Assert.Equal("100", cut.Find($"input#part-{row}-name").GetAttribute("maxlength"));
                Assert.Equal("Part name", cut.Find($"label[for='part-{row}-name']").TextContent.Trim());
                Assert.Equal("Quantity", cut.Find($"label[for='part-{row}-quantity']").TextContent.Trim());
                Assert.Equal("Price per part", cut.Find($"label[for='part-{row}-unit-price']").TextContent.Trim());
                Assert.Equal("0.01", cut.Find($"input#part-{row}-unit-price").GetAttribute("step"));
                Assert.Equal($"Part {row + 1} name", cut.Find($"input#part-{row}-name").GetAttribute("aria-label"));
                Assert.Equal($"Part {row + 1} quantity", cut.Find($"input#part-{row}-quantity").GetAttribute("aria-label"));
                Assert.Equal($"Part {row + 1} price per part", cut.Find($"input#part-{row}-unit-price").GetAttribute("aria-label"));
            }

            Assert.Equal("Add more part rows", cut.Find("button[name='add-part-rows']").TextContent.Trim());
        });
    }

    [Fact]
    public void Add_EachPartInputHasADistinctNameForThePostedForm()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var names = cut.FindAll("[data-testid='part-row'] input").Select(input => input.GetAttribute("name")).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.All(names, name => Assert.False(string.IsNullOrEmpty(name)));
        });
    }

    [Fact]
    public void Add_AFlatCostLine_PostsOnlyTheFlatCostAndRedirectsToTheRecord()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#line-description").Change("Tire rotation");
        cut.Find("input#flat-cost").Change("40");
        cut.Find("input#labor-hours").Change("2");
        cut.Find("input#part-0-name").Change("Ignored part");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items", post.RequestUri!.AbsolutePath);
        var body = LastBody();
        Assert.Equal("Tire rotation", body.GetProperty("description").GetString());
        Assert.Equal(40m, body.GetProperty("flatCost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("laborHours").ValueKind);
        Assert.Equal(0, body.GetProperty("parts").GetArrayLength());
    }

    [Fact]
    public void Add_ABreakdownLine_PostsLaborAndTheFilledPartsOnly()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#line-description").Change("Oil change");
        cut.Find("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#flat-cost").Change("99");
        cut.Find("input#labor-hours").Change("1.5");
        cut.Find("input#labor-rate").Change("90");
        cut.Find("input#part-1-name").Change("Oil filter");
        cut.Find("input#part-1-quantity").Change("2");
        cut.Find("input#part-1-unit-price").Change("12.5");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        var body = LastBody();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("flatCost").ValueKind);
        Assert.Equal(1.5m, body.GetProperty("laborHours").GetDecimal());
        Assert.Equal(90m, body.GetProperty("laborRate").GetDecimal());
        var part = Assert.Single(body.GetProperty("parts").EnumerateArray());
        Assert.Equal("Oil filter", part.GetProperty("name").GetString());
        Assert.Equal(2m, part.GetProperty("quantity").GetDecimal());
        Assert.Equal(12.5m, part.GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public void Add_ADiscountLine_PostsTheNegativeFlatCost()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#line-description").Change("Coupon");
        cut.Find("input#flat-cost").Change("-4.04");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        Assert.Equal(-4.04m, LastBody().GetProperty("flatCost").GetDecimal());
    }

    [Fact]
    public void Add_TotalLabor_FollowsTheHourlyRateInTheLaborSection()
    {
        var cut = RenderCreate();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-pricing-section='Breakdown']");
            Assert.Equal(
                ["labor-hours", "labor-rate", "labor-cost"],
                section.QuerySelectorAll("input[id^='labor-']").Select(input => input.Id));
            Assert.NotNull(cut.Find("label[for='labor-cost']"));
            Assert.Equal("Total labor ($)", cut.Find("label[for='labor-cost']").TextContent.Trim());
            var total = cut.Find("input#labor-cost");
            Assert.False(total.HasAttribute("readonly"));
            Assert.Equal("0.01", total.GetAttribute("step"));
            Assert.Equal("0", total.GetAttribute("min"));
            Assert.True(total.HasAttribute("data-labor-total"));
        });
    }

    [Fact]
    public void Add_ALaborTotalOnlyLine_PostsTheTotalWithoutHoursOrRate()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#line-description").Change("Brake job");
        cut.Find("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#labor-cost").Change("180");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        var body = LastBody();
        Assert.Equal(180m, body.GetProperty("laborCost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("laborHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("laborRate").ValueKind);
    }

    [Fact]
    public void Add_HoursAndRate_PostNoEnteredTotal()
    {
        var cut = RenderCreate();
        cut.WaitForElement("input#line-description").Change("Oil change");
        cut.Find("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#labor-hours").Change("2");
        cut.Find("input#labor-rate").Change("90");
        cut.Find("input#labor-cost").Change("1");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        Assert.Equal(JsonValueKind.Null, LastBody().GetProperty("laborCost").ValueKind);
    }

    [Fact]
    public void Add_ARejectedTotalLabor_ShowsTheMessageBesideTheField()
    {
        var cut = RenderCreate(() => ValidationProblem(("LaborCost", "Total labor must be between 0.00 and 9,999,999.99.")));
        cut.WaitForElement("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#labor-cost").Change("-5");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Total labor must be between", cut.Find("#laborcost-error").TextContent);
            Assert.Equal("laborcost-error", cut.Find("input#labor-cost").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void Add_ARejectedSave_ShowsMessagesBesideTheFieldsAndPartRowsAndKeepsTheValues()
    {
        var cut = RenderCreate(() => ValidationProblem(
            ("Description", "Description is required."),
            ("Pricing", "Enter a flat cost, or labor hours and a rate, or at least one part."),
            ("LaborRate", "Hourly rate is required with labor hours."),
            ("Parts[1].Quantity", "Quantity must be between 0.01 and 9,999.99.")));
        cut.WaitForElement("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#labor-hours").Change("1");
        cut.Find("input#part-0-name").Change("Oil");
        cut.Find("input#part-1-name").Change("Filter");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Description is required.", cut.Find("#description-error").TextContent);
            Assert.Contains("Enter a flat cost", cut.Find("#pricing-error").TextContent);
            Assert.Contains("Hourly rate is required", cut.Find("#laborrate-error").TextContent);
            Assert.Equal("Part 2: Quantity must be between 0.01 and 9,999.99.", cut.Find("#part-1-error").TextContent.Trim());
            Assert.Equal("laborrate-error", cut.Find("input#labor-rate").GetAttribute("aria-describedby"));
            Assert.Equal("1", cut.Find("input#labor-hours").GetAttribute("value"));
            Assert.Equal("Filter", cut.Find("input#part-1-name").GetAttribute("value"));
            Assert.True(cut.Find("input#pricing-breakdown").HasAttribute("checked"));
        });
    }

    [Fact]
    public void Add_WhenTheRecordIsFull_ShowsTheLimitMessage()
    {
        var cut = RenderCreate(() => Problem(HttpStatusCode.Conflict, "A service record can have up to 50 line items."));
        cut.WaitForElement("input#line-description").Change("One too many");
        cut.Find("input#flat-cost").Change("1");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.Contains("A service record can have up to 50 line items.", cut.Find("[data-testid='form-message']").TextContent));
    }

    [Fact]
    public void Add_WhenTheRecordDisappears_ShowsTheNotFoundState()
    {
        var cut = RenderCreate(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        cut.WaitForElement("input#line-description").Change("x");
        cut.Find("input#flat-cost").Change("1");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Markup);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void AddMoreRows_AddsThreeBlankRowsKeepsTheValuesAndSavesNothing()
    {
        var cut = RenderCreate(httpContext: AddRowsPost());
        cut.WaitForElement("input#line-description").Change("Oil change");
        cut.Find("input#part-0-name").Change("Oil");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(6, cut.FindAll("[data-testid='part-row']").Count);
            Assert.Equal("Oil change", cut.Find("input#line-description").GetAttribute("value"));
            Assert.Equal("Oil", cut.Find("input#part-0-name").GetAttribute("value"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void ThePartRowsNeverGoBeyond25()
    {
        var parts = Enumerable.Range(0, 23).Select(i => new ServicePartDto { Name = $"P{i}", Quantity = 1m, UnitPrice = 1m, TotalPrice = 1m }).ToList();
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Many", Parts = parts, LaborHours = 1m, LaborRate = 1m, TotalCost = 24m };
        var record = RecordDetail(vehicleId, recordId, lineItems: [line]);
        RegisterApi(request => Respond(request, record, null));
        var cut = Render<ServiceLineItemEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId)
            .Add(page => page.LineItemId, line.Id)
            .AddCascadingValue<HttpContext>(AddRowsPost()));
        cut.WaitForElement("form[data-testid='line-item-form']");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll("[data-testid='part-row']").Count));
    }

    [Fact]
    public void Edit_AFlatLine_SelectsFlatCostAndPrefills()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Tire rotation", FlatCost = 40m, Parts = [], TotalCost = 40m };

        var cut = RenderEdit(line);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit line item", cut.Find("h1").TextContent);
            Assert.Equal("Tire rotation", cut.Find("input#line-description").GetAttribute("value"));
            Assert.True(cut.Find("input#pricing-flat").HasAttribute("checked"));
            Assert.Equal("40", cut.Find("input#flat-cost").GetAttribute("value"));
            Assert.Equal(3, cut.FindAll("[data-testid='part-row']").Count);
        });
    }

    [Fact]
    public void Edit_ABreakdownLine_SelectsLaborAndPartsAndAddsThreeBlankRows()
    {
        var line = new ServiceLineItemDto
        {
            Id = Guid.NewGuid(),
            Description = "Oil change",
            LaborHours = 1.5m,
            LaborRate = 90m,
            Parts =
            [
                new ServicePartDto { Name = "Oil filter", Quantity = 1m, UnitPrice = 12.50m, TotalPrice = 12.50m },
                new ServicePartDto { Name = "Oil", Quantity = 5m, UnitPrice = 8.25m, TotalPrice = 41.25m },
            ],
            TotalCost = 188.75m,
        };

        var cut = RenderEdit(line);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input#pricing-breakdown").HasAttribute("checked"));
            Assert.Equal("1.5", cut.Find("input#labor-hours").GetAttribute("value"));
            Assert.Equal("90", cut.Find("input#labor-rate").GetAttribute("value"));
            Assert.Equal(5, cut.FindAll("[data-testid='part-row']").Count);
            Assert.Equal("Oil filter", cut.Find("input#part-0-name").GetAttribute("value"));
            Assert.Equal("8.25", cut.Find("input#part-1-unit-price").GetAttribute("value"));
            Assert.Equal(string.Empty, cut.Find("input#part-2-name").GetAttribute("value") ?? string.Empty);
        });
    }

    [Fact]
    public void Edit_ALaborTotalOnlyLine_SelectsLaborAndPartsAndPrefillsAnEditableTotal()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Brake job", LaborCost = 180m, Parts = [], TotalCost = 180m };

        var cut = RenderEdit(line);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input#pricing-breakdown").HasAttribute("checked"));
            Assert.Equal("180", cut.Find("input#labor-cost").GetAttribute("value"));
            Assert.False(cut.Find("input#labor-cost").HasAttribute("readonly"));
        });
    }

    [Fact]
    public void Edit_AHoursAndRateLine_ShowsTheCalculatedTotalReadOnly()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Oil change", LaborHours = 1.5m, LaborRate = 90m, LaborCost = 135m, Parts = [], TotalCost = 135m };

        var cut = RenderEdit(line);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(135m, decimal.Parse(cut.Find("input#labor-cost").GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(cut.Find("input#labor-cost").HasAttribute("readonly"));
        });
    }

    [Fact]
    public void Edit_ADiscountLine_PrefillsTheNegativeFlatCost()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Coupon", FlatCost = -4.04m, Parts = [], TotalCost = -4.04m };

        var cut = RenderEdit(line);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input#pricing-flat").HasAttribute("checked"));
            Assert.Equal("-4.04", cut.Find("input#flat-cost").GetAttribute("value"));
        });
    }

    [Fact]
    public void Edit_Save_PutsTheLineAndRedirectsToTheRecord()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "Tire rotation", FlatCost = 40m, Parts = [], TotalCost = 40m };
        var cut = RenderEdit(line, () => Json(RecordDetail(vehicleId, recordId)));
        cut.WaitForElement("input#flat-cost").Change("45");

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{line.Id}", put.RequestUri!.AbsolutePath);
        Assert.Equal(45m, LastBody().GetProperty("flatCost").GetDecimal());
    }

    [Fact]
    public void Edit_AnUnknownLine_ShowsTheNotFoundState()
    {
        var line = new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "x", FlatCost = 1m, Parts = [], TotalCost = 1m };
        var record = RecordDetail(vehicleId, recordId);
        RegisterApi(request => Respond(request, record, null));

        var cut = Render<ServiceLineItemEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId)
            .Add(page => page.LineItemId, line.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be found", cut.Markup);
            Assert.Empty(cut.FindAll("form"));
        });
    }
}

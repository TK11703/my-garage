using Bunit;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.Drafts;
using MyGarage.Web.ServiceRecords;

namespace MyGarage.Web.Tests.Drafts;

public class ServiceRecordProposalEditorTests : DraftTestContext
{
    private ServiceProposalForm model = ServiceProposalForm.From(SampleServiceRecord());

    private IRenderedComponent<ServiceRecordProposalEditor> RenderEditor(IReadOnlyDictionary<string, string>? errors = null) =>
        Render<ServiceRecordProposalEditor>(parameters =>
        {
            parameters.Add(editor => editor.Model, model);
            if (errors is not null)
            {
                parameters.Add(editor => editor.FieldErrors, errors);
            }
        });

    [Fact]
    public void ShowsTheRecordFields_WithLabelsRequiredMarkersAndPostedNames()
    {
        var cut = RenderEditor();

        var date = cut.Find("input#serviceRecord-dateServiced");
        Assert.Equal("date", date.GetAttribute("type"));
        Assert.Equal("2026-10-12", date.GetAttribute("value"));
        Assert.Equal("true", date.GetAttribute("aria-required"));
        Assert.Equal("Model.Service.DateServiced", date.GetAttribute("name"));
        Assert.NotNull(cut.Find("label[for='serviceRecord-dateServiced']"));

        var odometer = cut.Find("input#serviceRecord-odometerReading");
        Assert.Equal("number", odometer.GetAttribute("type"));
        Assert.Equal("45210", odometer.GetAttribute("value"));
        Assert.Equal("9999999", odometer.GetAttribute("max"));
        Assert.Equal("numeric", odometer.GetAttribute("inputmode"));
        Assert.Equal("true", odometer.GetAttribute("aria-required"));
        Assert.Equal("Model.Service.OdometerReading", odometer.GetAttribute("name"));
        Assert.NotNull(cut.Find("label[for='serviceRecord-odometerReading']"));

        var shop = cut.Find("input#serviceRecord-shopOrPerson");
        Assert.Equal("Joe's Garage", shop.GetAttribute("value"));
        Assert.Equal("100", shop.GetAttribute("maxlength"));
        Assert.Null(shop.GetAttribute("aria-required"));
        Assert.Equal("Model.Service.ShopOrPerson", shop.GetAttribute("name"));
        Assert.NotNull(cut.Find("label[for='serviceRecord-shopOrPerson']"));
    }

    [Fact]
    public void EveryInput_HasALabel()
    {
        var cut = RenderEditor();

        var inputs = cut.FindAll("input[id]:not([type='hidden']):not([type='radio'])");
        Assert.NotEmpty(inputs);
        Assert.All(inputs, input => Assert.NotNull(cut.Find($"label[for='{input.Id}']")));
        Assert.All(cut.FindAll("input[type='radio']"), radio => Assert.NotNull(cut.Find($"label[for='{radio.Id}']")));
    }

    [Fact]
    public void LineItems_AreShownInOrder_WithTheirDescriptionAndPricing()
    {
        var cut = RenderEditor();

        var lines = cut.FindAll("[data-testid='line-item']");
        Assert.Equal(2, lines.Count);
        Assert.Equal("Oil change", lines[0].QuerySelector("input[id$='-description']")!.GetAttribute("value"));
        Assert.Equal("Model.Service.Lines[0].Description", lines[0].QuerySelector("input[id$='-description']")!.GetAttribute("name"));
        Assert.Equal("serviceRecord-lineItems-0-description", lines[0].QuerySelector("input[id$='-description']")!.Id);
        Assert.Equal("Brake job", lines[1].QuerySelector("input[id$='-description']")!.GetAttribute("value"));
        Assert.Equal("serviceRecord-lineItems-1", lines[1].Id);
    }

    [Fact]
    public void AFlatLine_ShowsOnlyTheFlatCostGroup()
    {
        var cut = RenderEditor();

        var line = cut.FindAll("[data-testid='line-item']")[0];
        Assert.True(line.QuerySelector("input[id$='-pricing-flat']")!.HasAttribute("checked"));
        Assert.False(line.QuerySelector("input[id$='-pricing-breakdown']")!.HasAttribute("checked"));
        var visible = Assert.Single(line.QuerySelectorAll("[data-pricing-section]:not([hidden])"));
        Assert.Equal("Flat", visible.GetAttribute("data-pricing-section"));
        Assert.Equal("40", visible.QuerySelector("input[id$='-flatCost']")!.GetAttribute("value"));
        Assert.Equal("Model.Service.Lines[0].FlatCost", visible.QuerySelector("input[id$='-flatCost']")!.GetAttribute("name"));
        Assert.Equal("Breakdown", Assert.Single(line.QuerySelectorAll("[data-pricing-section][hidden]")).GetAttribute("data-pricing-section"));
    }

    [Fact]
    public void ALaborAndPartsLine_ShowsOnlyThatGroup_WithItsPartRows()
    {
        var cut = RenderEditor();

        var line = cut.FindAll("[data-testid='line-item']")[1];
        Assert.True(line.QuerySelector("input[id$='-pricing-breakdown']")!.HasAttribute("checked"));
        var visible = Assert.Single(line.QuerySelectorAll("[data-pricing-section]:not([hidden])"));
        Assert.Equal("Breakdown", visible.GetAttribute("data-pricing-section"));
        Assert.Equal("1.5", visible.QuerySelector("input[id$='-laborHours']")!.GetAttribute("value"));
        Assert.Equal("90", visible.QuerySelector("input[id$='-laborRate']")!.GetAttribute("value"));

        var part = Assert.Single(visible.QuerySelectorAll("[data-testid='part-row']"));
        Assert.Equal("Brake pad", part.QuerySelector("input[id$='-parts-0-name']")!.GetAttribute("value"));
        Assert.Equal("Model.Service.Lines[1].Parts[0].Name", part.QuerySelector("input[id$='-parts-0-name']")!.GetAttribute("name"));
        Assert.Equal("serviceRecord-lineItems-1-parts-0-name", part.QuerySelector("input[id$='-parts-0-name']")!.Id);
        Assert.Equal("2", part.QuerySelector("input[id$='-parts-0-quantity']")!.GetAttribute("value"));
        Assert.Equal("12.5", part.QuerySelector("input[id$='-parts-0-unitPrice']")!.GetAttribute("value"));
    }

    [Fact]
    public void ChoosingLaborAndParts_SwapsTheGroups_AndOnlyTheChosenOneIsSent()
    {
        var cut = RenderEditor();
        var radio = cut.Find("#serviceRecord-lineItems-0-pricing-breakdown");

        radio.Change("Breakdown");

        var line = cut.FindAll("[data-testid='line-item']")[0];
        Assert.Equal(PricingMode.Breakdown, model.Lines[0].Pricing);
        Assert.Equal("Breakdown", Assert.Single(line.QuerySelectorAll("[data-pricing-section]:not([hidden])")).GetAttribute("data-pricing-section"));

        var request = model.Lines[0].ToRequest();
        Assert.Null(request.FlatCost);
    }

    [Fact]
    public void ChoosingFlat_OnALaborLine_DropsTheLaborFromTheRequest()
    {
        var cut = RenderEditor();

        cut.Find("#serviceRecord-lineItems-1-pricing-flat").Change("Flat");
        cut.Find("#serviceRecord-lineItems-1-flatCost").Change("150");

        var request = model.Lines[1].ToRequest();
        Assert.Equal(150m, request.FlatCost);
        Assert.Null(request.LaborHours);
        Assert.Null(request.LaborRate);
        Assert.Empty(request.Parts);
    }

    [Fact]
    public void Editing_UpdatesTheModel()
    {
        var cut = RenderEditor();

        cut.Find("#serviceRecord-dateServiced").Change("2026-09-01");
        cut.Find("#serviceRecord-odometerReading").Change("50000");
        cut.Find("#serviceRecord-shopOrPerson").Change("Quick Lube");
        cut.Find("#serviceRecord-lineItems-0-description").Change("Tire rotation");
        cut.Find("#serviceRecord-lineItems-1-parts-0-name").Change("Rotor");
        cut.Find("#serviceRecord-lineItems-1-parts-0-quantity").Change("4");
        cut.Find("#serviceRecord-lineItems-1-parts-0-unitPrice").Change("20.25");

        Assert.Equal(new DateOnly(2026, 9, 1), model.DateServiced);
        Assert.Equal(50000, model.OdometerReading);
        Assert.Equal("Quick Lube", model.ShopOrPerson);
        Assert.Equal("Tire rotation", model.Lines[0].Description);
        Assert.Equal("Rotor", model.Lines[1].Parts[0].Name);
        Assert.Equal(4m, model.Lines[1].Parts[0].Quantity);
        Assert.Equal(20.25m, model.Lines[1].Parts[0].UnitPrice);
    }

    [Fact]
    public void RunningTotals_ShowEachPartEachLineAndTheWholeProposal()
    {
        var cut = RenderEditor();

        var lines = cut.FindAll("[data-testid='line-item']");
        Assert.Equal("$40.00", lines[0].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());
        Assert.Equal("$160.00", lines[1].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());
        Assert.Equal("$25.00", lines[1].QuerySelector("[data-testid='part-total']")!.TextContent.Trim());
        Assert.Equal("$200.00", cut.Find("[data-testid='proposal-total']").TextContent.Trim());
    }

    [Fact]
    public void Totals_FollowTheEdits()
    {
        var cut = RenderEditor();

        cut.Find("#serviceRecord-lineItems-0-flatCost").Change("55.5");
        cut.Find("#serviceRecord-lineItems-1-parts-0-quantity").Change("3");

        var lines = cut.FindAll("[data-testid='line-item']");
        Assert.Equal("$55.50", lines[0].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());
        Assert.Equal("$37.50", lines[1].QuerySelector("[data-testid='part-total']")!.TextContent.Trim());
        Assert.Equal("$172.50", lines[1].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());
        Assert.Equal("$228.00", cut.Find("[data-testid='proposal-total']").TextContent.Trim());
    }

    [Fact]
    public void EveryAddAndRemoveControl_IsASubmitButtonNamedAfterWhatItActsOn()
    {
        var cut = RenderEditor();

        var add = cut.Find("button[name='action'][value='add-line']");
        Assert.Equal("submit", add.GetAttribute("type"));
        Assert.Equal("Add line item", add.TextContent.Trim());
        Assert.True(add.HasAttribute("formnovalidate"));

        var removeSecondLine = cut.Find("button[value='remove-line:1']");
        Assert.Equal("Remove line item 2", removeSecondLine.GetAttribute("aria-label"));
        Assert.Contains("btn-sm", removeSecondLine.ClassList);
        Assert.Contains("btn-outline-danger", removeSecondLine.ClassList);

        Assert.Equal("Add part to line item 2", cut.Find("button[value='add-part:1']").GetAttribute("aria-label"));
        Assert.Equal("Remove part 1 from line item 2", cut.Find("button[value='remove-part:1:0']").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button[name='action']"), button => Assert.Equal("submit", button.GetAttribute("type")));
    }

    [Fact]
    public void WithNoLineItems_TheEditorSaysSo_AndOffersToAddOne()
    {
        model = ServiceProposalForm.From(new ServiceRecordProposal());

        var cut = RenderEditor();

        Assert.Contains("No line items found.", cut.Find("[data-testid='no-lines']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='line-item']"));
        Assert.NotNull(cut.Find("button[value='add-line']"));
        Assert.Equal("$0.00", cut.Find("[data-testid='proposal-total']").TextContent.Trim());
    }

    [Fact]
    public void ADocumentTotal_IsKeptInAHiddenField()
    {
        var cut = RenderEditor();

        var hidden = cut.Find("input[type='hidden'][name='Model.Service.DocumentTotal']");
        Assert.Equal("200", hidden.GetAttribute("value"));
    }

    [Fact]
    public void FieldErrors_AppearBesideTheirFields_AndAreTiedToThemForScreenReaders()
    {
        var cut = RenderEditor(new Dictionary<string, string>
        {
            ["serviceRecord.odometerReading"] = "Odometer reading is required.",
            ["serviceRecord.lineItems[1].parts[0].unitPrice"] = "Price per part cannot be negative.",
        });

        var error = cut.Find("#serviceRecord-odometerReading-error");
        Assert.Equal("Odometer reading is required.", error.TextContent.Trim());
        Assert.Contains("invalid-feedback", error.ClassList);
        Assert.Contains("d-block", error.ClassList);
        Assert.Equal("serviceRecord-odometerReading-error", cut.Find("#serviceRecord-odometerReading").GetAttribute("aria-describedby"));
        Assert.Equal("Price per part cannot be negative.", cut.Find("#serviceRecord-lineItems-1-parts-0-unitPrice-error").TextContent.Trim());
        Assert.Empty(cut.FindAll("#serviceRecord-dateServiced-error"));
    }

    [Fact]
    public void ProposalText_IsEncoded()
    {
        model = ServiceProposalForm.From(new ServiceRecordProposal
        {
            ShopOrPerson = "<b>Bold</b> Garage",
            LineItems = [new ServiceLineItemRequest { Description = "<i>Oil</i>", FlatCost = 1m }],
        });

        var cut = RenderEditor();

        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("i"));
        Assert.Equal("<b>Bold</b> Garage", cut.Find("#serviceRecord-shopOrPerson").GetAttribute("value"));
        Assert.Equal("<i>Oil</i>", cut.Find("#serviceRecord-lineItems-0-description").GetAttribute("value"));
    }

    [Fact]
    public void ThereAreNoTablesOrInlineStyles()
    {
        var cut = RenderEditor();

        Assert.Empty(cut.FindAll("table"));
        Assert.DoesNotContain("style=", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}

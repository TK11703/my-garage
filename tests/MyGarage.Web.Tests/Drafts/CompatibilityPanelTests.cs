using Bunit;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class CompatibilityPanelTests : DraftTestContext
{
    private IRenderedComponent<CompatibilityPanel> RenderPanel(CompatibilityDto? compatibility, bool saveFailed = false) =>
        Render<CompatibilityPanel>(parameters => parameters
            .Add(panel => panel.Compatibility, compatibility)
            .Add(panel => panel.SaveFailed, saveFailed));

    [Fact]
    public void IsAPoliteLiveRegionWithAHeading()
    {
        var cut = RenderPanel(Acceptable());

        var region = cut.Find("[data-testid='compatibility']");
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        var heading = cut.Find("#compatibility-heading");
        Assert.Equal("Compatibility", heading.TextContent.Trim());
        Assert.Equal("-1", heading.GetAttribute("tabindex"));
        Assert.Equal("compatibility-heading", region.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void AnAcceptableProposal_SaysItIsReadyToSave()
    {
        var cut = RenderPanel(Acceptable());

        Assert.Equal("Ready to save", cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
        Assert.Contains("text-success", cut.Find("[data-testid='compatibility-summary']").ClassList);
    }

    [Theory]
    [InlineData(1, "1 thing needs attention")]
    [InlineData(2, "2 things need attention")]
    public void AProposalThatCannotBeSaved_CountsWhatNeedsAttention(int problems, string expected)
    {
        var fields = Enumerable.Range(0, problems)
            .Select(index => ("serviceRecord.odometerReading" + index, CompatibilityStatus.Missing, (string?)"Odometer reading is required."))
            .Append(("serviceRecord.dateServiced", CompatibilityStatus.Ok, null))
            .ToArray();

        var cut = RenderPanel(NeedsAttention(DraftKind.ServiceRecord, fields));

        Assert.Equal(expected, cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
    }

    [Fact]
    public void EachField_ShowsItsStatusInWords_ItsMessage_AndALinkThatFocusesTheField()
    {
        var cut = RenderPanel(NeedsAttention(
            DraftKind.ServiceRecord,
            ("serviceRecord.dateServiced", CompatibilityStatus.Ok, null),
            ("serviceRecord.odometerReading", CompatibilityStatus.Missing, "Odometer reading is required."),
            ("serviceRecord.lineItems[1].parts[0].unitPrice", CompatibilityStatus.Invalid, "Price per part cannot be negative.")));

        var rows = cut.FindAll("[data-testid='compatibility-field']");
        Assert.Equal(3, rows.Count);

        // Problems come first, then what is fine.
        Assert.Equal(["missing", "invalid", "ok"], rows.Select(row => row.GetAttribute("data-status")));

        var missing = rows[0];
        Assert.Equal("Missing", missing.QuerySelector("[data-testid='compatibility-status']")!.TextContent.Trim());
        Assert.Equal("Odometer reading is required.", missing.QuerySelector("[data-testid='compatibility-message']")!.TextContent.Trim());
        var link = missing.QuerySelector("a")!;
        Assert.Equal("#serviceRecord-odometerReading", link.GetAttribute("href"));
        Assert.Equal("Odometer reading", link.TextContent.Trim());

        var invalid = rows[1];
        Assert.Equal("Invalid", invalid.QuerySelector("[data-testid='compatibility-status']")!.TextContent.Trim());
        Assert.Equal("#serviceRecord-lineItems-1-parts-0-unitPrice", invalid.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Line item 2, part 1: Unit price", invalid.QuerySelector("a")!.TextContent.Trim());

        Assert.Equal("OK", rows[2].QuerySelector("[data-testid='compatibility-status']")!.TextContent.Trim());
        Assert.Empty(rows[2].QuerySelectorAll("[data-testid='compatibility-message']"));
    }

    [Fact]
    public void Warnings_AreListedButNeverBlock()
    {
        var cut = RenderPanel(Acceptable(DraftKind.ServiceRecord, "No line items were found.", "The same file was already received in another draft."));

        var warnings = cut.FindAll("[data-testid='compatibility-warning']");
        Assert.Equal(["No line items were found.", "The same file was already received in another draft."], warnings.Select(item => item.TextContent.Trim()));
        Assert.All(cut.FindAll(".alert-warning"), alert => Assert.NotNull(alert.GetAttribute("role")));
        Assert.Equal("Ready to save", cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
    }

    [Fact]
    public void WithNoWarnings_NoWarningBlockIsShown()
    {
        var cut = RenderPanel(Acceptable());

        Assert.Empty(cut.FindAll("[data-testid='compatibility-warning']"));
        Assert.Empty(cut.FindAll(".alert-warning"));
    }

    [Fact]
    public void BeforeAnyCheck_ThePanelSaysSo()
    {
        var cut = RenderPanel(null);

        Assert.Contains("Not checked yet.", cut.Find("[data-testid='compatibility']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='compatibility-summary']"));
    }

    [Fact]
    public void ARefusedSave_IsAnnouncedAsAnAlert()
    {
        var cut = RenderPanel(NeedsAttention(DraftKind.ServiceRecord, ("serviceRecord.odometerReading", CompatibilityStatus.Missing, "Required")), saveFailed: true);

        var alert = cut.Find("[data-testid='compatibility-save-failed']");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("could not be saved", alert.TextContent);
    }

    [Fact]
    public void NoSaveFailedAlert_Normally()
    {
        var cut = RenderPanel(Acceptable());

        Assert.Empty(cut.FindAll("[data-testid='compatibility-save-failed']"));
    }

    [Fact]
    public void MessagesAreEncoded()
    {
        var cut = RenderPanel(NeedsAttention(DraftKind.ServiceRecord, ("serviceRecord.shopOrPerson", CompatibilityStatus.Invalid, "<b>Too</b> long")));

        Assert.Empty(cut.FindAll("b"));
        Assert.Contains("<b>Too</b> long", cut.Find("[data-testid='compatibility-message']").TextContent);
    }
}

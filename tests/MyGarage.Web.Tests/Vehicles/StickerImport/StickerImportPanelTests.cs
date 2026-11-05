using Bunit;
using Microsoft.AspNetCore.Components;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

public class StickerImportPanelTests : VehicleTestContext
{
    private IRenderedComponent<StickerImportPanel> RenderPanel(
        bool existing = false,
        StickerImportResultDto? result = null,
        string? message = null,
        string level = "danger") =>
        Render<StickerImportPanel>(parameters => parameters
            .Add(panel => panel.Existing, existing)
            .Add(panel => panel.Result, result)
            .Add(panel => panel.Message, message)
            .Add(panel => panel.Level, level)
            .Add(panel => panel.OnImport, EventCallback.Factory.Create(this, () => { })));

    private static StickerImportResultDto Result(
        StickerImportOutcome outcome = StickerImportOutcome.Filled,
        int fields = 3,
        IReadOnlyList<StickerFeatureDto>? features = null,
        IReadOnlyList<StickerNoticeDto>? notices = null) =>
        new() { Outcome = outcome, FieldsFilled = fields, Features = features ?? [], Notices = notices ?? [] };

    [Fact]
    public void ShowsTheHeadingHelpAndLimits()
    {
        var cut = RenderPanel();

        Assert.Equal("Import from sticker", cut.Find("h2").TextContent);
        var text = cut.Find("section").TextContent;
        Assert.Contains("Upload the window sticker to fill in the vehicle details.", text);
        Assert.Contains("PDF, JPG, PNG, or WebP, up to 10 MB", text);
        Assert.Contains("Nothing is saved until you save the vehicle, and the sticker file is not kept.", text);
    }

    [Fact]
    public void HasAMultipartPostFormNamedImportStickerWithALabelledFileInput()
    {
        var cut = RenderPanel();

        var form = cut.Find("form[data-sticker-import]");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("multipart/form-data", form.GetAttribute("enctype"));
        var input = form.QuerySelector("input[type='file']")!;
        Assert.Equal("stickerFile", input.GetAttribute("name"));
        Assert.Equal("application/pdf,image/jpeg,image/png,image/webp", input.GetAttribute("accept"));
        Assert.Equal("sticker-help", input.GetAttribute("aria-describedby"));
        Assert.Equal("Choose the window sticker file", cut.Find("label[for='sticker-file']").TextContent);
        Assert.NotNull(cut.Find("#sticker-help"));
        Assert.NotNull(form.QuerySelector("[data-sticker-preview][aria-live='polite']"));
    }

    [Fact]
    public void TheAcceptImportButtonHasTheBusyState()
    {
        var cut = RenderPanel();

        var button = cut.Find("button[data-testid='accept-import']");
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Contains("Accept import", button.TextContent);
        Assert.Equal("Reading the sticker...", button.GetAttribute("data-busy-text"));
        Assert.Contains("This may take up to two minutes.", cut.Find("form").TextContent);
        Assert.NotNull(cut.Find("button[data-sticker-clear]"));
    }

    [Theory]
    [InlineData(false, "with the values found on the sticker", false)]
    [InlineData(true, "with the saved vehicle plus the values found on the sticker", true)]
    public void TheWarningNamesWhatReplacesTheUnsavedChanges(bool existing, string expected, bool mentionsSavedVehicle)
    {
        var cut = RenderPanel(existing);

        var warning = cut.Find("[data-testid='sticker-warning']").TextContent;
        Assert.Contains("Importing replaces any unsaved changes on this form", warning);
        Assert.Contains(expected, warning);
        Assert.Equal(mentionsSavedVehicle, warning.Contains("saved vehicle"));
    }

    [Fact]
    public void AFilledResult_ShowsTheCountAndAButtonToTheDetailsTab()
    {
        var cut = RenderPanel(result: Result(fields: 12, features: [new StickerFeatureDto { Name = "Sunroof" }, new StickerFeatureDto { Name = "Heated Seats" }]));

        Assert.Contains("Read 12 details and 2 features from the sticker. Review them on the Details tab.", cut.Find("[data-testid='sticker-read-count']").TextContent.Trim());
        var open = cut.Find("button[data-bs-target='#tab-details']");
        Assert.Equal("tab", open.GetAttribute("data-bs-toggle"));
        Assert.Equal("status", cut.Find("#sticker-result").GetAttribute("role"));
    }

    [Fact]
    public void ASingleDetail_IsWordedInTheSingular()
    {
        var cut = RenderPanel(result: Result(fields: 1));

        Assert.Contains("Read 1 detail from the sticker.", cut.Find("[data-testid='sticker-read-count']").TextContent);
    }

    [Fact]
    public void NoDetailsFound_ShowsTheMessageAndNoDetailsButton()
    {
        var cut = RenderPanel(result: Result(StickerImportOutcome.NoDetailsFound, 0, notices: [new StickerNoticeDto { Code = StickerNoticeCode.NoDetails, Message = "No vehicle details were found in that file." }]));

        Assert.Contains("No vehicle details were found in that file.", cut.Find("[data-testid='sticker-no-details']").TextContent);
        Assert.Empty(cut.FindAll("button[data-bs-target='#tab-details']"));
        Assert.Empty(cut.FindAll("[data-testid='sticker-notices']"));
    }

    [Fact]
    public void NoticesAreListed_AsPlainText()
    {
        var cut = RenderPanel(result: Result(
            StickerImportOutcome.Partial,
            notices:
            [
                new StickerNoticeDto { Code = StickerNoticeCode.UnreadableValues, Message = "These values could not be used and were left empty: VIN, Horsepower." },
                new StickerNoticeDto { Code = StickerNoticeCode.MultipleVehicles, Message = "<b>bold</b> one" },
            ]));

        var items = cut.FindAll("[data-testid='sticker-notices'] li");
        Assert.Equal(2, items.Count);
        Assert.Equal("These values could not be used and were left empty: VIN, Horsepower.", items[0].TextContent);
        Assert.Equal("<b>bold</b> one", items[1].TextContent);
        Assert.Empty(cut.FindAll("b"));
    }

    [Fact]
    public void AMessage_IsShownAsAnAlertWithItsLevel()
    {
        var cut = RenderPanel(message: StickerImportMessages.TooLarge, level: "warning");

        var alert = cut.Find("[data-testid='sticker-message']");
        Assert.Equal("That file is larger than 10 MB.", alert.TextContent);
        Assert.Contains("alert-warning", alert.ClassList);
        Assert.Equal("alert", alert.GetAttribute("role"));
    }

    [Fact]
    public void WithoutAResultOrMessage_NoSummaryIsShown()
    {
        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#sticker-result, [data-testid='sticker-message']"));
    }
}

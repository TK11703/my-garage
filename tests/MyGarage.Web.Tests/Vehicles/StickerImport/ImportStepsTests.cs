using Bunit;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

public class ImportStepsTests : VehicleTestContext
{
    [Fact]
    public void TheStepsReadStickerReadingReview_WithAManualEntryLink()
    {
        var cut = Render<ImportSteps>(parameters => parameters.Add(steps => steps.Current, "import"));

        Assert.Equal(["1Sticker", "2Reading", "3Review", "+Enter manually"], cut.FindAll("nav[aria-label='Steps'] li").Select(item => item.TextContent.Trim()));
        Assert.Equal("true", cut.Find("#tab-import-tab").GetAttribute("aria-selected"));
        Assert.Equal("#tab-import", cut.Find("#tab-import-tab").GetAttribute("data-bs-target"));
        Assert.Equal("#tab-details", cut.Find("#tab-details-tab").GetAttribute("data-bs-target"));
        var manual = cut.Find("a#tab-manual-tab");
        Assert.Equal("/vehicles/new?tab=details", manual.GetAttribute("href"));
        Assert.Equal("#tab-details", manual.GetAttribute("data-bs-target"));
        Assert.Empty(cut.FindAll(".mg-step.done"));
    }

    [Fact]
    public void AfterAnImport_StickerAndReadingAreDone_AndReviewIsOpen()
    {
        var cut = Render<ImportSteps>(parameters => parameters
            .Add(steps => steps.Current, "details")
            .Add(steps => steps.Imported, true));

        Assert.Equal(2, cut.FindAll(".mg-step.done").Count);
        Assert.Equal("true", cut.Find("#tab-details-tab").GetAttribute("aria-selected"));
        Assert.Equal("false", cut.Find("#tab-manual-tab").GetAttribute("aria-selected"));
    }

    [Fact]
    public void ManualEntryWithoutAnImport_MarksTheManualStepOpen()
    {
        var cut = Render<ImportSteps>(parameters => parameters.Add(steps => steps.Current, "details"));

        Assert.Equal("true", cut.Find("#tab-manual-tab").GetAttribute("aria-selected"));
        Assert.Equal("false", cut.Find("#tab-details-tab").GetAttribute("aria-selected"));
        Assert.Empty(cut.FindAll(".mg-step.done"));
    }
}

public class StickerReadSummaryTests : VehicleTestContext
{
    private static StickerImportResultDto Result() =>
        new()
        {
            Outcome = StickerImportOutcome.Filled,
            FieldsFilled = 6,
            Year = 2015,
            Make = "Jeep",
            Model = "Grand Cherokee",
            Vin = "1C4RJFBG0FC000000",
            Engine = "3.6L V6",
            FuelTypeId = Guid.NewGuid(),
            FuelTypeText = "Gasoline",
            VehicleTypeText = "SUV",
        };

    [Fact]
    public void EachValueReadIsListedWithItsFieldAndASourceTag()
    {
        var cut = Render<StickerResultsPanel>(parameters => parameters.Add(panel => panel.Result, Result()));

        var items = cut.FindAll("[data-testid='sticker-read-list'] [data-read]");
        Assert.Equal(["year", "make", "model", "vin", "vehicle-type", "engine", "fuel-type"], items.Select(item => item.GetAttribute("data-read")));
        var make = items.Single(item => item.GetAttribute("data-read") == "make");
        Assert.Equal("Make", make.QuerySelector("dt")!.TextContent.Trim());
        Assert.Equal("Jeep", make.QuerySelector("dd")!.TextContent.Replace("From sticker", string.Empty).Trim());
        Assert.Contains("From sticker", make.TextContent);
    }

    [Fact]
    public void TheVinAndAnUnmatchedLookupAreFlaggedInWords()
    {
        var cut = Render<StickerResultsPanel>(parameters => parameters.Add(panel => panel.Result, Result()));

        var vin = cut.Find("[data-read='vin']");
        Assert.Contains("Check it", vin.TextContent);
        Assert.Contains("mg-read-check", vin.ClassName);
        var type = cut.Find("[data-read='vehicle-type']");
        Assert.Contains("Not matched to your list. Pick one", type.TextContent);
        Assert.Contains("mg-read-check", type.ClassName);
        Assert.DoesNotContain("mg-read-check", cut.Find("[data-read='fuel-type']").ClassName);
    }

    [Fact]
    public void ValuesNotReadAreNotListed_AndNoDetailsHasNoList()
    {
        var partial = new StickerImportResultDto { Outcome = StickerImportOutcome.Partial, FieldsFilled = 1, Make = "Jeep" };
        var none = new StickerImportResultDto { Outcome = StickerImportOutcome.NoDetailsFound };

        Assert.Equal(["make"], Render<StickerResultsPanel>(parameters => parameters.Add(panel => panel.Result, partial))
            .FindAll("[data-read]").Select(item => item.GetAttribute("data-read")));
        Assert.Empty(Render<StickerResultsPanel>(parameters => parameters.Add(panel => panel.Result, none)).FindAll("[data-testid='sticker-read-list']"));
    }

    [Fact]
    public void ValuesAreEncoded()
    {
        var result = new StickerImportResultDto { Outcome = StickerImportOutcome.Partial, FieldsFilled = 1, Make = "<b>Jeep</b>" };

        var cut = Render<StickerResultsPanel>(parameters => parameters.Add(panel => panel.Result, result));

        Assert.Empty(cut.FindAll("[data-read] b"));
        Assert.Contains("<b>Jeep</b>", cut.Find("[data-read='make'] dd").TextContent);
    }
}

public class VehicleCreateStepsTests : VehicleTestContext
{
    [Fact]
    public void TheAddPageOpensOnTheStickerStep_WithStepsInsteadOfTabs()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("nav[aria-label='Steps']"));
            Assert.Empty(cut.FindAll("ul.mg-tabs"));
            Assert.Equal(["tab-import"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Equal("/vehicles/new?tab=details", cut.Find("a#tab-manual-tab").GetAttribute("href"));
        });
    }

    [Fact]
    public void TheManualLinkOpensTheDetailsFormWithoutScript()
    {
        RegisterApi(_ => Json(Detail()));
        NavigateTo("/vehicles/new?tab=details");

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Equal("true", cut.Find("#tab-manual-tab").GetAttribute("aria-selected"));
        });
    }
}

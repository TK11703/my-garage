using Bunit;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class ReadinessRingTests : DraftTestContext
{
    private IRenderedComponent<CompatibilityPanel> RenderPanel(CompatibilityDto? compatibility) =>
        Render<CompatibilityPanel>(parameters => parameters.Add(panel => panel.Compatibility, compatibility));

    [Fact]
    public void TheRingShowsHowManyCheckedFieldsAreOk_InWordsAndAsAnArc()
    {
        var cut = RenderPanel(NeedsAttention(
            DraftKind.ServiceRecord,
            ("serviceRecord.dateServiced", CompatibilityStatus.Ok, null),
            ("serviceRecord.shopOrPerson", CompatibilityStatus.Ok, null),
            ("serviceRecord.odometerReading", CompatibilityStatus.Missing, "Odometer reading is required."),
            ("serviceRecord.tax", CompatibilityStatus.Invalid, "Tax cannot be negative.")));

        var ring = cut.Find("svg[data-testid='readiness-ring']");
        Assert.Equal("img", ring.GetAttribute("role"));
        Assert.Equal("2 of 4 checks are OK", ring.GetAttribute("aria-label"));
        Assert.Equal("50 100", cut.Find("[data-testid='readiness-arc']").GetAttribute("stroke-dasharray"));
        Assert.Contains("mg-ring-bad", ring.ClassName);
        Assert.Equal("2/4", cut.Find("[data-testid='readiness-count']").TextContent.Trim());
    }

    [Fact]
    public void WhenEveryFieldIsOk_TheRingIsFullAndToneOk()
    {
        var cut = RenderPanel(Acceptable());

        var ring = cut.Find("svg[data-testid='readiness-ring']");
        Assert.Contains("mg-ring-ok", ring.ClassName);
        Assert.Equal("100 100", cut.Find("[data-testid='readiness-arc']").GetAttribute("stroke-dasharray"));
    }

    [Fact]
    public void NotCheckedYet_HasNoRing()
    {
        var cut = RenderPanel(null);

        Assert.Empty(cut.FindAll("svg[data-testid='readiness-ring']"));
    }
}

using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleTagsViewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static VehicleTagDto Tag(string name, string? category = null) =>
        category is null ? new() { Id = Guid.NewGuid(), Name = name } : new() { Id = Guid.NewGuid(), Name = name, Category = category };

    private IRenderedComponent<VehicleTagsView> RenderView(string? query, params VehicleTagDto[] tags) =>
        Render<VehicleTagsView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId)
            .Add(view => view.Tags, tags)
            .Add(view => view.Query, query));

    [Fact]
    public void NoFeatures_ShowsALinkToAddThem_AndNoSearch()
    {
        var cut = RenderView(null);

        Assert.Contains("No features yet.", cut.Markup);
        Assert.Contains($"/vehicles/{vehicleId}/edit?tab=description", cut.Find("a").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='feature-search']"));
    }

    [Fact]
    public void Features_AreShownInOneCardPerCategoryInTheFixedOrder_WithCounts()
    {
        var cut = RenderView(
            null,
            Tag("Heated Seats", "Comfort & Convenience"),
            Tag("Airbags", "Safety"),
            Tag("Lane Assist", "Safety"),
            Tag("Mystery"));

        var cards = cut.FindAll("[data-testid='feature-category']");
        Assert.Equal(["Safety", "Comfort & Convenience", "Other"], cards.Select(card => card.GetAttribute("data-category")));
        Assert.Equal(["2", "1", "1"], cards.Select(card => card.QuerySelector("[data-testid='feature-count']")!.TextContent.Trim()));
        Assert.Equal(["Airbags", "Lane Assist"], cards[0].QuerySelectorAll("[data-testid='tag']").Select(item => item.TextContent.Trim()));
        Assert.All(cards, card => Assert.Equal(card.QuerySelector("h3")!.Id, card.QuerySelector("ul")!.GetAttribute("aria-labelledby")));
    }

    [Fact]
    public void ACardListsTheFirstSevenAndFoldsTheRestAway()
    {
        var tags = Enumerable.Range(1, 10).Select(number => Tag($"Feature {number:00}", "Exterior")).ToArray();

        var cut = RenderView(null, tags);

        var card = cut.Find("[data-testid='feature-category']");
        Assert.Equal("10", card.QuerySelector("[data-testid='feature-count']")!.TextContent.Trim());
        var more = card.QuerySelector("details[data-testid='feature-more']")!;
        Assert.Equal("Show 3 more features", more.QuerySelector("summary")!.TextContent.Trim());
        Assert.Equal(3, more.QuerySelectorAll("[data-testid='tag']").Length);
        Assert.Equal(7, card.QuerySelector("ul")!.QuerySelectorAll("li").Length);
    }

    [Fact]
    public void ACardWithSevenOrFewerHasNoFoldedPart()
    {
        var cut = RenderView(null, Enumerable.Range(1, 7).Select(number => Tag($"Feature {number}", "Exterior")).ToArray());

        Assert.Empty(cut.FindAll("details"));
    }

    [Fact]
    public void Search_KeepsOnlyMatchingFeatures_IgnoringCase_AndShowsAllOfThem()
    {
        var tags = Enumerable.Range(1, 9).Select(number => Tag($"Sensor {number}", "Safety"))
            .Append(Tag("Heated Seats", "Comfort & Convenience"))
            .ToArray();

        var cut = RenderView("  SENSOR ", tags);

        Assert.Equal(["Safety"], cut.FindAll("[data-testid='feature-category']").Select(card => card.GetAttribute("data-category")));
        Assert.Equal(9, cut.FindAll("[data-testid='tag']").Count);
        Assert.Empty(cut.FindAll("details"));
        Assert.Contains("9 features match \"SENSOR\".", cut.Find("[data-testid='feature-search-summary']").TextContent);
        Assert.Equal("SENSOR", cut.Find("#feature-query").GetAttribute("value"));
        Assert.Equal($"/vehicles/{vehicleId}?tab=description", cut.Find("[data-testid='feature-search-summary'] a").GetAttribute("href"));
    }

    [Fact]
    public void Search_WithNoMatch_SaysSoAndOffersToClear()
    {
        var cut = RenderView("zzz", Tag("Airbags", "Safety"));

        Assert.Empty(cut.FindAll("[data-testid='feature-category']"));
        Assert.Contains("No features match \"zzz\".", cut.Markup);
        Assert.Contains("Clear search", cut.Find("[data-testid='feature-search-summary']").TextContent);
    }

    [Fact]
    public void SearchForm_IsAGetToTheDescriptionTabWithALabelledBox()
    {
        var cut = RenderView(null, Tag("Airbags", "Safety"));

        var form = cut.Find("form[data-testid='feature-search']");
        Assert.Equal("get", form.GetAttribute("method"));
        Assert.Equal($"/vehicles/{vehicleId}", form.GetAttribute("action"));
        Assert.Equal("search", form.GetAttribute("role"));
        Assert.Equal("description", form.QuerySelector("input[name='tab']")!.GetAttribute("value"));
        Assert.Equal("featureQuery", cut.Find("#feature-query").GetAttribute("name"));
        Assert.Equal("Search features", cut.Find("label[for='feature-query']").TextContent.Trim());
    }

    [Fact]
    public void FeatureAndSearchText_AreEncoded()
    {
        var cut = RenderView("<i>", Tag("<i>x</i>", "Safety"));

        Assert.Empty(cut.FindAll("[data-testid='tag'] i"));
        Assert.Equal("<i>x</i>", cut.Find("[data-testid='tag']").TextContent.Trim());
    }
}

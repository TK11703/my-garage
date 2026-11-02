using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCardTests : VehicleTestContext
{
    private IRenderedComponent<VehicleCard> RenderCard(VehicleSummaryDto vehicle) =>
        Render<VehicleCard>(parameters => parameters.Add(card => card.Vehicle, vehicle));

    [Fact]
    public void ShowsTheComputedNameAsLinkMakeModelAndFormattedMileage()
    {
        var vehicle = Summary("Subaru", "Outback", 42_000, year: 2019);

        var cut = RenderCard(vehicle);

        var link = cut.Find("a.stretched-link, .card-title a");
        Assert.Equal("2019 Subaru Outback", link.TextContent.Trim());
        Assert.Equal($"/vehicles/{vehicle.Id}", link.GetAttribute("href"));
        Assert.Contains("42,000 mi", cut.Markup);
    }

    [Fact]
    public void NoMileage_ShowsMileageNotSet() =>
        Assert.Contains("Mileage not set", RenderCard(Summary()).Markup);

    [Fact]
    public void NoMainImage_ShowsPlaceholderAndNoImageElement()
    {
        var cut = RenderCard(Summary());

        Assert.Empty(cut.FindAll("img"));
        Assert.NotEmpty(cut.FindAll("[data-testid='vehicle-placeholder']"));
    }

    [Fact]
    public void MainImage_RendersLazyImageWithAltText()
    {
        var imageId = Guid.NewGuid();
        var vehicle = Summary(mainImageId: imageId);

        var image = RenderCard(vehicle).Find("img");

        Assert.Equal($"/vehicles/{vehicle.Id}/images/{imageId}", image.GetAttribute("src"));
        Assert.Equal("2020 Ford F-150", image.GetAttribute("alt"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
    }

    [Fact]
    public void UserText_IsEncoded()
    {
        var cut = RenderCard(Summary("<script>alert(1)</script>", year: null));

        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("<script>alert(1)</script> F-150", cut.Find(".card-title a").TextContent);
    }

    [Fact]
    public void DoesNotUseTableMarkup() => Assert.DoesNotContain("<table", RenderCard(Summary()).Markup);

    [Fact]
    public void IsAnImageForwardCardWithAWidescreenPicture()
    {
        var cut = RenderCard(Summary(mainImageId: Guid.NewGuid()));

        Assert.Contains("mg-vehicle-card", cut.Find(".card").ClassList);
        Assert.Contains("ratio-16x9", cut.Find(".ratio").ClassList);
        Assert.Contains("card-img-top", cut.Find("img").ClassList);
    }

    [Fact]
    public void PlaceholderUsesTheSameWidescreenFrame()
    {
        var cut = RenderCard(Summary());

        Assert.Contains("ratio-16x9", cut.Find(".ratio").ClassList);
    }

    [Fact]
    public void MileageIsShownWithADecorativeIcon()
    {
        var cut = RenderCard(Summary(mileage: 5_000));

        var line = cut.Find("[data-testid='vehicle-mileage']");
        Assert.Equal("5,000 mi", line.TextContent.Trim());
        Assert.Equal("true", line.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void LongTextWrapsInsteadOfBreakingTheGrid()
    {
        var cut = RenderCard(Summary("A very long make name", "A very long model name"));

        Assert.Contains("text-break", cut.Find(".card-title a").ClassList);
    }

    [Fact]
    public void FuelAndTransmission_RenderAsBadgesBelowTheTitle()
    {
        var cut = RenderCard(Summary(fuelType: "Gasoline", transmission: "Automatic"));

        var badges = cut.Find("[data-testid='vehicle-badges']");
        Assert.Equal("Gasoline", badges.QuerySelector("[data-testid='vehicle-fuel']")!.TextContent.Trim());
        Assert.Equal("Automatic", badges.QuerySelector("[data-testid='vehicle-transmission']")!.TextContent.Trim());
        Assert.Equal(2, badges.QuerySelectorAll("span.badge").Length);
        Assert.Equal("vehicle-badges", cut.Find(".card-title + p").GetAttribute("data-testid"));
    }

    [Fact]
    public void NoFuelOrTransmission_OmitsTheBadgesEntirely()
    {
        var cut = RenderCard(Summary());

        Assert.Empty(cut.FindAll("[data-testid='vehicle-badges']"));
        Assert.Empty(cut.FindAll(".badge"));
    }

    [Fact]
    public void OnlyFuel_RendersASingleBadge()
    {
        var cut = RenderCard(Summary(fuelType: "Diesel"));

        Assert.Equal("Diesel", cut.Find("[data-testid='vehicle-fuel']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='vehicle-transmission']"));
        Assert.Single(cut.FindAll("span.badge"));
    }

    [Fact]
    public void OnlyTransmission_RendersASingleBadge()
    {
        var cut = RenderCard(Summary(transmission: "Manual"));

        Assert.Equal("Manual", cut.Find("[data-testid='vehicle-transmission']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='vehicle-fuel']"));
        Assert.Single(cut.FindAll("span.badge"));
    }

    [Fact]
    public void BadgeText_IsHtmlEncoded()
    {
        var cut = RenderCard(Summary(fuelType: "<b>x</b>", transmission: "<i>y</i>"));

        Assert.Equal("<b>x</b>", cut.Find("[data-testid='vehicle-fuel']").TextContent);
        Assert.Equal("<i>y</i>", cut.Find("[data-testid='vehicle-transmission']").TextContent);
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("i"));
    }

    [Fact]
    public void BadgesWrapAndLongValuesBreak()
    {
        var cut = RenderCard(Summary(fuelType: "A very long fuel type name", transmission: "A very long transmission name"));

        Assert.Contains("flex-wrap", cut.Find("[data-testid='vehicle-badges']").ClassList);
        Assert.All(cut.FindAll("span.badge"), badge => Assert.Contains("text-break", badge.ClassList));
    }

    [Theory]
    [InlineData(OwnershipType.Purchased, "Purchased 2022")]
    [InlineData(OwnershipType.Leased, "Leased 2022")]
    [InlineData(null, "Acquired 2022")]
    public void PurchasedYear_ShowsTheOwnershipWordAndYearWithADecorativeCalendarIcon(OwnershipType? ownership, string expected)
    {
        var cut = RenderCard(Summary(mileage: 5_000, ownership: ownership, purchasedYear: 2022));

        var line = cut.Find("[data-testid='vehicle-acquired']");
        Assert.Equal(expected, line.TextContent.Trim());
        Assert.Equal("true", line.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void NoPurchasedYear_OmitsTheAcquiredLineEvenWhenOwnershipIsKnown()
    {
        var cut = RenderCard(Summary(mileage: 5_000, ownership: OwnershipType.Leased));

        Assert.Empty(cut.FindAll("[data-testid='vehicle-acquired']"));
        Assert.DoesNotContain("Leased", cut.Markup);
    }

    [Fact]
    public void MileageAndAcquiredShareOneWrappingDetailLine()
    {
        var cut = RenderCard(Summary(mileage: 5_000, ownership: OwnershipType.Purchased, purchasedYear: 2022));

        var line = cut.Find("p.card-text");
        Assert.Contains("flex-wrap", line.ClassList);
        Assert.NotNull(line.QuerySelector("[data-testid='vehicle-mileage']"));
        Assert.NotNull(line.QuerySelector("[data-testid='vehicle-acquired']"));
        Assert.Equal("5,000 mi", line.QuerySelector("[data-testid='vehicle-mileage']")!.TextContent.Trim());
    }

    [Fact]
    public void MileageStillShowsWhenThereAreNoBadgesOrAcquiredYear()
    {
        var cut = RenderCard(Summary(mileage: 5_000));

        Assert.Equal("5,000 mi", cut.Find("[data-testid='vehicle-mileage']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='vehicle-acquired']"));
    }

    [Fact]
    public void AllDetails_RenderTogetherWithoutDuplicatingTheName()
    {
        var cut = RenderCard(Summary("Subaru", "Outback", 42_000, year: 2019, fuelType: "Gasoline", transmission: "Automatic", ownership: OwnershipType.Leased, purchasedYear: 2022));

        Assert.Equal("2019 Subaru Outback", cut.Find(".card-title a").TextContent.Trim());
        Assert.Equal("Gasoline", cut.Find("[data-testid='vehicle-fuel']").TextContent.Trim());
        Assert.Equal("Automatic", cut.Find("[data-testid='vehicle-transmission']").TextContent.Trim());
        Assert.Equal("42,000 mi", cut.Find("[data-testid='vehicle-mileage']").TextContent.Trim());
        Assert.Equal("Leased 2022", cut.Find("[data-testid='vehicle-acquired']").TextContent.Trim());
    }
}

using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCardTests : VehicleTestContext
{
    private IRenderedComponent<VehicleCard> RenderCard(VehicleSummaryDto vehicle) =>
        Render<VehicleCard>(parameters => parameters.Add(card => card.Vehicle, vehicle));

    [Fact]
    public void ShowsNicknameAsLinkMakeModelAndFormattedMileage()
    {
        var vehicle = Summary("Blue Streak", "Subaru", "Outback", 42_000);

        var cut = RenderCard(vehicle);

        var link = cut.Find("a.stretched-link, .card-title a");
        Assert.Equal("Blue Streak", link.TextContent.Trim());
        Assert.Equal($"/vehicles/{vehicle.Id}", link.GetAttribute("href"));
        Assert.Contains("Subaru Outback", cut.Markup);
        Assert.Contains("42,000 mi", cut.Markup);
    }

    [Fact]
    public void NoMileage_ShowsMileageNotSet() =>
        Assert.Contains("Mileage not set", RenderCard(Summary("Daily")).Markup);

    [Fact]
    public void NoMainImage_ShowsPlaceholderAndNoImageElement()
    {
        var cut = RenderCard(Summary("Daily"));

        Assert.Empty(cut.FindAll("img"));
        Assert.NotEmpty(cut.FindAll("[data-testid='vehicle-placeholder']"));
    }

    [Fact]
    public void MainImage_RendersLazyImageWithAltText()
    {
        var imageId = Guid.NewGuid();
        var vehicle = Summary("Daily", mainImageId: imageId);

        var image = RenderCard(vehicle).Find("img");

        Assert.Equal($"/vehicles/{vehicle.Id}/images/{imageId}", image.GetAttribute("src"));
        Assert.Equal("Daily", image.GetAttribute("alt"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
    }

    [Fact]
    public void UserText_IsEncoded()
    {
        var cut = RenderCard(Summary("<script>alert(1)</script>"));

        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("<script>alert(1)</script>", cut.Find(".card-title a").TextContent);
    }

    [Fact]
    public void DoesNotUseTableMarkup() => Assert.DoesNotContain("<table", RenderCard(Summary("Daily")).Markup);
}

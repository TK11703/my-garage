using Bunit;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCardTests : VehicleTestContext
{
    private IRenderedComponent<VehicleCard> RenderCard(VehicleSummaryDto vehicle) =>
        Render<VehicleCard>(parameters => parameters.Add(card => card.Vehicle, vehicle));

    private static VehicleNextDueDto Due(ReminderStatus status, int? miles = null, int? days = null, string name = "Oil change") =>
        new() { ReminderId = Guid.NewGuid(), Name = name, Status = status, MilesRemaining = miles, DaysRemaining = days };

    [Fact]
    public void ShowsTheComputedNameAsLinkMakeModelAndFormattedMileage()
    {
        var vehicle = Summary("Subaru", "Outback", 42_000, year: 2019);

        var cut = RenderCard(vehicle);

        var link = cut.Find(".card-title a");
        Assert.Equal("2019 Subaru Outback", link.TextContent.Trim());
        Assert.Equal($"/vehicles/{vehicle.Id}", link.GetAttribute("href"));
        Assert.Contains("42,000 mi", cut.Markup);
    }

    [Fact]
    public void NoMileage_ShowsMileageNotSet() =>
        Assert.Contains("Mileage not set", RenderCard(Summary()).Markup);

    [Fact]
    public void NoMainImage_ShowsATypographicPlaceholderAndNoImageElement()
    {
        var cut = RenderCard(Summary(model: "Outback", year: 2019));

        Assert.Empty(cut.FindAll("img"));
        var placeholder = cut.Find("[data-testid='vehicle-placeholder']");
        Assert.Empty(placeholder.QuerySelectorAll("svg"));
        Assert.Equal("2019", placeholder.QuerySelector(".mg-vehicle-placeholder-year")!.TextContent.Trim());
        Assert.Equal("Outback", placeholder.QuerySelector(".mg-vehicle-placeholder-model")!.TextContent.Trim());
        Assert.Equal("img", placeholder.GetAttribute("role"));
        Assert.StartsWith("No picture for", placeholder.GetAttribute("aria-label"));
    }

    [Fact]
    public void PlaceholderWithoutAYear_ShowsOnlyTheModel()
    {
        var cut = RenderCard(Summary(model: "Outback", year: null));

        Assert.Empty(cut.FindAll(".mg-vehicle-placeholder-year"));
        Assert.Equal("Outback", cut.Find(".mg-vehicle-placeholder-model").TextContent.Trim());
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
    public void IsAnImageForwardCardWithTheNameOnAWidescreenPicture()
    {
        var cut = RenderCard(Summary(mainImageId: Guid.NewGuid()));

        Assert.Contains("mg-vehicle-card", cut.Find(".card").ClassList);
        var top = cut.Find(".mg-vehicle-top");
        Assert.Contains("card-img-top", top.QuerySelector("img")!.ClassList);
        Assert.NotNull(top.QuerySelector(".mg-vehicle-plate .card-title a"));
    }

    [Fact]
    public void OdometerIsAReadoutWithItsOwnLabel()
    {
        var cut = RenderCard(Summary(mileage: 5_000));

        var readout = cut.Find("[data-testid='vehicle-mileage']");
        Assert.Equal("5,000 mi", readout.TextContent.Trim());
        Assert.Equal("Odometer", readout.ParentElement!.QuerySelector("dt")!.TextContent.Trim());
    }

    [Fact]
    public void LongTextWrapsInsteadOfBreakingTheGrid()
    {
        var cut = RenderCard(Summary("A very long make name", "A very long model name"));

        Assert.Contains("text-break", cut.Find(".card-title a").ClassList);
    }

    [Fact]
    public void FuelAndTransmission_RenderAsBadges()
    {
        var cut = RenderCard(Summary(fuelType: "Gasoline", transmission: "Automatic"));

        var badges = cut.Find("[data-testid='vehicle-badges']");
        Assert.Equal("Gasoline", badges.QuerySelector("[data-testid='vehicle-fuel']")!.TextContent.Trim());
        Assert.Equal("Automatic", badges.QuerySelector("[data-testid='vehicle-transmission']")!.TextContent.Trim());
        Assert.Equal(2, badges.QuerySelectorAll("span.badge").Length);
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

        Assert.Contains("mg-vehicle-tags", cut.Find("[data-testid='vehicle-badges']").ClassList);
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
    public void BadgesAndAcquiredShareOneWrappingLine()
    {
        var cut = RenderCard(Summary(mileage: 5_000, fuelType: "Gasoline", ownership: OwnershipType.Purchased, purchasedYear: 2022));

        var line = cut.Find("p.mg-vehicle-tags");
        Assert.NotNull(line.QuerySelector("[data-testid='vehicle-fuel']"));
        Assert.Equal("Purchased 2022", line.QuerySelector("[data-testid='vehicle-acquired']")!.TextContent.Trim());
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

    [Theory]
    [InlineData(ReminderStatus.Overdue, "Overdue", "mg-status-bad")]
    [InlineData(ReminderStatus.DueSoon, "Due soon", "mg-status-due")]
    public void AReminderThatNeedsAttention_ShowsAStatusPill(ReminderStatus status, string label, string cssClass)
    {
        var cut = RenderCard(Summary(nextDue: Due(status, miles: 100)));

        var pill = cut.Find("[data-testid='vehicle-status']");
        Assert.Equal(label, pill.TextContent.Trim());
        Assert.Contains(cssClass, pill.ClassList);
    }

    [Theory]
    [InlineData(ReminderStatus.Upcoming)]
    [InlineData(ReminderStatus.MileageNeeded)]
    public void AQuietReminder_ShowsNoPill(ReminderStatus status) =>
        Assert.Empty(RenderCard(Summary(nextDue: Due(status, miles: 4_000))).FindAll("[data-testid='vehicle-status']"));

    [Fact]
    public void NoReminder_ShowsNoPillAndOffersToSetOne()
    {
        var cut = RenderCard(Summary());

        Assert.Empty(cut.FindAll("[data-testid='vehicle-status']"));
        Assert.Equal("No reminder set", cut.Find("[data-testid='vehicle-next-due'] dd").TextContent.Trim());
    }

    [Theory]
    [InlineData(3_780, null, "3,780 mi left")]
    [InlineData(-120, null, "120 mi overdue")]
    [InlineData(0, null, "due now")]
    [InlineData(null, 9, "in 9 days")]
    [InlineData(null, 1, "in 1 day")]
    [InlineData(null, -3, "3 days overdue")]
    [InlineData(null, 0, "due today")]
    public void NextDue_NamesTheReminderAndTheRemainingDistanceOrTime(int? miles, int? days, string expected)
    {
        var status = miles <= 0 || days <= 0 ? ReminderStatus.Overdue : ReminderStatus.Upcoming;

        var cut = RenderCard(Summary(nextDue: Due(status, miles, days, "Inspection")));

        var area = cut.Find("[data-testid='vehicle-next-due']");
        Assert.Equal("Inspection", area.QuerySelector("dt")!.TextContent.Trim());
        Assert.Equal(expected, area.QuerySelector("dd")!.TextContent.Trim());
    }

    [Fact]
    public void NextDue_WithBothDistanceAndTime_ShowsTheOneThatIsOverdue()
    {
        var cut = RenderCard(Summary(nextDue: Due(ReminderStatus.Overdue, miles: 3_000, days: -3)));

        Assert.Equal("3 days overdue", cut.Find("[data-testid='vehicle-next-due'] dd").TextContent.Trim());
    }

    [Fact]
    public void NextDue_ForAMileageReminderWithNoOdometer_SaysItNeedsAMileage()
    {
        var cut = RenderCard(Summary(nextDue: Due(ReminderStatus.MileageNeeded)));

        Assert.Equal("Needs a mileage", cut.Find("[data-testid='vehicle-next-due'] dd").TextContent.Trim());
        Assert.Contains("mg-readout-needed", cut.Find("[data-testid='vehicle-next-due']").ClassList);
    }

    [Theory]
    [InlineData(null, "$0.00")]
    [InlineData(345.92, "$345.92")]
    [InlineData(1234.5, "$1,234.50")]
    public void SpendThisYear_IsShownAsMoneyUnderTheYear(double? amount, string expected)
    {
        var cut = RenderCard(Summary(spendThisYear: amount is { } value ? (decimal)value : null));

        Assert.Equal(expected, cut.Find("[data-testid='vehicle-spend']").TextContent.Trim());
        Assert.Equal($"Spent {DateTime.UtcNow.Year}", cut.Find("[data-testid='vehicle-spend']").ParentElement!.QuerySelector("dt")!.TextContent.Trim());
    }

    [Fact]
    public void AnArchivedVehicle_ShowsItsDateAndNoStatusPillOrNextDue()
    {
        var archived = Summary(nextDue: Due(ReminderStatus.Overdue, miles: -5));
        var cut = RenderCard(new VehicleSummaryDto
        {
            Id = archived.Id,
            Make = archived.Make,
            Model = archived.Model,
            Year = archived.Year,
            NextDue = archived.NextDue,
            ArchivedAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
        });

        Assert.Equal("Archived Nov 8, 2026", cut.Find("[data-testid='vehicle-archived']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='vehicle-status']"));
        Assert.Empty(cut.FindAll("[data-testid='vehicle-next-due']"));
        Assert.Contains("mg-vehicle-card-archived", cut.Find(".mg-vehicle-card").ClassList);
    }
}

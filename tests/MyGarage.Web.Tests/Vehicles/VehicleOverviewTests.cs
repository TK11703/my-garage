using System.Net;
using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Reminders;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleOverviewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private List<ReminderDto> reminders = [];
    private HttpStatusCode? remindersFailure;
    private ServiceRecordDto? latestRecord;
    private IReadOnlyList<FuelEnergySpendMonthDto>? spendMonths;
    private HttpStatusCode? spendFailure;
    private List<SuggestedReminderDto> suggestions = [];
    private HttpStatusCode? suggestionsFailure;

    private static ReminderDto Reminder(Guid vehicleId, string name, ReminderStatus status, int? miles = null, int? days = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            VehicleName = "2020 Ford F-150",
            Name = name,
            LastDoneOn = new DateOnly(2026, 1, 1),
            IsPaused = status == ReminderStatus.Paused,
            Status = status,
            MilesRemaining = miles,
            DaysRemaining = days,
        };

    private IRenderedComponent<VehicleOverview> RenderOverview(VehicleDetailDto vehicle, bool archived = false)
    {
        RegisterApi(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/reminder-suggestions", StringComparison.Ordinal))
            {
                return suggestionsFailure is { } suggestionStatus ? new HttpResponseMessage(suggestionStatus) : Json(suggestions);
            }

            if (path.EndsWith("/reminders", StringComparison.Ordinal))
            {
                return remindersFailure is { } status ? new HttpResponseMessage(status) : Json(reminders);
            }

            if (path.EndsWith("/fuel-energy-entries/spend", StringComparison.Ordinal))
            {
                return spendFailure is { } spendStatus ? new HttpResponseMessage(spendStatus)
                    : spendMonths is null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(spendMonths);
            }

            return latestRecord is not null && path.EndsWith($"/service-records/{latestRecord.Id}", StringComparison.Ordinal)
                ? Json(latestRecord)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        return Render<VehicleOverview>(parameters => parameters.Add(page => page.Vehicle, vehicle).Add(page => page.Archived, archived));
    }

    private VehicleDetailDto Vehicle(
        int? currentMileage = 45_000,
        DateOnly? lastServiceOn = null,
        decimal? spend = null,
        bool withImage = false,
        string? engine = null) =>
        new()
        {
            Id = vehicleId,
            Make = "Ford",
            Model = "F-150",
            Year = 2020,
            Engine = engine,
            CurrentMileage = currentMileage,
            LastServiceOn = lastServiceOn,
            SpendThisYear = spend,
            Tags = [],
            Images = withImage ? [new VehicleImageDto { Id = Guid.NewGuid(), IsMain = true, ContentType = "image/jpeg", SizeBytes = 1, CreatedAt = DateTimeOffset.UtcNow }] : [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void ShowsTheCurrentOdometerLastServiceAndSpend()
    {
        var cut = RenderOverview(Vehicle(45_000, new DateOnly(2026, 3, 1), 345.92m));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("45,000 mi", cut.Find("[data-testid='overview-odometer']").TextContent.Trim());
            Assert.Equal("Mar 1, 2026", cut.Find("[data-testid='overview-last-service']").TextContent.Trim());
            Assert.Equal("$345.92", cut.Find("[data-testid='overview-spend']").TextContent.Trim());
        });
    }

    [Fact]
    public void WithNothingRecorded_ShowsHonestEmptyValues()
    {
        var cut = RenderOverview(Vehicle(currentMileage: null));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Mileage not set", cut.Find("[data-testid='overview-odometer']").TextContent.Trim());
            Assert.Equal("No service yet", cut.Find("[data-testid='overview-last-service']").TextContent.Trim());
            Assert.Equal("$0.00", cut.Find("[data-testid='overview-spend']").TextContent.Trim());
        });
    }

    [Fact]
    public void TheMainPictureIsShown()
    {
        Assert.NotNull(RenderOverview(Vehicle(withImage: true)).Find("img[data-testid='overview-photo']"));
    }

    [Fact]
    public void WithoutAPicture_ShowsATypographicPlaceholder()
    {
        var placeholder = RenderOverview(Vehicle()).Find("[data-testid='overview-placeholder']");

        Assert.Equal("2020", placeholder.QuerySelector(".mg-vehicle-placeholder-year")!.TextContent.Trim());
        Assert.Equal("F-150", placeholder.QuerySelector(".mg-vehicle-placeholder-model")!.TextContent.Trim());
    }

    [Fact]
    public void TheSpecPlate_ListsOnlyTheSpecsThatAreSet()
    {
        var cut = RenderOverview(Vehicle(engine: "5.0L V8"));

        var plate = cut.Find("[data-testid='spec-plate']");
        Assert.Equal(["Engine"], plate.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
        Assert.Equal("5.0L V8", plate.QuerySelector("dd")!.TextContent.Trim());
    }

    [Fact]
    public void NextDue_IsTheMostUrgentActiveReminder_AndNeedsAttentionListsTheOnesThatAreDue()
    {
        reminders =
        [
            Reminder(vehicleId, "Registration", ReminderStatus.Upcoming, days: 200),
            Reminder(vehicleId, "Inspection", ReminderStatus.Overdue, days: -3),
            Reminder(vehicleId, "Oil change", ReminderStatus.DueSoon, miles: 200),
            Reminder(vehicleId, "Tires", ReminderStatus.Paused),
        ];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            var next = cut.Find("[data-testid='overview-next-due']");
            Assert.Equal("Inspection", next.QuerySelector("dt")!.TextContent.Trim());
            Assert.Equal("3 days overdue", next.QuerySelector("dd")!.TextContent.Trim());
            Assert.Contains("mg-readout-bad", next.ClassList);
            Assert.Equal(["Inspection", "Oil change"], cut.FindAll("[data-testid='attention-item'] a").Select(link => link.TextContent.Trim()));
            Assert.StartsWith($"/vehicles/{vehicleId}/reminders/", cut.Find("[data-testid='attention-item'] a").GetAttribute("href"));
        });
    }

    [Fact]
    public void TheDial_ShowsHowMuchOfTheMostUrgentIntervalIsUsed()
    {
        reminders =
        [
            new ReminderDto
            {
                Id = Guid.NewGuid(),
                VehicleId = vehicleId,
                VehicleName = "2020 Ford F-150",
                Name = "Oil change",
                IntervalMiles = 5_000,
                LastDoneOn = new DateOnly(2026, 1, 1),
                IsPaused = false,
                Status = ReminderStatus.DueSoon,
                MilesRemaining = 1_000,
            },
        ];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            var dial = cut.Find("[data-testid='gauge-dial']");
            Assert.Equal("80%", dial.QuerySelector(".mg-dial-pct")!.TextContent.Trim());
            Assert.Equal("80% of the Oil change interval is used", dial.GetAttribute("aria-label"));
            Assert.Equal("50.2", dial.QuerySelector(".mg-dial-arc")!.GetAttribute("stroke-dashoffset"));
            Assert.Contains("mg-dial-due", dial.QuerySelector(".mg-dial-arc")!.ClassList);
            Assert.Equal("Oil change", cut.Find("[data-testid='gauge-name']").TextContent.Trim());
        });
    }

    [Fact]
    public void WithoutAnIntervalToMeasure_TheDialIsLeftOut()
    {
        reminders = [Reminder(vehicleId, "Registration", ReminderStatus.Upcoming, days: 200)];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='overview-next-due']")));
        Assert.Empty(cut.FindAll("[data-testid='gauge-dial']"));
    }

    private static ServiceLineItemDto Line(string description, decimal total) =>
        new() { Id = Guid.NewGuid(), Description = description, FlatCost = total, Parts = [], TotalCost = total };

    private void GivenALatestRecord(params ServiceLineItemDto[] lines)
    {
        var summary = RecordSummary(new DateOnly(2026, 10, 30), 90_100, "Allen Ng", lines.Length);
        latestRecord = RecordDetail(vehicleId, summary.Id, new DateOnly(2026, 10, 30), 90_100, "Allen Ng", lines, tax: 8.25m);
        ServiceRecords = ServiceRecordsPage([summary]);
    }

    [Fact]
    public void TheLatestServiceRecord_IsShownAsAReceipt()
    {
        GivenALatestRecord(Line("Synthetic oil change", 100m), Line("Coupon", -4.04m), Line("Labor", 180m));

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            var receipt = cut.Find("[data-testid='overview-receipt']");
            Assert.Contains("Oct 30, 2026", receipt.QuerySelector("h2")!.TextContent);
            Assert.Contains("Allen Ng", receipt.TextContent);
            Assert.Contains("90,100 mi", receipt.TextContent);
            Assert.Equal(["Synthetic oil change", "Coupon", "Labor"], receipt.QuerySelectorAll("[data-testid='receipt-line'] .mg-rc-name").Select(name => name.TextContent.Trim()));
            Assert.Contains("mg-rc-credit", receipt.QuerySelectorAll("[data-testid='receipt-line']")[1].ClassList);
            Assert.Equal("$8.25", receipt.QuerySelector("[data-testid='receipt-tax']")!.TextContent.Trim());
            Assert.Equal("$284.21", receipt.QuerySelector("[data-testid='receipt-total']")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{latestRecord!.Id}", receipt.QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ALongReceipt_ShowsTheFirstLinesAndHowManyAreLeftOut()
    {
        GivenALatestRecord(Line("One", 1m), Line("Two", 2m), Line("Three", 3m), Line("Four", 4m), Line("Five", 5m), Line("Six", 6m));

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(4, cut.FindAll("[data-testid='receipt-line']").Count);
            Assert.Equal("+ 2 more line items", cut.Find("[data-testid='receipt-more']").TextContent.Trim());
        });
    }

    [Fact]
    public void WithoutServiceRecords_NoReceiptIsShown()
    {
        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='overview-next-due']")));
        Assert.Empty(cut.FindAll("[data-testid='overview-receipt']"));
    }

    [Fact]
    public void WhenTheServiceRecordsFail_TheRestOfTheOverviewStillShows()
    {
        ServiceRecordsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderOverview(Vehicle(45_000));

        cut.WaitForAssertion(() => Assert.Equal("45,000 mi", cut.Find("[data-testid='overview-odometer']").TextContent.Trim()));
        Assert.Empty(cut.FindAll("[data-testid='overview-receipt']"));
    }

    private static IReadOnlyList<FuelEnergySpendMonthDto> Months(decimal lastFuel = 0m, decimal lastCharging = 0m, decimal firstFuel = 0m) =>
    [
        .. Enumerable.Range(0, 12).Select(offset => new FuelEnergySpendMonthDto
        {
            Month = new DateOnly(2025, 12, 1).AddMonths(offset),
            Fuel = offset == 0 ? firstFuel : offset == 11 ? lastFuel : 0m,
            Charging = offset == 11 ? lastCharging : 0m,
        }),
    ];

    [Fact]
    public void FuelAndChargingSpend_IsTwelveBarsWithTheTotalAndTheLatestMonthMarked()
    {
        spendMonths = Months(lastFuel: 50.25m, lastCharging: 11.46m, firstFuel: 20m);

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[data-testid='overview-spend-panel']");
            Assert.Equal("$81.71", panel.QuerySelector("[data-testid='spend-total']")!.TextContent.Trim());
            var bars = panel.QuerySelectorAll("[data-testid='spend-bar']");
            Assert.Equal(12, bars.Length);
            Assert.Contains("mg-spark-hi", bars[11].ClassList);
            Assert.DoesNotContain(bars.Take(11), bar => bar.ClassList.Contains("mg-spark-hi"));
            Assert.Equal("DEC 2025: $20.00", bars[0].QuerySelector("title")!.TextContent.Trim());
            Assert.Equal("40", bars[11].GetAttribute("height"));
            Assert.Equal("DEC", panel.QuerySelector(".mg-spark-axis span")!.TextContent.Trim());
            Assert.Contains("highest is $61.71 in NOV 2026", panel.QuerySelector("svg")!.GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void WithNoFuelOrChargingInTheLastYear_SaysSoInsteadOfDrawingBars()
    {
        spendMonths = Months();

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.Equal("$0.00", cut.Find("[data-testid='spend-total']").TextContent.Trim()));
        Assert.NotNull(cut.Find("[data-testid='spend-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='spend-bar']"));
    }

    [Fact]
    public void WhenTheSpendFails_TheRestOfTheOverviewStillShows()
    {
        spendFailure = HttpStatusCode.InternalServerError;

        var cut = RenderOverview(Vehicle(45_000));

        cut.WaitForAssertion(() => Assert.Equal("45,000 mi", cut.Find("[data-testid='overview-odometer']").TextContent.Trim()));
        Assert.Empty(cut.FindAll("[data-testid='overview-spend-panel']"));
    }

    [Fact]
    public void NeedsAttention_CountsTheItemsThatAreDue()
    {
        reminders =
        [
            Reminder(vehicleId, "Inspection", ReminderStatus.Overdue, days: -3),
            Reminder(vehicleId, "Oil change", ReminderStatus.DueSoon, miles: 200),
        ];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("[data-testid='attention-count']").TextContent.Trim()));
    }

    [Fact]
    public void WithoutReminders_OffersToSetOne()
    {
        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("No reminder set", cut.Find("[data-testid='overview-next-due'] dd").TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/reminders/new", cut.Find("[data-testid='nothing-due'] a").GetAttribute("href"));
            var button = cut.Find("[data-testid='nothing-due'] a.btn");
            Assert.Equal("Set reminder", button.TextContent.Trim());
        });
    }

    [Fact]
    public void WhenNothingIsDueSoon_SaysSo()
    {
        reminders = [Reminder(vehicleId, "Registration", ReminderStatus.Upcoming, days: 200)];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.Equal("Nothing is due soon.", cut.Find("[data-testid='nothing-due']").TextContent.Trim()));
    }

    [Fact]
    public void WhenTheRemindersFail_TheRestOfTheOverviewStillShows()
    {
        remindersFailure = HttpStatusCode.InternalServerError;

        var cut = RenderOverview(Vehicle(45_000));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("45,000 mi", cut.Find("[data-testid='overview-odometer']").TextContent.Trim());
            Assert.Contains("Reminders are not available", cut.Find("[data-testid='needs-attention']").TextContent);
        });
    }

    [Fact]
    public void AnArchivedVehicle_IsMarked_AndHasItsRemindersOff_WithoutAskingTheApi()
    {
        reminders = [Reminder(vehicleId, "Inspection", ReminderStatus.Overdue, days: -3)];

        var cut = RenderOverview(Vehicle(), archived: true);

        Assert.Equal("Archived", cut.Find("[data-testid='overview-archived']").TextContent.Trim());
        Assert.Equal("Reminders are off", cut.Find("[data-testid='overview-next-due'] dd").TextContent.Trim());
        Assert.Contains("reminders are off", cut.Find("[data-testid='nothing-due']").TextContent);
        Assert.DoesNotContain(Requests, request => request.RequestUri!.AbsolutePath.EndsWith("/reminders", StringComparison.Ordinal));
    }

    [Fact]
    public void SuggestedReminders_OfferASetReminderButtonThatOpensTheFormWithTheSuggestion()
    {
        var suggestion = new SuggestedReminderDto
        {
            Name = "Oil change",
            IntervalMiles = 5000,
            IntervalMonths = 6,
            LastDoneOn = new DateOnly(2026, 10, 30),
            LastDoneMileage = 90_100,
            Basis = "Suggested from your Oct 30 service",
        };
        suggestions = [suggestion];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid='overview-suggestion']"));
            Assert.Equal("Oil change", row.QuerySelector("[data-testid='suggestion-name']")!.TextContent.Trim());
            Assert.Contains("Suggested from your Oct 30 service", row.TextContent);
            var button = row.QuerySelector("a.btn[data-testid='set-reminder']")!;
            Assert.Equal("Set reminder", button.TextContent.Trim());
            Assert.Equal(ReminderLinks.New(vehicleId, suggestion), button.GetAttribute("href"));
            Assert.Equal("Set up a reminder for Oil change", button.GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void SuggestedReminders_ReplaceTheGeneralSetReminderButton()
    {
        suggestions = [new SuggestedReminderDto { Name = "Tires", LastDoneOn = new DateOnly(2026, 1, 1), Basis = "From your service history" }];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='overview-suggestion']")));
        Assert.Empty(cut.FindAll("[data-testid='nothing-due'] a"));
        Assert.Single(cut.FindAll("a[data-testid='set-reminder']"));
    }

    [Fact]
    public void SuggestedReminders_AreNotCountedAsNeedingAttention()
    {
        suggestions = [new SuggestedReminderDto { Name = "Tires", LastDoneOn = new DateOnly(2026, 1, 1), Basis = "From your service history" }];

        var cut = RenderOverview(Vehicle());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='overview-suggestion']")));
        Assert.Empty(cut.FindAll("[data-testid='attention-count']"));
        Assert.Empty(cut.FindAll("[data-testid='attention-item']"));
    }

    [Fact]
    public void WhenTheSuggestionsFail_TheRestOfTheOverviewStillShows()
    {
        suggestionsFailure = HttpStatusCode.InternalServerError;

        var cut = RenderOverview(Vehicle(45_000));

        cut.WaitForAssertion(() => Assert.Equal("45,000 mi", cut.Find("[data-testid='overview-odometer']").TextContent.Trim()));
        Assert.Empty(cut.FindAll("[data-testid='overview-suggestion']"));
    }

    [Fact]
    public void AnArchivedVehicle_IsNotOfferedSuggestions()
    {
        suggestions = [new SuggestedReminderDto { Name = "Tires", LastDoneOn = new DateOnly(2026, 1, 1), Basis = "From your service history" }];

        var cut = RenderOverview(Vehicle(), archived: true);

        Assert.Empty(cut.FindAll("[data-testid='overview-suggestion']"));
        Assert.DoesNotContain(Requests, request => request.RequestUri!.AbsolutePath.EndsWith("/reminder-suggestions", StringComparison.Ordinal));
    }

    [Fact]
    public void UserText_IsEncoded()
    {
        var cut = RenderOverview(Vehicle(engine: "<b>big</b>"));

        Assert.Empty(cut.FindAll("[data-testid='spec-plate'] b"));
        Assert.Equal("<b>big</b>", cut.Find("[data-testid='spec-plate'] dd").TextContent);
    }
}

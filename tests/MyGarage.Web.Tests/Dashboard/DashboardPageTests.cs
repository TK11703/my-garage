using System.Net;
using Bunit;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Dashboard;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Dashboard;

public class DashboardPageTests : VehicleTestContext
{
    private DashboardDto dashboard = Dashboard();

    private static DashboardDto Dashboard(
        int vehicles = 3,
        int serviceRecords = 12,
        int fuel = 7,
        int drafts = 2,
        int overdue = 1,
        int dueSoon = 2,
        FuelEnergySpendMonthDto[]? spend = null,
        RecentActivityDto[]? activity = null,
        params ReminderDto[] attention) =>
        new()
        {
            Vehicles = vehicles,
            ServiceRecords = serviceRecords,
            FuelEnergyEntries = fuel,
            DraftsAwaitingReview = drafts,
            RemindersOverdue = overdue,
            RemindersDueSoon = dueSoon,
            Attention = attention,
            FuelEnergySpend = spend ?? [],
            RecentActivity = activity ?? [],
        };

    private static RecentActivityDto Entry(string vehicle = "2020 Ford F-150", string? category = null, string excerpt = "Replaced the wipers", Guid? image = null, DateTimeOffset? at = null, Guid? vehicleId = null) =>
        new()
        {
            NoteId = Guid.NewGuid(), VehicleId = vehicleId ?? Guid.NewGuid(), VehicleName = vehicle, VehicleMainImageId = image, Category = category,
            Excerpt = excerpt, CreatedAt = at ?? DateTimeOffset.UtcNow.AddMinutes(-12),
        };

    private static FuelEnergySpendMonthDto Month(int year, int month, decimal fuel, decimal charging) =>
        new() { Month = new DateOnly(year, month, 1), Fuel = fuel, Charging = charging };

    private IRenderedComponent<DashboardPage> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? (request => request.RequestUri!.AbsolutePath == "/api/dashboard" ? Json(dashboard) : new HttpResponseMessage(HttpStatusCode.NotFound)));
        return Render<DashboardPage>();
    }

    private static string Card(IRenderedComponent<DashboardPage> cut, string id) =>
        cut.Find($"[data-testid='metric-{id}']").TextContent;

    [Fact]
    public void ShowsTheFourFiguresAsCompactCards_WithALabelAndANumber()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Dashboard", cut.Find("h1").TextContent);
            Assert.Equal(4, cut.FindAll("[aria-label='Garage figures'] > li").Count);
            foreach (var (id, label, value) in new[]
            {
                ("vehicles", "Vehicles", "3"), ("service-records", "Service records", "12"), ("fuel-energy-entries", "Fuel and charging entries", "7"),
                ("drafts", "Drafts awaiting review", "2"),
            })
            {
                Assert.Contains(label, Card(cut, id));
                Assert.Equal(value, cut.Find($"[data-testid='metric-{id}'] [data-testid='metric-value']").TextContent.Trim());
            }

            Assert.Contains("row-cols-lg-4", cut.Find("[aria-label='Garage figures']").ClassName);
        });
    }

    [Fact]
    public void TheOverdueAndDueSoonCardsAreGone_BecauseNeedsAttentionCoversThem()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='metric-overdue']"));
            Assert.Empty(cut.FindAll("[data-testid='metric-due-soon']"));
            Assert.DoesNotContain("Reminders overdue", cut.Markup);
            Assert.DoesNotContain("Reminders due soon", cut.Markup);
        });
    }

    [Fact]
    public void TheLinkedCardsGoToTheirPages_AndTheOthersHaveNoLink()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/vehicles", cut.Find("[data-testid='metric-vehicles'] a").GetAttribute("href"));
            Assert.Equal("Vehicles", cut.Find("[data-testid='metric-vehicles'] a").TextContent.Trim());
            Assert.Equal("/drafts", cut.Find("[data-testid='metric-drafts'] a").GetAttribute("href"));
            Assert.Empty(cut.FindAll("[data-testid='metric-service-records'] a"));
            Assert.Empty(cut.FindAll("[data-testid='metric-fuel-energy-entries'] a"));
        });
    }

    [Fact]
    public void TheNumbersAreColored_ButTheDraftsNumberOnlyWhenThereAreDrafts()
    {
        var cut = RenderPage();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("text-primary", cut.Find("[data-testid='metric-vehicles'] [data-testid='metric-value']").ClassName);
            Assert.Contains("text-success", cut.Find("[data-testid='metric-service-records'] [data-testid='metric-value']").ClassName);
            Assert.Contains("text-warning-emphasis", cut.Find("[data-testid='metric-fuel-energy-entries'] [data-testid='metric-value']").ClassName);
            Assert.Contains("text-danger", cut.Find("[data-testid='metric-drafts'] [data-testid='metric-value']").ClassName);
        });
    }

    [Fact]
    public void ADraftsCountOfZero_StaysPlain()
    {
        dashboard = Dashboard(drafts: 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.DoesNotContain("text-danger", cut.Find("[data-testid='metric-drafts'] [data-testid='metric-value']").ClassName));
    }

    [Fact]
    public void WithNoFuelOrChargingCosts_TheSpendSectionSaysSoInsteadOfDrawingAnEmptyChart()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No fuel or charging costs", cut.Find("[data-testid='spend']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='spend-chart']"));
        });
    }

    [Fact]
    public void WithFuelAndChargingCosts_TheSpendSectionDrawsTheChart()
    {
        dashboard = Dashboard(spend: [Month(2026, 9, 40m, 0m), Month(2026, 10, 25m, 12m)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Fuel and charging spend", cut.Find("[data-testid='spend'] h2").TextContent);
            Assert.Contains("card", cut.Find("[data-testid='spend']").ClassName);
            Assert.NotNull(cut.Find("[data-testid='spend'] .card-body [data-testid='spend-chart']"));
            Assert.NotNull(cut.Find("[data-testid='spend-chart'] svg[role='img']"));
        });
    }

    [Fact]
    public void AnEmptyGarage_HasNoSpendSection()
    {
        dashboard = Dashboard(0, 0, 0, 0, 0, 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='spend']")));
    }

    [Fact]
    public void NeedsAttention_ListsEachReminderWithItsVehicleStatusRemainingAndALink()
    {
        var vehicleId = Guid.NewGuid();
        var overdue = Reminder(vehicleId, name: "Inspection", status: ReminderStatus.Overdue, milesRemaining: null, daysRemaining: -3, vehicleName: "2020 Ford F-150");
        var soon = Reminder(vehicleId, name: "Oil change", status: ReminderStatus.DueSoon, milesRemaining: 200, vehicleName: "2020 Ford F-150");
        dashboard = Dashboard(attention: [overdue, soon]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='attention']");
            var rows = section.QuerySelectorAll("[data-testid='reminder']");
            Assert.Equal(2, rows.Length);
            Assert.Equal("Inspection", rows[0].QuerySelector("[data-testid='reminder-link']")!.TextContent.Trim());
            Assert.Equal("Overdue", rows[0].QuerySelector("[data-testid='reminder-status']")!.TextContent.Trim());
            Assert.Equal("3 days overdue", rows[0].QuerySelector("[data-testid='reminder-remaining']")!.TextContent.Trim());
            Assert.Equal("2020 Ford F-150", rows[0].QuerySelector("[data-testid='reminder-vehicle']")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}/reminders/{overdue.Id}", rows[0].QuerySelector("[data-testid='reminder-link']")!.GetAttribute("href"));
            Assert.Equal("200 miles left", rows[1].QuerySelector("[data-testid='reminder-remaining']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void NeedsAttention_IsACardWithATitleASubtitleAndALinkToAllReminders()
    {
        dashboard = Dashboard(attention: [Reminder(status: ReminderStatus.Overdue)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid='attention']");
            Assert.Contains("card", card.ClassName);
            Assert.Equal("Needs attention", card.QuerySelector("h2")!.TextContent);
            Assert.Contains("overdue or due soon", card.QuerySelector(".card-body > div p")!.TextContent);
            Assert.Equal("/reminders", cut.Find("[data-testid='all-reminders']").GetAttribute("href"));
            Assert.Contains("list-group-flush", card.QuerySelector("ul")!.ClassName);
        });
    }

    [Fact]
    public void EachAttentionRowLeadsWithATileOfTheNextDueDate_ColoredByStatus()
    {
        var overdue = Reminder(name: "Inspection", status: ReminderStatus.Overdue, nextDueDate: new DateOnly(2026, 11, 2), daysRemaining: -4);
        var soon = Reminder(name: "Registration", status: ReminderStatus.DueSoon, nextDueDate: new DateOnly(2026, 11, 20), daysRemaining: 14);
        dashboard = Dashboard(attention: [overdue, soon]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var tiles = cut.FindAll("[data-testid='attention'] [data-testid='due-tile']");
            Assert.Equal(2, tiles.Count);
            Assert.Contains("Nov", tiles[0].TextContent);
            Assert.Contains("02", tiles[0].TextContent);
            Assert.Contains("Next due Nov 2, 2026", tiles[0].TextContent);
            Assert.Contains("bg-danger-subtle", tiles[0].ClassName);
            Assert.Contains("20", tiles[1].TextContent);
            Assert.Contains("bg-warning-subtle", tiles[1].ClassName);
        });
    }

    [Fact]
    public void ARowThatIsDueByMileageOnly_ShowsAMileageIconInsteadOfADate()
    {
        dashboard = Dashboard(attention: [Reminder(status: ReminderStatus.DueSoon, nextDueDate: null, milesRemaining: 300)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var tile = cut.Find("[data-testid='attention'] [data-testid='due-tile']");
            Assert.NotNull(tile.QuerySelector("svg"));
            Assert.Contains("Due by mileage", tile.TextContent);
        });
    }

    [Fact]
    public void RecentUpdates_SitsBesideTheSpendCard_EachInSixColumns()
    {
        dashboard = Dashboard(spend: [Month(2026, 10, 25m, 12m)]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("[data-testid='spend']").ParentElement!.ParentElement!;
            Assert.Contains("row", row.ClassName);
            Assert.Equal(2, row.Children.Length);
            Assert.All(row.Children, column => Assert.Contains("col-lg-6", column.ClassName));
            Assert.NotNull(row.Children[1].QuerySelector("[data-testid='recent-updates']"));
            Assert.Contains("card", cut.Find("[data-testid='recent-updates']").ClassName);
        });
    }

    [Fact]
    public void RecentActivity_ListsEachJournalEntryWithItsVehicleKindExcerptAndAge()
    {
        var vehicleId = Guid.NewGuid();
        dashboard = Dashboard(activity:
        [
            Entry("2020 Ford F-150", "Maintenance", "Changed the cabin air filter", vehicleId: vehicleId, at: DateTimeOffset.UtcNow.AddMinutes(-12)),
            Entry("2015 Jeep Grand Cherokee", null, "Washed and waxed", at: DateTimeOffset.UtcNow.AddHours(-3)),
        ]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("[data-testid='recent-updates']");
            Assert.Equal("Recent updates", section.QuerySelector("h2")!.TextContent);
            Assert.Equal("Recent activity", section.QuerySelector("h3")!.TextContent);
            var rows = section.QuerySelectorAll("[data-testid='activity']");
            Assert.Equal(2, rows.Length);
            Assert.Equal("2020 Ford F-150", rows[0].QuerySelector("[data-testid='activity-vehicle']")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}?tab=description", rows[0].QuerySelector("[data-testid='activity-vehicle']")!.GetAttribute("href"));
            Assert.Equal("added a Maintenance note", rows[0].QuerySelector("[data-testid='activity-kind']")!.TextContent.Trim());
            Assert.Equal("Changed the cabin air filter", rows[0].QuerySelector("[data-testid='activity-excerpt']")!.TextContent.Trim());
            Assert.Equal("12 minutes ago", rows[0].QuerySelector("[data-testid='activity-time']")!.TextContent.Trim());
            Assert.Equal("added a note", rows[1].QuerySelector("[data-testid='activity-kind']")!.TextContent.Trim());
            Assert.Equal("3 hours ago", rows[1].QuerySelector("[data-testid='activity-time']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void TheAvatarIsTheVehiclesMainPicture_OrACarIconWhenItHasNone()
    {
        var vehicleId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        dashboard = Dashboard(activity: [Entry(vehicleId: vehicleId, image: imageId), Entry("2015 Jeep Grand Cherokee")]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='activity']");
            var picture = rows[0].QuerySelector("img.mg-avatar")!;
            Assert.Equal($"/vehicles/{vehicleId}/images/{imageId}", picture.GetAttribute("src"));
            Assert.Equal(string.Empty, picture.GetAttribute("alt"));
            Assert.Null(rows[1].QuerySelector("img"));
            Assert.NotNull(rows[1].QuerySelector("span.mg-avatar svg"));
        });
    }

    [Fact]
    public void NoteTextAndCategoryAreShownAsText_NeverAsMarkup()
    {
        dashboard = Dashboard(activity: [Entry(category: "<b>x</b>", excerpt: "<script>alert(1)</script>")]);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='activity'] script"));
            Assert.Empty(cut.FindAll("[data-testid='activity'] b"));
            Assert.Contains("<script>alert(1)</script>", cut.Find("[data-testid='activity-excerpt']").TextContent);
        });
    }

    [Fact]
    public void WithNoJournalEntries_RecentActivitySaysHowToAddOne()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No journal entries yet", cut.Find("[data-testid='no-activity']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='activity-list']"));
        });
    }

    [Fact]
    public void AnEmptyGarage_HasNoRecentUpdatesOrAttentionCards()
    {
        dashboard = Dashboard(0, 0, 0, 0, 0, 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='recent-updates']"));
            Assert.Empty(cut.FindAll("[data-testid='attention']"));
        });
    }

    [Fact]
    public void WithVehiclesButNothingDue_SaysNothingNeedsAttention()
    {
        dashboard = Dashboard(overdue: 0, dueSoon: 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Nothing needs attention", cut.Find("[data-testid='attention']").TextContent));
    }

    [Fact]
    public void AnEmptyGarage_ShowsZerosAndInvitesTheFirstVehicle_WithoutAnAttentionSection()
    {
        dashboard = Dashboard(0, 0, 0, 0, 0, 0);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("0", cut.Find("[data-testid='metric-vehicles'] [data-testid='metric-value']").TextContent.Trim());
            Assert.Equal("/vehicles/new", cut.Find("[data-testid='first-vehicle'] a.btn").GetAttribute("href"));
            Assert.Contains("Add your first vehicle", cut.Find("[data-testid='first-vehicle']").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='attention']"));
        });
    }

    [Fact]
    public void FieldsThePageDoesNotKnow_AreIgnored()
    {
        var json = """{"vehicles":1,"serviceRecords":0,"fuelEnergyEntries":0,"draftsAwaitingReview":0,"remindersOverdue":0,"remindersDueSoon":0,"attention":[],"fuelUsage":[{"month":"2026-10","gallons":12.5}]}""";

        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find("[data-testid='metric-vehicles'] [data-testid='metric-value']").TextContent.Trim()));
    }

    [Fact]
    public void AnOutage_ShowsAnAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

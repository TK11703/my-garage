using System.Net;
using Bunit;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderTabTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private List<ReminderDto> reminders = [];

    private IRenderedComponent<VehicleDetail> RenderTab(Func<HttpRequestMessage, HttpResponseMessage>? remindersResponse = null)
    {
        RegisterApi(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/vehicles/{vehicleId}")
            {
                return Json(Detail(vehicleId));
            }

            return path == $"/api/vehicles/{vehicleId}/reminders"
                ? remindersResponse?.Invoke(request) ?? Json(reminders)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        NavigateTo($"/vehicles/{vehicleId}?tab=reminders");
        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ListsEachReminderWithItsFacts()
    {
        var oil = Reminder(vehicleId, name: "Oil change", intervalMiles: 5_000, intervalTime: new() { Value = 6, Unit = ReminderTimeUnit.Months },
            lastDoneOn: new DateOnly(2026, 5, 1), lastDoneMileage: 40_000, nextDueMileage: 45_000, nextDueDate: new DateOnly(2026, 11, 1));
        var hitch = Reminder(vehicleId, name: "Hitch check", appliesTo: ReminderAppliesTo.Equipment, subject: "Trailer hitch",
            intervalMiles: null, intervalTime: new() { Value = 2, Unit = ReminderTimeUnit.Years }, lastDoneMileage: null,
            nextDueMileage: null, nextDueDate: new DateOnly(2028, 5, 1), milesRemaining: null);
        reminders = [oil, hitch];

        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='reminder']");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Oil change", rows[0].TextContent);
            Assert.Contains("Every 5,000 miles or 6 months", rows[0].TextContent);
            Assert.Contains("May 1, 2026 at 40,000 miles", rows[0].TextContent);
            Assert.Contains("45,000 miles", rows[0].TextContent);
            Assert.Contains("Nov 1, 2026", rows[0].TextContent);
            Assert.Contains("Equipment: Trailer hitch", rows[1].TextContent);
            Assert.Contains("Every 2 years", rows[1].TextContent);
            Assert.Contains("May 1, 2028", rows[1].TextContent);
            Assert.Equal($"/vehicles/{vehicleId}/reminders/{oil.Id}", rows[0].QuerySelector("a[data-testid='reminder-link']")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void AddReminderLivesInTheTopAddMenu_NotOnTheTab()
    {
        reminders = [Reminder(vehicleId)];
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}/reminders/new", cut.Find(".mg-pagebar [data-testid='add-maintenance-menu'] a[data-testid='add-reminder']").GetAttribute("href"));
            Assert.Empty(cut.FindAll("#tab-reminders a[data-testid='add-reminder']"));
            Assert.Empty(cut.FindAll("#tab-reminders a.btn"));
        });
    }

    [Fact]
    public void TheListIsInACardWithATitleAndASubtitle()
    {
        reminders = [Reminder(vehicleId)];
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("#tab-reminders section.card");
            Assert.Equal("Reminders", card.QuerySelector("h2")!.TextContent);
            Assert.Contains("by miles, by time, or both", card.QuerySelector(".card-body > p")!.TextContent);
            Assert.Contains("list-group-flush", card.QuerySelector("[data-testid='reminder-list']")!.ClassName);
        });
    }

    [Theory]
    [InlineData(ReminderStatus.Overdue, "bg-danger-subtle")]
    [InlineData(ReminderStatus.DueSoon, "bg-warning-subtle")]
    [InlineData(ReminderStatus.Upcoming, "bg-success-subtle")]
    [InlineData(ReminderStatus.MileageNeeded, "bg-info-subtle")]
    [InlineData(ReminderStatus.Paused, "bg-secondary-subtle")]
    public void EachRowLeadsWithADateTileColoredByStatus(ReminderStatus status, string tileClass)
    {
        reminders = [Reminder(vehicleId, status: status, nextDueDate: new DateOnly(2026, 11, 9), isPaused: status == ReminderStatus.Paused)];
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            var tile = cut.Find("#tab-reminders [data-testid='due-tile']");
            Assert.Contains(tileClass, tile.ClassName);
            Assert.Contains("Nov", tile.TextContent);
            Assert.Contains("09", tile.TextContent);
            Assert.Contains("Next due Nov 9, 2026", tile.TextContent);
        });
    }

    [Fact]
    public void ARowDueByMileageOnly_ShowsAMileageIconInTheTile()
    {
        reminders = [Reminder(vehicleId, nextDueDate: null)];
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            var tile = cut.Find("#tab-reminders [data-testid='due-tile']");
            Assert.NotNull(tile.QuerySelector("svg"));
            Assert.Contains("Due by mileage", tile.TextContent);
        });
    }

    [Fact]
    public void ARowShowsWhatItAppliesToAndItsIntervalUnderTheName_NotTheVehicleName()
    {
        reminders = [Reminder(vehicleId, name: "Hitch check", appliesTo: ReminderAppliesTo.Equipment, subject: "Trailer hitch", vehicleName: "2020 Ford F-150")];
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("#tab-reminders [data-testid='reminder']");
            Assert.Contains("Equipment: Trailer hitch", row.TextContent);
            Assert.Empty(row.QuerySelectorAll("[data-testid='reminder-vehicle']"));
        });
    }

    [Fact]
    public void AVehicleWithoutReminders_ShowsAnInformationalAlertWithAnIcon()
    {
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='reminder']"));
            var alert = cut.Find("#tab-reminders [data-testid='no-reminders']");
            Assert.Contains("alert-info", alert.ClassName);
            Assert.Equal("status", alert.GetAttribute("role"));
            Assert.NotNull(alert.QuerySelector("svg"));
            Assert.Contains("No reminders yet. Add one for things like oil changes, inspections, and registrations.", alert.TextContent);
            Assert.Empty(cut.FindAll("#tab-reminders a[href$='/reminders/new']"));
        });
    }

    [Fact]
    public void ARemindersOutage_ShowsAnAlertInsideTheTab()
    {
        var cut = RenderTab(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#tab-reminders [role='alert']")));
    }

    [Theory]
    [InlineData("created", "Reminder added.")]
    [InlineData("updated", "Reminder updated.")]
    [InlineData("deleted", "Reminder deleted.")]
    [InlineData("%3Cscript%3E", null)]
    [InlineData("nonsense", null)]
    public void TheSavedValueShowsANoticeOnlyWhenItIsKnown(string saved, string? notice)
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : Json(reminders));
        NavigateTo($"/vehicles/{vehicleId}?tab=reminders&saved={saved}");

        var cut = Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#tab-reminders [data-testid='reminders-view']")));
        Assert.Equal(notice, cut.FindAll("[data-testid='reminder-saved']").FirstOrDefault()?.TextContent.Trim());
    }

    [Fact]
    public void TheDeleteVehicleConfirmationMentionsReminders()
    {
        var cut = RenderTab();

        cut.WaitForAssertion(() => Assert.Contains("reminders", cut.Find("[data-testid='delete-vehicle'] p").TextContent));
    }

    [Fact]
    public void TheTabNavShowsTheRemindersTabAsCurrent()
    {
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Reminders", cut.Find("#tab-reminders-tab").TextContent);
            Assert.Contains("active", cut.Find("#tab-reminders-tab").ClassName);
        });
    }
}

public class ReminderDetailPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid reminderId = Guid.NewGuid();

    private IRenderedComponent<ReminderDetail> RenderPage(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        RegisterApi(respond);
        return Render<ReminderDetail>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.ReminderId, reminderId));
    }

    [Fact]
    public void ShowsTheDetailsCard()
    {
        var reminder = Reminder(vehicleId, reminderId, "Hitch check", ReminderAppliesTo.Location, "Home garage", intervalMiles: 5_000,
            intervalTime: new() { Value = 1, Unit = ReminderTimeUnit.Years }, lastDoneOn: new DateOnly(2026, 5, 1), lastDoneMileage: 40_000,
            nextDueMileage: 45_000, nextDueDate: new DateOnly(2027, 5, 1));

        var cut = RenderPage(_ => Json(DetailOf(reminder)));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Hitch check", cut.Find("h1").TextContent);
            var details = cut.Find("[data-testid='reminder-details']").TextContent;
            Assert.Contains("Location: Home garage", details);
            Assert.Contains("Every 5,000 miles or 1 year", details);
            Assert.Contains("May 1, 2026 at 40,000 miles", details);
            Assert.Contains("45,000 miles", details);
            Assert.Contains("May 1, 2027", details);
            var trail = cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()).ToList();
            Assert.Contains("2020 Ford F-150", trail);
        });
    }

    [Fact]
    public void AnUnknownReminder_ShowsNotFoundWithALinkBackToTheRemindersTab()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Reminder not found", cut.Find("h1").TextContent);
            Assert.Equal($"/vehicles/{vehicleId}?tab=reminders", cut.Find("a.btn").GetAttribute("href"));
        });
    }

    [Fact]
    public void AnApiOutage_ShowsAnAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

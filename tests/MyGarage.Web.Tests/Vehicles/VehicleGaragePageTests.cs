using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

// The garage page: the attention strip, active cards, and the archived section with Restore (specs/016-ui-redesign-instrument-cluster).
public class VehicleGaragePageTests : VehicleTestContext
{
    private List<ReminderDto> reminders = [];
    private HttpStatusCode? remindersFailure;
    private PagedResultDto<VehicleSummaryDto> garage = Page([]);

    private static ReminderDto Reminder(string name, ReminderStatus status, string vehicle = "2020 Ford F-150", int? miles = null, int? days = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            VehicleName = vehicle,
            Name = name,
            LastDoneOn = new DateOnly(2026, 1, 1),
            IsPaused = false,
            Status = status,
            MilesRemaining = miles,
            DaysRemaining = days,
        };

    private static VehicleSummaryDto Archived(string model = "Mustang") =>
        new() { Id = Guid.NewGuid(), Make = "Ford", Model = model, Year = 2005, ArchivedAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero) };

    private IRenderedComponent<VehicleList> RenderGarage(string path = "/")
    {
        RegisterApi(request =>
        {
            var apiPath = request.RequestUri!.AbsolutePath;
            if (apiPath == "/api/reminders")
            {
                return remindersFailure is { } status ? new HttpResponseMessage(status) : Json(reminders);
            }

            if (request.Method == HttpMethod.Post && apiPath.EndsWith("/restore", StringComparison.Ordinal))
            {
                return Json(Detail());
            }

            return Json(garage);
        });
        NavigateTo(path);
        return Render<VehicleList>();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/vehicles")]
    public void TheGarage_IsServedAtTheHomeAddressAndAtVehicles(string path)
    {
        garage = Page([Summary("Alpha")]);

        var cut = RenderGarage(path);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Your garage", cut.Find("h1").TextContent);
            Assert.Single(cut.FindAll(".mg-vehicle-card"));
        });
    }

    [Fact]
    public void TheActions_OneIsPrimary_AndAddMaintenanceIsStillReachable()
    {
        garage = Page([Summary("Alpha")]);

        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            var actions = cut.Find(".mg-pagebar-actions");
            Assert.Single(actions.QuerySelectorAll("a.btn-primary"));
            Assert.Equal("/vehicles/new", actions.QuerySelector("a.btn-primary")!.GetAttribute("href"));
            Assert.Equal("/drafts/upload", actions.QuerySelector("a[data-testid='add-maintenance']")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void TheStrip_CountsOverdueAndDueSoon_NamesTheMostUrgent_AndLinksToTheReminders()
    {
        garage = Page([Summary("Alpha")]);
        reminders =
        [
            Reminder("Inspection", ReminderStatus.Overdue, "2010 Toyota Venza", days: -3),
            Reminder("Oil change", ReminderStatus.DueSoon, miles: 3_780),
            Reminder("Registration", ReminderStatus.Upcoming, days: 200),
        ];

        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("1 overdue, 1 due soon", cut.Find("[data-testid='attention-summary']").TextContent.Trim());
            Assert.Equal("Inspection on 2010 Toyota Venza · 3 days overdue", cut.Find("[data-testid='attention-top']").TextContent.Trim());
            Assert.Equal("/reminders", cut.Find("[data-testid='attention-link']").GetAttribute("href"));
            Assert.Equal("status", cut.Find("[data-testid='attention-strip']").GetAttribute("role"));
            Assert.Contains("mg-attention-strip-bad", cut.Find("[data-testid='attention-strip']").ClassList);
        });
    }

    [Theory]
    [InlineData(ReminderStatus.Overdue, "2 overdue")]
    [InlineData(ReminderStatus.DueSoon, "2 due soon")]
    public void TheStrip_WithOnlyOneKind_SaysJustThat(ReminderStatus status, string expected)
    {
        garage = Page([Summary("Alpha")]);
        reminders = [Reminder("A", status, days: 2), Reminder("B", status, days: 4)];

        var cut = RenderGarage();

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("[data-testid='attention-summary']").TextContent.Trim()));
    }

    [Fact]
    public void TheStrip_WhenNothingIsDue_SaysSo()
    {
        garage = Page([Summary("Alpha")]);
        reminders = [Reminder("Registration", ReminderStatus.Upcoming, days: 200), Reminder("Tires", ReminderStatus.Paused)];

        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Nothing needs attention", cut.Find("[data-testid='attention-summary']").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='attention-top']"));
            Assert.Contains("mg-attention-strip-ok", cut.Find("[data-testid='attention-strip']").ClassList);
        });
    }

    [Fact]
    public void WhenTheRemindersCannotBeRead_TheGarageStillShows_WithoutTheStrip()
    {
        garage = Page([Summary("Alpha")]);
        remindersFailure = HttpStatusCode.InternalServerError;

        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mg-vehicle-card"));
            Assert.Empty(cut.FindAll("[data-testid='attention-strip']"));
        });
    }

    [Fact]
    public void AnEmptyGarage_InvitesAFirstVehicle_WithoutTheStrip()
    {
        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You have no vehicles yet", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='attention-strip']"));
        });
    }

    [Fact]
    public void ArchivedVehicles_SitInTheirOwnSectionBelowTheActiveOnes_WithAHistoryNote()
    {
        garage = Page([Archived(), Summary("Alpha"), Summary("Bravo")]);

        var cut = RenderGarage("/vehicles?status=all");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-testid='active-vehicles'] .mg-vehicle-card").Count);
            var section = cut.Find("[data-testid='archived-section']");
            Assert.Equal("Archived", section.QuerySelector("h2")!.TextContent.Trim());
            Assert.Contains("keep their history", section.TextContent);
            Assert.Single(section.QuerySelectorAll(".mg-vehicle-card"));
            Assert.Equal("Archived Nov 8, 2026", section.QuerySelector("[data-testid='vehicle-archived']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void ActiveVehicles_EndWithAnAddAVehicleCard()
    {
        garage = Page([Summary("Alpha")]);

        var cut = RenderGarage();

        cut.WaitForAssertion(() =>
        {
            var add = cut.Find("[data-testid='active-vehicles'] a[data-testid='add-vehicle-card']");
            Assert.Equal("/vehicles/new", add.GetAttribute("href"));
            Assert.Contains("Add a vehicle", add.TextContent);
        });
    }

    [Fact]
    public void OnlyArchivedVehicles_HaveNoAddAVehicleCard()
    {
        garage = Page([Archived()]);

        var cut = RenderGarage("/vehicles?status=archived");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='add-vehicle-card']")));
    }

    [Fact]
    public void WithNoArchivedVehicles_ThereIsNoArchivedSection()
    {
        garage = Page([Summary("Alpha")]);

        var cut = RenderGarage();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='archived-section']")));
    }

    [Fact]
    public void OnlyArchivedVehicles_ShowOnlyTheArchivedSection()
    {
        garage = Page([Archived()]);

        var cut = RenderGarage("/vehicles?status=archived");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='active-vehicles']"));
            Assert.Single(cut.FindAll("[data-testid='archived-section'] .mg-vehicle-card"));
        });
    }

    [Fact]
    public void EachArchivedVehicle_HasANamedRestoreButton()
    {
        var mustang = Archived("Mustang");
        garage = Page([mustang]);

        var cut = RenderGarage("/vehicles?status=all");

        cut.WaitForAssertion(() =>
        {
            var button = cut.Find("[data-testid='restore-form'] button");
            Assert.Equal("submit", button.GetAttribute("type"));
            Assert.Equal("Restore 2005 Ford Mustang", button.GetAttribute("aria-label"));
            Assert.Equal("Restore", button.TextContent.Trim());
        });
    }

    [Fact]
    public void Restore_PostsToTheApi_AndReturnsToTheGarage()
    {
        var mustang = Archived();
        garage = Page([mustang]);
        var cut = RenderGarage("/vehicles?status=all");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='restore-form']")));

        cut.Find("[data-testid='restore-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/vehicles", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{mustang.Id}/restore", Requests.Single(request => request.Method == HttpMethod.Post).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Restore_WhenTheApiRejectsTheToken_ShowsSignInAgain()
    {
        garage = Page([Archived()]);
        RegisterApi(request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : request.RequestUri!.AbsolutePath == "/api/reminders" ? Json(reminders) : Json(garage));
        NavigateTo("/vehicles?status=all");
        var cut = Render<VehicleList>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='restore-form']")));

        cut.Find("[data-testid='restore-form']").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }

    [Fact]
    public void LinksCarryingSearchCriteria_AreStillSentToTheSearchPage_FromTheHomeAddressToo()
    {
        var cut = RenderGarage("/?q=zzz");

        Assert.EndsWith("/search?q=zzz", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.NotNull(cut);
    }
}

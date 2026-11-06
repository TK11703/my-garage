using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderEditPageTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid reminderId = Guid.NewGuid();
    private string putBody = string.Empty;
    private ReminderDto reminder;

    public ReminderEditPageTests()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        reminder = Reminder(vehicleId, reminderId, "Oil change", intervalMiles: 5_000, intervalTime: new() { Value = 2, Unit = ReminderTimeUnit.Years },
            lastDoneOn: new DateOnly(2026, 5, 1), lastDoneMileage: 40_000);
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path != $"/api/vehicles/{vehicleId}/reminders/{reminderId}")
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Put)
        {
            putBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(reminder);
        }

        return Json(DetailOf(reminder));
    }

    private IRenderedComponent<ReminderEdit> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? Respond);
        return Render<ReminderEdit>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));
    }

    [Fact]
    public void PrefillsEveryField_AndTheTrail()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Edit reminder", cut.Find("h1").TextContent);
            Assert.Equal("edit-reminder", cut.Find("form").GetAttribute("id"));
            Assert.Equal("Oil change", cut.Find("#name").GetAttribute("value"));
            Assert.Equal("5000", cut.Find("#interval-miles").GetAttribute("value"));
            Assert.Equal("2", cut.Find("#interval-time-value").GetAttribute("value"));
            Assert.Equal("years", cut.Find("#interval-time-unit").GetAttribute("value"));
            Assert.Equal("2026-05-01", cut.Find("#last-done-on").GetAttribute("value"));
            Assert.Equal("40000", cut.Find("#last-done-mileage").GetAttribute("value"));
            Assert.Equal(ReminderLinks.Detail(vehicleId, reminderId), cut.Find("a.btn-outline-secondary").GetAttribute("href"));
            var crumbs = cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()).ToList();
            Assert.Contains("2020 Ford F-150", crumbs);
            Assert.Contains("Oil change", crumbs);
        });
    }

    [Fact]
    public void AnEquipmentReminder_PrefillsTheAppliesToAndTheSubject()
    {
        reminder = Reminder(vehicleId, reminderId, "Hitch check", ReminderAppliesTo.Equipment, "Trailer hitch");

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("#applies-to-equipment").HasAttribute("checked"));
            Assert.Equal("Trailer hitch", cut.Find("#subject").GetAttribute("value"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Name", "Name is required."), ("IntervalTime.Value", "Years must be a whole number from 1 to 50."))
            : Respond(request));
        cut.WaitForElement("input#name").Change("");
        cut.Find("input#interval-time-value").Change("99");

        cut.Find("form#edit-reminder").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Name is required.", cut.Find("#name-error").TextContent);
            Assert.Contains("Years must be a whole number", cut.Find("#timevalue-error").TextContent);
            Assert.Equal("99", cut.Find("#interval-time-value").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PutsTheValuesAndRedirectsToTheDetailPageWithTheUpdatedNotice()
    {
        var cut = RenderPage();
        cut.WaitForElement("input#name").Change("Synthetic oil change");

        cut.Find("form#edit-reminder").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/reminders/{reminderId}?saved=updated", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Contains("\"name\":\"Synthetic oil change\"", putBody);
        Assert.Contains("\"intervalMiles\":5000", putBody);
    }

    [Fact]
    public void AnUnknownReminder_ShowsNotFoundWithoutAForm()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Reminder not found", cut.Find("h1").TextContent);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void AnApiOutage_ShowsAnAlert()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

public class ReminderManageActionsTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid reminderId = Guid.NewGuid();
    private ReminderDto reminder;

    public ReminderManageActionsTests()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        reminder = Reminder(vehicleId, reminderId, "Oil change");
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == $"/api/vehicles/{vehicleId}")
        {
            return Json(new VehicleDetailDto { Id = vehicleId, Make = "Ford", Model = "F-150", Year = 2020, Tags = [], Images = [], CreatedAt = Now, UpdatedAt = Now });
        }

        var reminderPath = $"/api/vehicles/{vehicleId}/reminders/{reminderId}";
        if (request.Method == HttpMethod.Get && path == reminderPath)
        {
            return Json(DetailOf(reminder));
        }

        if (request.Method == HttpMethod.Post && (path == reminderPath + "/pause" || path == reminderPath + "/resume"))
        {
            return Json(reminder);
        }

        return request.Method == HttpMethod.Delete && path == reminderPath ? new HttpResponseMessage(HttpStatusCode.NoContent) : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private IRenderedComponent<ReminderDetail> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? Respond);
        return Render<ReminderDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));
    }

    [Fact]
    public void AnActiveReminder_OffersEditPauseAndDelete()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(ReminderLinks.Edit(vehicleId, reminderId), cut.Find("a[data-testid='edit-reminder']").GetAttribute("href"));
            Assert.NotNull(cut.Find("form[data-testid='pause-reminder']"));
            Assert.Empty(cut.FindAll("form[data-testid='resume-reminder']"));
            var delete = cut.Find("details[data-testid='delete-reminder']");
            Assert.False(delete.HasAttribute("open"));
            Assert.Contains("Delete Oil change?", delete.TextContent);
            Assert.Equal(ReminderLinks.Detail(vehicleId, reminderId), delete.QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void APausedReminder_OffersResumeAndExplainsThatItSendsNothing()
    {
        reminder = Reminder(vehicleId, reminderId, isPaused: true, status: ReminderStatus.Paused);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("form[data-testid='resume-reminder']"));
            Assert.Empty(cut.FindAll("form[data-testid='pause-reminder']"));
            Assert.Equal("Paused", cut.Find("[data-testid='reminder-status']").TextContent.Trim());
            Assert.Contains("sends no notifications", cut.Find("[data-testid='paused-note']").TextContent);
        });
    }

    [Fact]
    public void Pause_PostsAndRedirectsWithThePausedNotice()
    {
        var cut = RenderPage();

        cut.WaitForElement("form[data-testid='pause-reminder']").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/reminders/{reminderId}?saved=paused", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{vehicleId}/reminders/{reminderId}/pause", Requests.Single(request => request.Method == HttpMethod.Post).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Resume_PostsAndRedirectsWithTheResumedNotice()
    {
        reminder = Reminder(vehicleId, reminderId, isPaused: true, status: ReminderStatus.Paused);
        var cut = RenderPage();

        cut.WaitForElement("form[data-testid='resume-reminder']").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/reminders/{reminderId}?saved=resumed", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{vehicleId}/reminders/{reminderId}/resume", Requests.Single(request => request.Method == HttpMethod.Post).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void NothingIsDeletedUntilConfirmed()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='delete-reminder']")));

        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingDelete_SendsItAndRedirectsToTheRemindersTabWithTheDeletedNotice()
    {
        var cut = RenderPage();

        cut.WaitForElement("details[data-testid='delete-reminder'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=reminders&saved=deleted", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{vehicleId}/reminders/{reminderId}", Requests.Single(request => request.Method == HttpMethod.Delete).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void AFailedAction_ShowsAnAlert()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Respond(request));

        cut.WaitForElement("form[data-testid='pause-reminder']").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }

    [Theory]
    [InlineData("paused", "Reminder paused.")]
    [InlineData("resumed", "Reminder resumed.")]
    [InlineData("updated", "Reminder updated.")]
    public void TheSavedValueShowsANotice(string saved, string expectedStart)
    {
        RegisterApi(Respond);
        NavigateTo($"/vehicles/{vehicleId}/reminders/{reminderId}?saved={saved}");

        var cut = Render<ReminderDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));

        cut.WaitForAssertion(() => Assert.StartsWith(expectedStart, cut.Find("[data-testid='reminder-saved']").TextContent.Trim()));
    }
}

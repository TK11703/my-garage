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

public class ReminderCompleteTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid reminderId = Guid.NewGuid();
    private string postedBody = string.Empty;
    private ReminderDto reminder;
    private ReminderCompletionDto[] completions = [];

    public ReminderCompleteTests()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        reminder = Reminder(vehicleId, reminderId, "Oil change", status: ReminderStatus.Overdue, milesRemaining: -200);
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == $"/api/vehicles/{vehicleId}")
        {
            return Json(new VehicleDetailDto
            {
                Id = vehicleId, Make = "Ford", Model = "F-150", Year = 2020, Mileage = 46_200, Tags = [], Images = [], CreatedAt = Now, UpdatedAt = Now,
            });
        }

        if (path == $"/api/vehicles/{vehicleId}/reminders/{reminderId}/complete")
        {
            postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(DetailOf(reminder, completions));
        }

        return path == $"/api/vehicles/{vehicleId}/reminders/{reminderId}" ? Json(DetailOf(reminder, completions)) : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private IRenderedComponent<ReminderDetail> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null, string? saved = null)
    {
        RegisterApi(respond ?? Respond);
        if (saved is not null)
        {
            NavigateTo($"/vehicles/{vehicleId}/reminders/{reminderId}?saved={saved}");
        }

        return Render<ReminderDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.ReminderId, reminderId));
    }

    [Fact]
    public void TheMarkDoneCard_HasTheFormWithTodayAndTheVehiclesMileageAsDefaults()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("complete-reminder", cut.Find("form#complete-reminder").GetAttribute("id"));
            Assert.Equal("2026-11-06", cut.Find("#done-on").GetAttribute("value"));
            Assert.Equal("46200", cut.Find("#done-mileage").GetAttribute("value"));
            Assert.Contains("Mileage", cut.Find("label[for='done-mileage']").TextContent);
            Assert.Contains("Done on", cut.Find("label[for='done-on']").TextContent);
            Assert.Contains("Also update the vehicle's mileage", cut.Find("label[for='update-vehicle-mileage']").TextContent);
            Assert.Equal("checkbox", cut.Find("#update-vehicle-mileage").GetAttribute("type"));
        });
    }

    [Fact]
    public void ATimeOnlyReminder_DoesNotMarkTheMileageRequired()
    {
        reminder = Reminder(vehicleId, reminderId, intervalMiles: null, intervalTime: new() { Value = 1, Unit = ReminderTimeUnit.Years },
            lastDoneMileage: null, nextDueMileage: null, milesRemaining: null, daysRemaining: -3, status: ReminderStatus.Overdue);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(cut.Find("#done-mileage").GetAttribute("aria-required"));
            Assert.Empty(cut.FindAll("label[for='done-mileage'] .text-danger"));
        });
    }

    [Fact]
    public void ADistanceReminder_MarksTheMileageRequired()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#done-mileage").GetAttribute("aria-required"));
            Assert.Single(cut.FindAll("label[for='done-mileage'] .text-danger"));
        });
    }

    [Fact]
    public void TheHistoryCard_ListsCompletionsMostRecentFirst()
    {
        completions = [Completion(new DateOnly(2026, 6, 1), 44_000), Completion(new DateOnly(2026, 1, 15), null)];

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-testid='completion']");
            Assert.Equal(2, items.Count);
            Assert.Contains("Jun 1, 2026", items[0].TextContent);
            Assert.Contains("44,000 miles", items[0].TextContent);
            Assert.Contains("Jan 15, 2026", items[1].TextContent);
            Assert.DoesNotContain("miles", items[1].TextContent);
        });
    }

    [Fact]
    public void WithoutCompletions_TheHistoryCardSaysSo()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("Nothing has been marked done yet", cut.Find("[data-testid='history']").TextContent));
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Post
            ? ValidationProblem(("DoneOn", "Done on date must be between 1900-01-01 and today."), ("Mileage", "Mileage cannot be below the last done mileage of 40,000."))
            : Respond(request));
        cut.WaitForElement("input#done-mileage").Change("100");
        cut.Find("input#done-on").Change("2027-01-01");

        cut.Find("form#complete-reminder").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("between 1900-01-01 and today", cut.Find("#doneon-error").TextContent);
            Assert.Contains("cannot be below the last done mileage", cut.Find("#mileage-error").TextContent);
            Assert.Equal("100", cut.Find("#done-mileage").GetAttribute("value"));
            Assert.Equal("2027-01-01", cut.Find("#done-on").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsWithTheDoneNotice()
    {
        var cut = RenderPage();
        cut.WaitForElement("input#done-mileage").Change("46300");
        cut.Find("input#update-vehicle-mileage").Change(true);

        cut.Find("form#complete-reminder").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}/reminders/{reminderId}?saved=done", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/reminders/{reminderId}/complete", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"doneOn\":\"2026-11-06\"", postedBody);
        Assert.Contains("\"mileage\":46300", postedBody);
        Assert.Contains("\"updateVehicleMileage\":true", postedBody);
    }

    [Fact]
    public void TheDoneNotice_NamesTheNewNextDuePoint()
    {
        reminder = Reminder(vehicleId, reminderId, nextDueMileage: 51_200, milesRemaining: 5_000);

        var cut = RenderPage(saved: "done");

        cut.WaitForAssertion(() => Assert.Equal("Marked done. Next due 51,200 miles.", cut.Find("[data-testid='reminder-saved']").TextContent.Trim()));
    }

    [Fact]
    public void AnApiOutageWhileSaving_ShowsAnAlert()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Respond(request));
        cut.WaitForElement("input#done-mileage").Change("46300");

        cut.Find("form#complete-reminder").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alert']")));
    }
}

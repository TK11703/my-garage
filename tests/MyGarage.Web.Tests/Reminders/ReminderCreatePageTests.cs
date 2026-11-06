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

public class ReminderCreatePageTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();
    private string postedBody = string.Empty;
    private List<ReminderDto> existing = [];
    private int? vehicleMileage = 42_000;

    public ReminderCreatePageTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));

    private VehicleDetailDto Vehicle() =>
        new()
        {
            Id = vehicleId,
            Make = "Ford",
            Model = "F-150",
            Year = 2020,
            Mileage = vehicleMileage,
            Tags = [],
            Images = [],
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == $"/api/vehicles/{vehicleId}")
        {
            return Json(Vehicle());
        }

        if (path == $"/api/vehicles/{vehicleId}/reminders")
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(existing);
            }

            postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(Reminder(vehicleId), HttpStatusCode.Created);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private IRenderedComponent<ReminderCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null, string? preset = null)
    {
        RegisterApi(respond ?? Respond);
        if (preset is not null)
        {
            NavigateTo($"/vehicles/{vehicleId}/reminders/new?preset={preset}");
        }

        return Render<ReminderCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsEveryFieldWithALabelAndTheInputLimits()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add reminder", cut.Find("h1").TextContent);
            Assert.Equal("create-reminder", cut.Find("form").GetAttribute("id"));
            foreach (var (id, label) in new[]
            {
                ("name", "Name"), ("subject", "Equipment or location name"), ("interval-miles", "Repeat every (miles)"),
                ("interval-time-value", "Repeat every (months or years)"), ("last-done-on", "Last done on"), ("last-done-mileage", "Last done mileage"),
            })
            {
                Assert.Contains(label, cut.Find($"label[for='{id}']").TextContent);
            }

            Assert.Equal("100", cut.Find("#name").GetAttribute("maxlength"));
            Assert.Equal("1", cut.Find("#interval-miles").GetAttribute("min"));
            Assert.Equal("999999", cut.Find("#interval-miles").GetAttribute("max"));
            Assert.Equal("0", cut.Find("#last-done-mileage").GetAttribute("min"));
            Assert.Equal("9999999", cut.Find("#last-done-mileage").GetAttribute("max"));
            Assert.Equal("date", cut.Find("#last-done-on").GetAttribute("type"));
            Assert.Equal(["months", "years"], cut.FindAll("#interval-time-unit option").Select(option => option.GetAttribute("value")));
            Assert.Equal(["vehicle", "equipment", "location"], cut.FindAll("input[type='radio'][name='Model.AppliesTo']").Select(radio => radio.GetAttribute("value")!.ToLowerInvariant()));
        });
    }

    [Fact]
    public void TheDefaultsAreTodayAndTheVehiclesCurrentMileage()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2026-11-06", cut.Find("#last-done-on").GetAttribute("value"));
            Assert.Equal("42000", cut.Find("#last-done-mileage").GetAttribute("value"));
            Assert.True(cut.Find("#applies-to-vehicle").HasAttribute("checked"));
        });
    }

    [Fact]
    public void TheOilChangePreset_FillsTheNameAndTheIntervals()
    {
        var cut = RenderPage(preset: "oil-change");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Oil change", cut.Find("#name").GetAttribute("value"));
            Assert.Equal("5000", cut.Find("#interval-miles").GetAttribute("value"));
            Assert.Equal("6", cut.Find("#interval-time-value").GetAttribute("value"));
            Assert.Equal("months", cut.Find("#interval-time-unit").GetAttribute("value"));
        });
    }

    [Fact]
    public void TheInspectionPreset_IsATimeOnlyReminderInYears()
    {
        var cut = RenderPage(preset: "inspection");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Inspection", cut.Find("#name").GetAttribute("value"));
            Assert.Null(cut.Find("#interval-miles").GetAttribute("value"));
            Assert.Equal("1", cut.Find("#interval-time-value").GetAttribute("value"));
            Assert.Equal("years", cut.Find("#interval-time-unit").GetAttribute("value"));
        });
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("nonsense")]
    public void CustomAndUnknownPresets_LeaveTheFormEmpty(string preset)
    {
        var cut = RenderPage(preset: preset);

        cut.WaitForAssertion(() =>
        {
            Assert.Null(cut.Find("#name").GetAttribute("value"));
            Assert.Null(cut.Find("#interval-miles").GetAttribute("value"));
        });
    }

    [Fact]
    public void ThePresetLinksCoverEveryTypeAndCustom()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("a[data-preset]");
            Assert.Equal(["oil-change", "tire-rotation", "inspection", "registration", "custom"], links.Select(link => link.GetAttribute("data-preset")));
            Assert.Equal($"/vehicles/{vehicleId}/reminders/new?preset=oil-change", links[0].GetAttribute("href"));
        });
    }

    [Fact]
    public void AVehicleWithoutAMileage_ExplainsThatADistanceReminderCannotShowAStatusYet()
    {
        vehicleMileage = null;

        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.Contains("until it has one", cut.Find("[data-testid='no-mileage-note']").TextContent));
    }

    [Fact]
    public void AVehicleWithAMileage_ShowsNoMileageNote()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("h1")));
        Assert.Empty(cut.FindAll("[data-testid='no-mileage-note']"));
    }

    [Fact]
    public void ASameNamedReminder_ShowsANonBlockingWarning()
    {
        existing = [Reminder(vehicleId, name: "oil change")];

        var cut = RenderPage(preset: "oil-change");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("already has a reminder named", cut.Find("[data-testid='duplicate-name']").TextContent);
            Assert.NotNull(cut.Find("form"));
        });
    }

    [Fact]
    public void ARejectedSave_ShowsMessagesBesideTheFieldsAndKeepsTheTypedValues()
    {
        var cut = RenderPage(request => request.Method == HttpMethod.Post
            ? ValidationProblem(
                ("Name", "Name is required."),
                ("IntervalMiles", "Miles must be a whole number from 1 to 999,999."),
                ("IntervalTime.Value", "Months must be a whole number from 1 to 600."),
                ("LastDoneOn", "Last done date must be between 1900-01-01 and today."),
                ("LastDoneMileage", "Last done mileage is required for a reminder that repeats by miles."))
            : Respond(request));
        cut.WaitForElement("input#interval-miles").Change("0");
        cut.Find("input#interval-time-value").Change("700");
        cut.Find("input#last-done-mileage").Change("12");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Name is required.", cut.Find("#name-error").TextContent);
            Assert.Contains("Miles must be a whole number", cut.Find("#intervalmiles-error").TextContent);
            Assert.Contains("Months must be a whole number", cut.Find("#timevalue-error").TextContent);
            Assert.Contains("between 1900-01-01 and today", cut.Find("#lastdoneon-error").TextContent);
            Assert.Contains("is required for a reminder that repeats by miles", cut.Find("#lastdonemileage-error").TextContent);
            Assert.Equal("intervalmiles-error", cut.Find("#interval-miles").GetAttribute("aria-describedby"));
            Assert.Equal("0", cut.Find("#interval-miles").GetAttribute("value"));
            Assert.Equal("700", cut.Find("#interval-time-value").GetAttribute("value"));
            Assert.Equal("12", cut.Find("#last-done-mileage").GetAttribute("value"));
        });
    }

    [Fact]
    public void ASuccessfulSave_PostsTheValuesAndRedirectsToTheRemindersTab()
    {
        var cut = RenderPage();
        cut.WaitForElement("input#name").Change("Oil change");
        cut.Find("input#interval-miles").Change("5000");
        cut.Find("input#interval-time-value").Change("6");
        cut.Find("input#last-done-on").Change("2026-10-01");
        cut.Find("input#last-done-mileage").Change("40000");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}?tab=reminders&saved=created", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/reminders", post.RequestUri!.AbsolutePath);
        Assert.Contains("\"name\":\"Oil change\"", postedBody);
        Assert.Contains("\"appliesTo\":\"vehicle\"", postedBody);
        Assert.Contains("\"intervalMiles\":5000", postedBody);
        Assert.Contains("\"unit\":\"months\"", postedBody);
        Assert.Contains("2026-10-01", postedBody);
        Assert.Contains("\"lastDoneMileage\":40000", postedBody);
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundStateWithoutAForm()
    {
        var cut = RenderPage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
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

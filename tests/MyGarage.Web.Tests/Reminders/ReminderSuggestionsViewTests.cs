using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Reminders;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderSuggestionsViewTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();

    public ReminderSuggestionsViewTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));

    private static SuggestedReminderDto Suggestion(string name = "Oil change", int? miles = 5_000, int? months = 6, int? mileage = 45_000) =>
        new()
        {
            Name = name,
            IntervalMiles = miles,
            IntervalMonths = months,
            LastDoneOn = new DateOnly(2026, 1, 15),
            LastDoneMileage = mileage,
            Basis = "Done 3 times; last on Jan 15, 2026",
        };

    private IRenderedComponent<VehicleDetail> RenderTab(Func<HttpResponseMessage> suggestions)
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"/api/vehicles/{vehicleId}" => Json(Detail(vehicleId)),
            var path when path == $"/api/vehicles/{vehicleId}/reminders" => Json(new List<ReminderDto>()),
            var path when path == $"/api/vehicles/{vehicleId}/reminder-suggestions" => suggestions(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        NavigateTo($"/vehicles/{vehicleId}?tab=reminders");
        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void TheRemindersTab_OffersEachSuggestionWithItsIntervalBasisAndAPrefilledLink()
    {
        var cut = RenderTab(() => Json(new[] { Suggestion() }));

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[data-testid='reminder-suggestions']");
            Assert.Equal("Suggested reminders", panel.QuerySelector("h2")!.TextContent);
            var row = Assert.Single(panel.QuerySelectorAll("[data-testid='reminder-suggestion']"));
            Assert.Equal("Oil change", row.QuerySelector("[data-testid='suggestion-name']")!.TextContent.Trim());
            Assert.Contains("Every 5,000 miles or 6 months", row.TextContent);
            Assert.Contains("Done 3 times; last on Jan 15, 2026", row.TextContent);
            var link = row.QuerySelector("a[data-testid='use-suggestion']")!;
            Assert.Equal("Set up a reminder for Oil change", link.GetAttribute("aria-label"));
            Assert.Equal($"/vehicles/{vehicleId}/reminders/new?name=Oil%20change&miles=5000&months=6&doneOn=2026-01-15&mileage=45000", link.GetAttribute("href"));
        });
    }

    [Fact]
    public void WithoutSuggestions_NoPanelShows()
    {
        var cut = RenderTab(() => Json(Array.Empty<SuggestedReminderDto>()));

        cut.WaitForElement("[data-testid='no-reminders']");
        Assert.Empty(cut.FindAll("[data-testid='reminder-suggestions']"));
    }

    [Fact]
    public void AFailedSuggestionsCall_LeavesTheTabWorking()
    {
        var cut = RenderTab(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        cut.WaitForElement("[data-testid='no-reminders']");
        Assert.Empty(cut.FindAll("[data-testid='reminder-suggestions']"));
    }

    [Fact]
    public void TheServiceRecordPage_OffersOnlyTheSuggestionsForItsOwnServices()
    {
        var record = RecordDetail(vehicleId, recordId, lineItems:
        [
            new ServiceLineItemDto { Id = Guid.NewGuid(), Description = "oil change", FlatCost = 40m, Parts = [], TotalCost = 40m },
        ]);
        RegisterApi(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"/api/vehicles/{vehicleId}" => Json(Detail(vehicleId)),
            var path when path == $"/api/vehicles/{vehicleId}/reminder-suggestions" => Json(new[] { Suggestion(), Suggestion("Tire rotation") }),
            var path when path == $"/api/vehicles/{vehicleId}/service-records/{recordId}" => Json(record),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var cut = Render<ServiceRecordDetail>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId));

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid='reminder-suggestion']"));
            Assert.Equal("Oil change", row.QuerySelector("[data-testid='suggestion-name']")!.TextContent.Trim());
        });
    }

    [Fact]
    public void TheServiceRecordPage_ForARecordWithoutLines_AsksForNoSuggestions()
    {
        RegisterApi(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path == $"/api/vehicles/{vehicleId}" => Json(Detail(vehicleId)),
            var path when path == $"/api/vehicles/{vehicleId}/service-records/{recordId}" => Json(RecordDetail(vehicleId, recordId)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var cut = Render<ServiceRecordDetail>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId));

        cut.WaitForElement("[data-testid='total-cost']");
        Assert.DoesNotContain(Requests, request => request.RequestUri!.AbsolutePath.EndsWith("reminder-suggestions", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAddForm_OpensWithASuggestionsValues()
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(new VehicleDetailDto { Id = vehicleId, Make = "Ford", Model = "F-150", Year = 2020, Mileage = 40_000, Tags = [], Images = [], CreatedAt = Now, UpdatedAt = Now })
            : Json(new List<ReminderDto>()));
        NavigateTo($"/vehicles/{vehicleId}/reminders/new?name=Oil%20change&miles=5000&months=6&doneOn=2026-01-15&mileage=45000");

        var cut = Render<ReminderCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Oil change", cut.Find("#name").GetAttribute("value"));
            Assert.Equal("5000", cut.Find("#interval-miles").GetAttribute("value"));
            Assert.Equal("6", cut.Find("#interval-time-value").GetAttribute("value"));
            Assert.Equal("2026-01-15", cut.Find("#last-done-on").GetAttribute("value"));
            Assert.Equal("45000", cut.Find("#last-done-mileage").GetAttribute("value"));
            Assert.Equal("50,000 miles or Jul 15, 2026, whichever comes first", cut.Find("[data-preview-text]").TextContent.Trim());
        });
    }
}

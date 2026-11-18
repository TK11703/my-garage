using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Reminders;

public class ReminderNextDuePreviewFormatTests
{
    [Fact]
    public void WithBothIntervals_ShowsMilesAndDate_AndSaysWhicheverComesFirst() =>
        Assert.Equal(
            "45,000 miles or Nov 1, 2026, whichever comes first",
            ReminderFormat.NextDuePreview(5_000, 6, ReminderTimeUnit.Months, new DateOnly(2026, 5, 1), 40_000));

    [Fact]
    public void WithOnlyADistance_ShowsTheMiles() =>
        Assert.Equal("45,000 miles", ReminderFormat.NextDuePreview(5_000, null, ReminderTimeUnit.Months, new DateOnly(2026, 5, 1), 40_000));

    [Theory]
    [InlineData(1, ReminderTimeUnit.Years, "May 1, 2027")]
    [InlineData(18, ReminderTimeUnit.Months, "Nov 1, 2027")]
    public void WithOnlyATime_ShowsTheDate(int value, ReminderTimeUnit unit, string expected) =>
        Assert.Equal(expected, ReminderFormat.NextDuePreview(null, value, unit, new DateOnly(2026, 5, 1), null));

    [Fact]
    public void AMonthEndClampsLikeTheServer() =>
        Assert.Equal("Feb 28, 2026", ReminderFormat.NextDuePreview(null, 1, ReminderTimeUnit.Months, new DateOnly(2026, 1, 31), null));

    [Theory]
    [InlineData(null, null)]
    [InlineData(5_000, null)]
    public void WithNothingToCompute_ExplainsWhatToEnter(int? miles, int? months)
    {
        var text = ReminderFormat.NextDuePreview(miles, months, ReminderTimeUnit.Months, new DateOnly(2026, 5, 1), null);

        Assert.Equal("Enter how often it repeats and when it was last done to see when it is next due.", text);
    }
}

public class ReminderNextDuePreviewFormTests : VehicleTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();

    public ReminderNextDuePreviewFormTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));

    [Fact]
    public void TheFormShowsANextDuePreview_FromThePresetAndTheVehiclesMileage()
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(new VehicleDetailDto { Id = vehicleId, Make = "Ford", Model = "F-150", Year = 2020, Mileage = 40_000, Tags = [], Images = [], CreatedAt = Now, UpdatedAt = Now })
            : Json(new List<ReminderDto>()));
        NavigateTo($"/vehicles/{vehicleId}/reminders/new?preset=oil-change");

        var cut = Render<ReminderCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            var preview = cut.Find("[data-reminder-preview]");
            Assert.Equal("polite", preview.GetAttribute("aria-live"));
            Assert.Equal("45,000 miles or May 6, 2027, whichever comes first", preview.QuerySelector("[data-preview-text]")!.TextContent.Trim());
        });
    }
}

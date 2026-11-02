using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleLatestOdometerWebTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static VehicleDetailDto WithLatest(Guid id, int? latest) =>
        new()
        {
            Id = id, Make = "Ford", Model = "F-150", Year = 2020, Mileage = 42_000, LatestOdometerReading = latest,
            Tags = [], Images = [], CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void FormModel_RoundTripsTheLatestOdometerReading()
    {
        var model = VehicleFormModel.From(WithLatest(Guid.NewGuid(), 61_000));

        Assert.Equal(61_000, model.LatestOdometerReading);
        Assert.Equal(61_000, model.ToRequest().LatestOdometerReading);
    }

    [Fact]
    public void BasicFields_IncludeTheLatestOdometerReadingAfterTheMileage() =>
        Assert.Equal(
            ["Year", "Make", "Model", "Trim", "Vin", "VehicleTypeId", "Ownership", "PurchasedOn", "LeaseEndsOn", "Mileage", "LatestOdometerReading"],
            VehicleFormFields.BasicFields);

    [Fact]
    public void TheForm_OffersAnOptionalNumericLatestOdometerReadingWithHelperText()
    {
        RegisterApi(_ => Json(WithLatest(vehicleId, 61_000)));

        var cut = Render<VehicleEdit>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            var input = cut.Find("#group-basic input#latest-odometer-reading");
            Assert.Equal("number", input.GetAttribute("type"));
            Assert.Equal("0", input.GetAttribute("min"));
            Assert.Equal("9999999", input.GetAttribute("max"));
            Assert.Equal("1", input.GetAttribute("step"));
            Assert.Equal("numeric", input.GetAttribute("inputmode"));
            Assert.Null(input.GetAttribute("aria-required"));
            Assert.Equal("61000", input.GetAttribute("value"));
            Assert.NotNull(cut.Find("label[for='latest-odometer-reading']"));
            Assert.Contains("Shown on your garage card unless a newer service record has a higher reading.", cut.Find("#latest-odometer-help").TextContent);
            Assert.Contains("latest-odometer-help", input.GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void TheFormFieldComesAfterTheMileageField()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        cut.WaitForAssertion(() =>
        {
            var ids = cut.Find("#group-basic").QuerySelectorAll("input[id], select[id]").Select(field => field.Id).ToList();
            Assert.Equal(["mileage", "latest-odometer-reading"], ids.Skip(ids.IndexOf("mileage")));
        });
    }

    [Fact]
    public void AnApiErrorOnTheReading_ShowsBesideTheFieldOnTheDetailsTab()
    {
        RegisterApi(_ => ValidationProblem(("LatestOdometerReading", "Latest odometer reading must be between 0 and 9,999,999.")));
        var cut = Render<VehicleCreate>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Latest odometer reading must be between", cut.Find("#group-basic #latestodometerreading-error").TextContent);
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Fact]
    public void TheDetailPage_ShowsTheReadingOrNotSet()
    {
        RegisterApi(_ => Json(WithLatest(vehicleId, 61_000)));
        var withReading = Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        withReading.WaitForAssertion(() =>
        {
            var basic = withReading.Find("#group-basic");
            var labels = basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
            var values = basic.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()).ToList();
            Assert.Equal("61,000 mi", values[labels.IndexOf("Latest odometer reading")]);
        });
    }

    [Fact]
    public void TheDetailPage_ShowsNotSetWithoutAReading()
    {
        RegisterApi(_ => Json(WithLatest(vehicleId, null)));

        var cut = Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#group-basic");
            var labels = basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
            var values = basic.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()).ToList();
            Assert.Equal("Not set", values[labels.IndexOf("Latest odometer reading")]);
            Assert.Equal(labels.IndexOf("Latest odometer reading"), labels.FindIndex(label => label.StartsWith("Mileage", StringComparison.Ordinal)) + 1);
        });
    }

    [Fact]
    public void TheCard_StillShowsTheSummaryMileageAsBefore()
    {
        RegisterApi(_ => Json(Page([Summary(mileage: 70_000)])));

        var cut = Render<VehicleCard>(parameters => parameters.Add(card => card.Vehicle, Summary(mileage: 70_000)));

        Assert.Equal("70,000 mi", cut.Find("[data-testid='vehicle-mileage']").TextContent.Trim());
    }
}

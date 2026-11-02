using Bunit;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleFormTests : VehicleTestContext
{
    private static readonly VehicleFormLookups Choices = new()
    {
        VehicleTypes = [Value("Sedan"), Value("Wagon")],
        FuelTypes = [Value("Gasoline"), Value("Diesel")],
        Transmissions = [Value("Automatic")],
        Drivetrains = [Value("FWD"), Value("AWD")],
    };

    private static LookupValueDto Value(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private IRenderedComponent<VehicleForm> RenderForm(
        bool tabbed = false,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? activeTab = null,
        VehicleFormModel? model = null) =>
        Render<VehicleForm>(parameters => parameters
            .Add(form => form.Model, model ?? new VehicleFormModel())
            .Add(form => form.FormName, "create-vehicle")
            .Add(form => form.SubmitText, "Add vehicle")
            .Add(form => form.CancelHref, "/vehicles")
            .Add(form => form.Errors, errors ?? new Dictionary<string, string[]>())
            .Add(form => form.Lookups, Choices)
            .Add(form => form.Tabbed, tabbed)
            .Add(form => form.ActiveTab, activeTab)
            .Add(form => form.OnSubmit, () => { }));

    [Theory]
    [InlineData("nickname")]
    [InlineData("year")]
    [InlineData("make")]
    [InlineData("model")]
    public void RequiredFields_ShowAVisibleMarkerAndAriaRequired(string field)
    {
        var cut = RenderForm();

        var marker = cut.Find($"label[for='{field}'] span.text-danger");
        Assert.Equal("*", marker.TextContent.Trim());
        Assert.Equal("true", marker.GetAttribute("aria-hidden"));
        Assert.Equal("true", cut.Find($"#{field}").GetAttribute("aria-required"));
    }

    [Theory]
    [InlineData("vin")]
    [InlineData("trim")]
    [InlineData("vehicle-type")]
    [InlineData("ownership")]
    [InlineData("purchased-on")]
    [InlineData("lease-ends-on")]
    [InlineData("engine")]
    [InlineData("horsepower")]
    [InlineData("fuel-type")]
    [InlineData("transmission")]
    [InlineData("color")]
    [InlineData("mileage")]
    [InlineData("seats")]
    [InlineData("doors")]
    [InlineData("drivetrain")]
    [InlineData("wheel-size")]
    [InlineData("tire-size")]
    public void OptionalFields_HaveALabelAndNoRequiredMarker(string field)
    {
        var cut = RenderForm();

        Assert.NotNull(cut.Find($"label[for='{field}']"));
        Assert.Empty(cut.FindAll($"label[for='{field}'] span.text-danger"));
    }

    [Fact]
    public void BasicInfoTab_HoldsTheBasicFieldsInOrder()
    {
        var cut = RenderForm(tabbed: true);

        var ids = cut.Find("#tab-basic").QuerySelectorAll("input[id], select[id]").Select(field => field.Id);
        Assert.Equal(
            ["nickname", "vin", "make", "model", "trim", "year", "vehicle-type", "ownership", "purchased-on", "lease-ends-on"],
            ids);
    }

    [Fact]
    public void SpecificationsTab_HoldsTheSpecificationFieldsInOrder()
    {
        var cut = RenderForm(tabbed: true);

        var ids = cut.Find("#tab-specifications").QuerySelectorAll("input[id], select[id]").Select(field => field.Id);
        Assert.Equal(
            ["engine", "horsepower", "fuel-type", "transmission", "mileage", "color", "seats", "doors", "drivetrain", "wheel-size", "tire-size"],
            ids);
    }

    [Fact]
    public void NonTabbedForm_HoldsEveryFieldOnce()
    {
        var cut = RenderForm();

        var ids = cut.FindAll("form input[id], form select[id]").Select(field => field.Id).ToList();
        Assert.Equal(21, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Selects_StartWithNotSetAndListTheLookupItemsInOrder()
    {
        var cut = RenderForm();

        Assert.Equal(["Not set", "Sedan", "Wagon"], cut.FindAll("select#vehicle-type option").Select(option => option.TextContent.Trim()));
        Assert.Equal(["Not set", "Gasoline", "Diesel"], cut.FindAll("select#fuel-type option").Select(option => option.TextContent.Trim()));
        Assert.Equal(["Not set", "Automatic"], cut.FindAll("select#transmission option").Select(option => option.TextContent.Trim()));
        Assert.Equal(["Not set", "FWD", "AWD"], cut.FindAll("select#drivetrain option").Select(option => option.TextContent.Trim()));
        Assert.Equal(["Not set", "Purchased", "Leased"], cut.FindAll("select#ownership option").Select(option => option.TextContent.Trim()));
    }

    [Fact]
    public void Constraints_AreRepeatedOnTheInputs()
    {
        var cut = RenderForm();

        Assert.Equal("17", cut.Find("#vin").GetAttribute("maxlength"));
        Assert.Equal("1900", cut.Find("#year").GetAttribute("min"));
        Assert.Equal("1", cut.Find("#horsepower").GetAttribute("min"));
        Assert.Equal("3000", cut.Find("#horsepower").GetAttribute("max"));
        Assert.Equal("20", cut.Find("#seats").GetAttribute("max"));
        Assert.Equal("10", cut.Find("#wheel-size").GetAttribute("min"));
        Assert.Equal("30", cut.Find("#wheel-size").GetAttribute("max"));
        Assert.Equal("0.1", cut.Find("#wheel-size").GetAttribute("step"));
        Assert.Equal("20", cut.Find("#tire-size").GetAttribute("maxlength"));
        Assert.Equal("date", cut.Find("#purchased-on").GetAttribute("type"));
        Assert.Equal("date", cut.Find("#lease-ends-on").GetAttribute("type"));
        Assert.Contains("Leased vehicles only", cut.Find("#lease-ends-on-help").TextContent);
    }

    [Theory]
    [InlineData("Vin", "vin-error", "tab-basic")]
    [InlineData("LeaseEndsOn", "leaseendson-error", "tab-basic")]
    [InlineData("Horsepower", "horsepower-error", "tab-specifications")]
    [InlineData("WheelSize", "wheelsize-error", "tab-specifications")]
    [InlineData("FuelTypeId", "fueltypeid-error", "tab-specifications")]
    public void ApiErrors_ShowNextToTheFieldAndOpenTheTabHoldingThem(string property, string errorId, string expectedPane)
    {
        var cut = RenderForm(tabbed: true, errors: new Dictionary<string, string[]> { [property] = ["Something is wrong."] });

        Assert.Contains("Something is wrong.", cut.Find($"#{errorId}").TextContent);
        Assert.Equal([expectedPane], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
    }

    [Fact]
    public void FirstErrorTab_WinsWhenBothTabsHaveErrors()
    {
        var cut = RenderForm(tabbed: true, errors: new Dictionary<string, string[]> { ["Horsepower"] = ["bad"], ["Year"] = ["bad"] });

        Assert.Equal(["tab-basic"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
    }

    [Fact]
    public void ExplainsTheRequiredMarker()
    {
        var cut = RenderForm();

        Assert.Contains("* Required", cut.Find("p.form-text").TextContent);
    }

    [Fact]
    public void IsConstrainedToReadableWidth()
    {
        var cut = RenderForm();

        Assert.NotNull(cut.Find(".row > .col-lg-8 form"));
    }

    [Fact]
    public void SubmitComesBeforeCancel()
    {
        var cut = RenderForm();

        var buttons = cut.FindAll("form > div.d-flex > *");
        Assert.Equal("Add vehicle", buttons[0].TextContent.Trim());
        Assert.Contains("btn-primary", buttons[0].ClassName);
        Assert.Equal("Cancel", buttons[1].TextContent.Trim());
        Assert.Contains("btn-outline-secondary", buttons[1].ClassName);
    }
}

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

        var ids = cut.Find("#group-basic").QuerySelectorAll("input[id], select[id]").Select(field => field.Id);
        Assert.Equal(
            ["year", "make", "model", "trim", "vin", "vehicle-type", "ownership", "purchased-on", "lease-ends-on", "mileage"],
            ids);
    }

    [Fact]
    public void SpecificationsTab_HoldsTheSpecificationFieldsInOrder()
    {
        var cut = RenderForm(tabbed: true);

        var ids = cut.Find("#group-specifications").QuerySelectorAll("input[id], select[id]").Select(field => field.Id);
        Assert.Equal(
            ["engine", "horsepower", "drivetrain", "color", "fuel-type", "transmission", "seats", "doors", "wheel-size", "tire-size"],
            ids);
    }

    [Fact]
    public void TheFormHasNoNicknameField()
    {
        var cut = RenderForm();

        Assert.Empty(cut.FindAll("#nickname, label[for='nickname'], [name*='Nickname']"));
    }

    [Fact]
    public void BasicInfoFields_AreGroupedIntoRowsOfThreeThreeThreeAndOne()
    {
        var cut = RenderForm(tabbed: true);

        var rows = cut.Find("#group-basic").QuerySelectorAll("div.row.g-3")
            .Select(row => row.QuerySelectorAll("input[id], select[id]").Select(field => field.Id).ToList())
            .ToList();
        Assert.Equal(
            [["year", "make", "model"], ["trim", "vin", "vehicle-type"], ["ownership", "purchased-on", "lease-ends-on"], ["mileage"]],
            rows);
        Assert.All(
            cut.Find("#group-basic").QuerySelectorAll("div.row.g-3 > div"),
            column => Assert.Contains("col-sm-4", column.ClassList));
    }

    [Fact]
    public void SpecificationFields_AreGroupedIntoRowsOfThreeThreeAndFour()
    {
        var cut = RenderForm(tabbed: true);

        var rows = cut.Find("#group-specifications").QuerySelectorAll("div.row.g-3").ToList();
        Assert.Equal(
            [["engine", "horsepower", "drivetrain"], ["color", "fuel-type", "transmission"], ["seats", "doors", "wheel-size", "tire-size"]],
            rows.Select(row => row.QuerySelectorAll("input[id], select[id]").Select(field => field.Id).ToList()).ToList());
        Assert.All(rows[0].Children.Concat(rows[1].Children), column => Assert.Contains("col-sm-4", column.ClassList));
        Assert.All(rows[2].Children, column =>
        {
            Assert.Contains("col-6", column.ClassList);
            Assert.Contains("col-sm-3", column.ClassList);
        });
    }

    [Fact]
    public void NonTabbedForm_HoldsEveryFieldOnce()
    {
        var cut = RenderForm();

        var ids = cut.FindAll("form input[id], form select[id]").Select(field => field.Id).ToList();
        Assert.Equal(20, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OwnershipType.Purchased)]
    public void LeaseEndField_IsHiddenUnlessTheVehicleIsLeased(OwnershipType? ownership)
    {
        var cut = RenderForm(model: new VehicleFormModel { Ownership = ownership });

        Assert.True(cut.Find("#lease-ends-field").HasAttribute("hidden"));
    }

    [Fact]
    public void LeaseEndField_IsShownWhenTheVehicleIsLeased()
    {
        var cut = RenderForm(model: new VehicleFormModel { Ownership = OwnershipType.Leased });

        var field = cut.Find("#lease-ends-field");
        Assert.False(field.HasAttribute("hidden"));
        Assert.NotNull(field.QuerySelector("input#lease-ends-on"));
        Assert.NotNull(field.QuerySelector("label[for='lease-ends-on']"));
    }

    [Fact]
    public void LeaseEndField_ContainsTheDateAndItsLabelOnly_WithNoLeasedOnlyHelpText()
    {
        var cut = RenderForm(model: new VehicleFormModel { Ownership = OwnershipType.Leased });

        Assert.Equal("Date lease ends", cut.Find("#lease-ends-field").TextContent.Trim());
        Assert.DoesNotContain("Leased vehicles only", cut.Markup);
        Assert.Empty(cut.FindAll("#lease-ends-on-help"));
    }

    [Fact]
    public void OwnershipSelect_NamesTheLeaseFieldItControls()
    {
        var cut = RenderForm();

        var target = cut.Find("select#ownership").GetAttribute("data-lease-source");
        Assert.Equal("lease-ends-field", target);
        Assert.NotNull(cut.Find($"#{target}"));
    }

    [Fact]
    public void OwnershipPurchaseDateAndLeaseEndShareOneRowOfThreeColumns()
    {
        var cut = RenderForm(tabbed: true);

        var row = cut.Find("select#ownership").Closest("div.row")!;
        Assert.Same(row, cut.Find("input#purchased-on").Closest("div.row"));
        Assert.Same(row, cut.Find("input#lease-ends-on").Closest("div.row"));
        Assert.Equal(3, row.Children.Length);
        Assert.Same(row, cut.Find("#lease-ends-field").ParentElement);
    }

    [Fact]
    public void Mileage_SitsOnItsOwnRowOnTheBasicInfoTabAndNotOnSpecifications()
    {
        var cut = RenderForm(tabbed: true);

        var row = cut.Find("#group-basic input#mileage").Closest("div.row")!;
        Assert.Single(row.Children);
        Assert.Empty(cut.Find("#group-specifications").QuerySelectorAll("#mileage, label[for='mileage']"));
    }

    [Theory]
    [InlineData(null, "Date purchased or leased", "Mileage on Date purchased or leased")]
    [InlineData(OwnershipType.Purchased, "Date Purchased", "Mileage on Purchase Date")]
    [InlineData(OwnershipType.Leased, "Date Leased", "Mileage on Lease Date")]
    public void DateAndMileageLabels_FollowTheOwnership(OwnershipType? ownership, string dateLabel, string mileageLabel)
    {
        var cut = RenderForm(model: new VehicleFormModel { Ownership = ownership });

        Assert.Equal(dateLabel, cut.Find("label[for='purchased-on']").TextContent.Trim());
        Assert.Equal(mileageLabel, cut.Find("label[for='mileage']").TextContent.Trim());
    }

    [Fact]
    public void DateAndMileageLabels_CarryEveryVariantForTheOwnershipScript()
    {
        var cut = RenderForm();

        var date = cut.Find("label[for='purchased-on']");
        Assert.True(date.HasAttribute("data-ownership-text"));
        Assert.Equal("Date Purchased", date.GetAttribute("data-purchased"));
        Assert.Equal("Date Leased", date.GetAttribute("data-leased"));
        Assert.Equal("Date purchased or leased", date.GetAttribute("data-unset"));

        var mileage = cut.Find("label[for='mileage']");
        Assert.True(mileage.HasAttribute("data-ownership-text"));
        Assert.Equal("Mileage on Purchase Date", mileage.GetAttribute("data-purchased"));
        Assert.Equal("Mileage on Lease Date", mileage.GetAttribute("data-leased"));
        Assert.Equal("Mileage on Date purchased or leased", mileage.GetAttribute("data-unset"));
    }

    [Fact]
    public void ARejectedLeaseEnd_KeepsTheFieldVisibleForALeasedVehicle()
    {
        var cut = RenderForm(
            errors: new Dictionary<string, string[]> { ["LeaseEndsOn"] = ["Lease end must be after the purchase date."] },
            model: new VehicleFormModel { Ownership = OwnershipType.Leased });

        Assert.False(cut.Find("#lease-ends-field").HasAttribute("hidden"));
        Assert.Contains("Lease end must be after", cut.Find("#leaseendson-error").TextContent);
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
    }

    [Theory]
    [InlineData("Vin", "vin-error", "tab-details")]
    [InlineData("LeaseEndsOn", "leaseendson-error", "tab-details")]
    [InlineData("Mileage", "mileage-error", "tab-details")]
    [InlineData("Horsepower", "horsepower-error", "tab-details")]
    [InlineData("WheelSize", "wheelsize-error", "tab-details")]
    [InlineData("FuelTypeId", "fueltypeid-error", "tab-details")]
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

        Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
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

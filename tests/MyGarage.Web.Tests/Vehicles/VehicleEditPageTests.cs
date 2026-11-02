using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleEditPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleEdit> RenderEdit() =>
        Render<VehicleEdit>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

    [Fact]
    public void Form_IsPrefilledWithTheCurrentDetails()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("input#nickname"));
            Assert.Equal("2020", cut.Find("input#year").GetAttribute("value"));
            Assert.Equal("Ford", cut.Find("input#make").GetAttribute("value"));
            Assert.Equal("F-150", cut.Find("input#model").GetAttribute("value"));
            Assert.Equal("XLT", cut.Find("input#trim").GetAttribute("value"));
            Assert.Equal("Blue", cut.Find("input#color").GetAttribute("value"));
            Assert.Equal("42000", cut.Find("input#mileage").GetAttribute("value"));
            Assert.Equal("4", cut.Find("input#doors").GetAttribute("value"));
        });
    }

    [Fact]
    public void Form_IsPrefilledWithEveryNewDetail()
    {
        var detail = FullDetail(vehicleId);
        Lookups = new MyGarage.Shared.Lookups.LookupsDto
        {
            VehicleTypes = [Item("Sedan"), new() { Id = detail.VehicleType!.Id, Name = "Wagon", IsDefault = true, IsHidden = false }],
            FuelTypes = [new() { Id = detail.FuelType!.Id, Name = "Gasoline", IsDefault = true, IsHidden = false }],
            Transmissions = [new() { Id = detail.Transmission!.Id, Name = "Automatic", IsDefault = true, IsHidden = false }],
            Drivetrains = [new() { Id = detail.Drivetrain!.Id, Name = "AWD", IsDefault = true, IsHidden = false }],
        };
        RegisterApi(_ => Json(detail));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("1HGCM82633A004352", cut.Find("input#vin").GetAttribute("value"));
            Assert.Equal("2019", cut.Find("input#year").GetAttribute("value"));
            Assert.Equal("2024-03-15", cut.Find("input#purchased-on").GetAttribute("value"));
            Assert.Equal("2027-03-14", cut.Find("input#lease-ends-on").GetAttribute("value"));
            Assert.Equal("2.5L flat-4", cut.Find("input#engine").GetAttribute("value"));
            Assert.Equal("182", cut.Find("input#horsepower").GetAttribute("value"));
            Assert.Equal("5", cut.Find("input#seats").GetAttribute("value"));
            Assert.Equal("17.5", cut.Find("input#wheel-size").GetAttribute("value"));
            Assert.Equal("225/60R17", cut.Find("input#tire-size").GetAttribute("value"));
            Assert.Equal(detail.VehicleType!.Id.ToString(), cut.Find("select#vehicle-type").GetAttribute("value"));
            Assert.Equal(detail.FuelType!.Id.ToString(), cut.Find("select#fuel-type").GetAttribute("value"));
            Assert.Equal(detail.Transmission!.Id.ToString(), cut.Find("select#transmission").GetAttribute("value"));
            Assert.Equal(detail.Drivetrain!.Id.ToString(), cut.Find("select#drivetrain").GetAttribute("value"));
            Assert.Equal("Leased", cut.Find("select#ownership").GetAttribute("value"));
            Assert.False(cut.Find("#lease-ends-field").HasAttribute("hidden"));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OwnershipType.Purchased)]
    public void LeaseEndField_IsHiddenWhenTheStoredVehicleIsNotLeased(OwnershipType? ownership)
    {
        var basis = FullDetail(vehicleId);
        RegisterApi(_ => Json(new VehicleDetailDto
        {
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Year = basis.Year, Ownership = ownership,
            Tags = [], Images = [], CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
        }));

        var cut = RenderEdit();

        cut.WaitForAssertion(() => Assert.True(cut.Find("#lease-ends-field").HasAttribute("hidden")));
    }

    [Fact]
    public void HiddenCurrentItem_StaysSelectedAndIsMarkedHidden()
    {
        var detail = FullDetail(vehicleId);
        Lookups = new MyGarage.Shared.Lookups.LookupsDto
        {
            VehicleTypes = [Item("Sedan")],
            FuelTypes = [],
            Transmissions = [],
            Drivetrains = [],
        };
        RegisterApi(_ => Json(detail));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll("select#vehicle-type option").Select(option => option.TextContent.Trim()).ToList();
            Assert.Equal(["Not set", "Sedan", "Wagon (hidden)"], options);
            Assert.Equal(detail.VehicleType!.Id.ToString(), cut.Find("select#vehicle-type").GetAttribute("value"));
            Assert.Contains("Gasoline (hidden)", cut.Find("select#fuel-type").TextContent);
        });
    }

    [Fact]
    public void LegacyVehicle_OpensWithAnEmptyRequiredYear()
    {
        RegisterApi(_ => Json(Detail(vehicleId, year: null)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var year = cut.Find("input#year");
            Assert.True(string.IsNullOrEmpty(year.GetAttribute("value")));
            Assert.Equal("true", year.GetAttribute("aria-required"));
            Assert.Equal("", cut.Find("select#vehicle-type").GetAttribute("value") ?? "");
        });
    }

    [Fact]
    public void SavingALegacyVehicleWithoutAYear_ShowsTheYearErrorOnTheBasicInfoTab()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Year", "Year is required."))
            : Json(Detail(vehicleId, year: null)));
        NavigateTo($"/vehicles/{vehicleId}/edit?tab=specifications");
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));
        cut.Find("input#engine").Change("5.0L V8");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Year is required.", cut.Find("#year-error").TextContent);
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Equal("5.0L V8", cut.Find("input#engine").GetAttribute("value"));
        });
    }

    [Fact]
    public void ARejectedMileageOpensTheBasicInfoTabEvenWhenSavingFromSpecifications()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Mileage", "Mileage must be between 0 and 9,999,999."))
            : Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}/edit?tab=specifications");
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mileage must be between", cut.Find("#group-basic #mileage-error").TextContent);
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Fact]
    public void ShowsTrailAndTitleWithTheComputedVehicleName()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("Edit 2020 Ford F-150", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150", "Edit"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}", items[2].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("page", items[3].GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void UnknownVehicle_ShowsStandardNotFoundHeader()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void TabsSitOutsideTheCard_AndOnlyTheTabContentIsInIt_WithIcons()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.Find("ul.mg-tabs");
            Assert.Null(tabs.Closest(".card"));
            Assert.Equal(3, tabs.QuerySelectorAll("button svg.bi[aria-hidden='true']").Length);
            Assert.NotNull(cut.Find("#group-basic").Closest(".card"));
            Assert.NotNull(cut.Find("#tab-media").Closest(".card"));
        });
    }

    [Fact]
    public void ShowsThreeTabsWithDetailsActiveByDefault()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Details", "Media", "Description"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["true", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
            Assert.Contains("active", tabs[0].ClassList);
            Assert.Equal(["tab-details", "tab-media", "tab-description"],
                tabs.Select(tab => tab.GetAttribute("data-bs-target")!.TrimStart('#')));
            Assert.Contains("show", cut.Find("#tab-details").ClassList);
            Assert.Contains("active", cut.Find("#tab-details").ClassList);
            Assert.DoesNotContain("active", cut.Find("#tab-media").ClassList);
        });
    }

    [Theory]
    [InlineData("details", "tab-details")]
    [InlineData("media", "tab-media")]
    [InlineData("description", "tab-description")]
    [InlineData("zzz", "tab-details")]
    public void TabQuery_SelectsTheActiveTabAndIgnoresUnknownValues(string tab, string expectedPane)
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}/edit?tab={tab}");

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var active = cut.FindAll(".tab-pane.active").Select(pane => pane.Id).ToList();
            Assert.Equal([expectedPane], active);
            Assert.Equal("true", cut.Find($"button[data-bs-target='#{expectedPane}']").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void BasicInfoAndSpecificationsHoldTheVehicleFieldsInOneForm()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#group-basic");
            foreach (var field in new[] { "year", "make", "model", "trim", "vin", "vehicle-type", "ownership", "purchased-on", "lease-ends-on", "mileage" })
            {
                Assert.NotNull(basic.QuerySelector($"#{field}"));
            }

            var specifications = cut.Find("#group-specifications");
            foreach (var field in new[] { "engine", "horsepower", "drivetrain", "color", "fuel-type", "transmission", "seats", "doors", "wheel-size", "tire-size" })
            {
                Assert.NotNull(specifications.QuerySelector($"#{field}"));
            }

            Assert.Empty(specifications.QuerySelectorAll("#mileage"));
            var form = cut.Find("form");
            Assert.Empty(form.QuerySelectorAll("input#nickname"));
            Assert.NotNull(form.QuerySelector("input#year"));
            Assert.NotNull(form.QuerySelector("input#tire-size"));
            Assert.Single(cut.FindAll("#tab-details button[type='submit']"));
            Assert.Equal("Save changes", cut.Find("#tab-details button[type='submit']").TextContent.Trim());
        });
    }

    [Fact]
    public void MediaTabHoldsThePictureGallery()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var media = cut.Find("#tab-media");
            Assert.NotNull(media.QuerySelector("section[aria-labelledby='gallery-heading']"));
            Assert.NotNull(media.QuerySelector("form[data-testid='upload-image'][data-dropzone]"));
            Assert.NotNull(media.QuerySelector("h3[data-testid='uploaded-heading']"));
        });
    }

    [Fact]
    public void DescriptionTabHoldsFeaturesThenTheJournalEachInAHalfWidthColumn()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("#tab-description div.row.g-4");
            var columns = row.Children.ToList();
            Assert.Equal(2, columns.Count);
            Assert.All(columns, column => Assert.Contains("col-lg-6", column.ClassList));
            Assert.NotNull(columns[0].QuerySelector("section[aria-labelledby='tags-heading']"));
            Assert.NotNull(columns[1].QuerySelector("section[aria-labelledby='journal-heading']"));
            var sections = cut.Find("#tab-description").QuerySelectorAll("section").Select(section => section.GetAttribute("aria-labelledby")).ToList();
            Assert.Equal(["tags-heading", "journal-heading"], sections);
        });
    }

    [Fact]
    public void SectionsReturnToTheirOwnTabOnTheEditPage()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("edit", cut.Find("#tab-media input[name='returnTo']").GetAttribute("value"));
            Assert.NotNull(cut.Find("#tab-description form[data-testid='add-tag']"));
        });
    }

    [Fact]
    public void UploadStatusQuery_ShowsTheMessageOnTheMediaTab()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}/edit?tab=media&imageStatus=uploaded");

        var cut = RenderEdit();

        cut.WaitForAssertion(() => Assert.Contains("Picture added.", cut.Find("#tab-media [data-testid='image-notice']").TextContent));
    }

    [Fact]
    public void HeaderOffersABackToVehicleLink()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var back = cut.Find("h1").ParentElement!.QuerySelector("a.btn")!;
            Assert.Equal("Back to vehicle", back.TextContent.Trim());
            Assert.Equal("Back to the vehicle details", back.GetAttribute("title"));
            Assert.Equal("true", back.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
            Assert.Equal($"/vehicles/{vehicleId}", back.GetAttribute("href"));
        });
    }

    [Fact]
    public void ErrorOnASpecificationField_OpensTheSpecificationsTab()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Doors", "Doors must be between 1 and 6."))
            : Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Contains("Doors must be between 1 and 6.", cut.Find("#doors-error").TextContent);
        });
    }

    [Fact]
    public void ErrorOnABasicField_StaysOnBasicInfo()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Year", "Year is required."), ("Doors", "Doors must be between 1 and 6."))
            : Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id)));
    }

    [Fact]
    public void CancelLink_ReturnsToTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
            Assert.Equal($"/vehicles/{vehicleId}", cut.Find("a.btn-outline-secondary").GetAttribute("href")));
    }

    [Fact]
    public void ApiValidationErrors_AreShownPerField()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Doors", "Doors must be between 1 and 6."))
            : Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Doors must be between 1 and 6.", cut.Find("#doors-error").TextContent));
    }

    [Fact]
    public void Success_PutsTheFormValuesAndRedirectsToTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));
        cut.Find("input#make").Change("Renamed");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}", Services.GetRequiredService<NavigationManager>().Uri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void UnknownVehicle_ShowsNotFound()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.NotFound, cut.Markup);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));

        var cut = RenderEdit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }
}

using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily", cut.Find("input#nickname").GetAttribute("value"));
            Assert.Equal("Ford", cut.Find("input#make").GetAttribute("value"));
            Assert.Equal("F-150", cut.Find("input#model").GetAttribute("value"));
            Assert.Equal("XLT", cut.Find("input#trim").GetAttribute("value"));
            Assert.Equal("Blue", cut.Find("input#color").GetAttribute("value"));
            Assert.Equal("42000", cut.Find("input#mileage").GetAttribute("value"));
            Assert.Equal("4", cut.Find("input#doors").GetAttribute("value"));
        });
    }

    [Fact]
    public void ShowsTrailAndTitleWithTheStoredNickname()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("Edit Daily", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "Daily", "Edit"], items.Select(item => item.TextContent.Trim()));
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
            Assert.Equal(4, tabs.QuerySelectorAll("button svg.bi[aria-hidden='true']").Length);
            Assert.NotNull(cut.Find("#tab-basic").Closest(".card"));
            Assert.NotNull(cut.Find("#tab-media").Closest(".card"));
        });
    }

    [Fact]
    public void ShowsFourTabsWithBasicInfoActiveByDefault()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Basic Info", "Specifications", "Media", "Description"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["true", "false", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
            Assert.Contains("active", tabs[0].ClassList);
            Assert.Equal(["tab-basic", "tab-specifications", "tab-media", "tab-description"],
                tabs.Select(tab => tab.GetAttribute("data-bs-target")!.TrimStart('#')));
            Assert.Contains("show", cut.Find("#tab-basic").ClassList);
            Assert.Contains("active", cut.Find("#tab-basic").ClassList);
            Assert.DoesNotContain("active", cut.Find("#tab-media").ClassList);
        });
    }

    [Theory]
    [InlineData("specifications", "tab-specifications")]
    [InlineData("media", "tab-media")]
    [InlineData("description", "tab-description")]
    [InlineData("zzz", "tab-basic")]
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
            var basic = cut.Find("#tab-basic");
            foreach (var field in new[] { "nickname", "make", "model", "trim" })
            {
                Assert.NotNull(basic.QuerySelector($"input#{field}"));
            }

            var specifications = cut.Find("#tab-specifications");
            foreach (var field in new[] { "color", "mileage", "doors" })
            {
                Assert.NotNull(specifications.QuerySelector($"input#{field}"));
            }

            var form = cut.Find("form");
            Assert.NotNull(form.QuerySelector("input#nickname"));
            Assert.NotNull(form.QuerySelector("input#doors"));
            Assert.Equal("Save changes", cut.Find("#tab-basic button[type='submit']").TextContent.Trim());
            Assert.Equal("Save changes", cut.Find("#tab-specifications button[type='submit']").TextContent.Trim());
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
            Assert.NotNull(media.QuerySelector("form[data-testid='upload-image']"));
        });
    }

    [Fact]
    public void DescriptionTabHoldsFeatureTagsThenTheJournal()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderEdit();

        cut.WaitForAssertion(() =>
        {
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
            Assert.Equal(["tab-specifications"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
            Assert.Contains("Doors must be between 1 and 6.", cut.Find("#doors-error").TextContent);
        });
    }

    [Fact]
    public void ErrorOnABasicField_StaysOnBasicInfo()
    {
        RegisterApi(request => request.Method == HttpMethod.Put
            ? ValidationProblem(("Nickname", "Nickname is required."), ("Doors", "Doors must be between 1 and 6."))
            : Json(Detail(vehicleId)));
        var cut = RenderEdit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(["tab-basic"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id)));
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
        cut.Find("input#nickname").Change("Renamed");

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

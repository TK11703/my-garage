using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleDetailPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleDetail> RenderDetail() =>
        Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

    private static VehicleDetailDto DetailWith(Guid id, IReadOnlyList<VehicleTagDto> tags, IReadOnlyList<VehicleImageDto> images)
    {
        var basis = Detail(id);
        return new VehicleDetailDto
        {
            Id = basis.Id, Nickname = basis.Nickname, Make = basis.Make, Model = basis.Model, Trim = basis.Trim, Color = basis.Color,
            Mileage = basis.Mileage, Doors = basis.Doors, Tags = tags, Images = images, CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
        };
    }

    [Fact]
    public void ShowsEveryDetail()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily", cut.Find("h1").TextContent);
            Assert.Contains(">Ford<", cut.Markup);
            Assert.Contains(">F-150<", cut.Markup);
            Assert.Contains("XLT", cut.Markup);
            Assert.Contains("Blue", cut.Markup);
            Assert.Contains("42,000 mi", cut.Markup);
            Assert.Contains(">4<", cut.Markup);
        });
    }

    [Fact]
    public void TabsSitOutsideTheCard_AndOnlyTheTabContentIsInIt_WithIcons()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.Find("ul.mg-tabs");
            Assert.Null(tabs.Closest(".card"));
            Assert.Equal(4, tabs.QuerySelectorAll("button svg.bi[aria-hidden='true']").Length);
            Assert.NotNull(cut.Find("#tab-basic").Closest(".card"));
            Assert.NotNull(cut.Find("#tab-description").Closest(".card"));
        });
    }

    [Fact]
    public void NothingSitsBetweenTheHeaderAndTheTabs()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find("h1").ParentElement!;
            Assert.Equal("ul", header.NextElementSibling!.TagName.ToLowerInvariant());
            Assert.Contains("mg-tabs", header.NextElementSibling.ClassList);
        });
    }

    [Fact]
    public void ShowsFourTabsWithBasicInfoActiveByDefault()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Basic Info", "Specifications", "Media", "Description"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["true", "false", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
            Assert.Equal(["tab-basic"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
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
        NavigateTo($"/vehicles/{vehicleId}?tab={tab}");

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Equal([expectedPane], cut.FindAll(".tab-pane.active").Select(pane => pane.Id)));
    }

    [Fact]
    public void BasicInfoAndSpecificationsShowTheirValues()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#tab-basic");
            Assert.Equal(["Nickname", "Make", "Model", "Trim"], basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(["Daily", "Ford", "F-150", "XLT"], basic.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));

            var specifications = cut.Find("#tab-specifications");
            Assert.Equal(["Color", "Mileage", "Doors"], specifications.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(["Blue", "42,000 mi", "4"], specifications.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
        });
    }

    [Fact]
    public void DescriptionTabShowsFeatureTagsThenTheJournal()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var sections = cut.Find("#tab-description").QuerySelectorAll("section").Select(section => section.GetAttribute("aria-labelledby")).ToList();
            Assert.Equal(["tags-heading", "journal-heading"], sections);
        });
    }

    [Fact]
    public void MediaTabShowsThePicturesReadOnly()
    {
        var main = new VehicleImageDto { Id = Guid.NewGuid(), IsMain = true, Caption = "Front view", ContentType = "image/png", SizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow };
        var other = new VehicleImageDto { Id = Guid.NewGuid(), IsMain = false, ContentType = "image/png", SizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow };
        RegisterApi(_ => Json(DetailWith(vehicleId, [], [main, other])));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var media = cut.Find("#tab-media");
            var tiles = media.QuerySelectorAll("[data-testid='image']").ToList();
            Assert.Equal(2, tiles.Count);
            Assert.Equal($"/vehicles/{vehicleId}/images/{main.Id}", tiles[0].QuerySelector("img")!.GetAttribute("src"));
            Assert.Equal("Front view", tiles[0].QuerySelector("img")!.GetAttribute("alt"));
            Assert.NotNull(tiles[0].QuerySelector("[data-testid='main-badge']"));
            Assert.Null(tiles[1].QuerySelector("[data-testid='main-badge']"));
            Assert.Empty(media.QuerySelectorAll("form, button, input"));
        });
    }

    [Fact]
    public void TagsAreShownAsBadgesWithoutRemoveButtons()
    {
        var tags = new[] { new VehicleTagDto { Id = Guid.NewGuid(), Name = "sunroof" }, new VehicleTagDto { Id = Guid.NewGuid(), Name = "<b>tow</b>" } };
        RegisterApi(_ => Json(DetailWith(vehicleId, tags, [])));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var badges = cut.FindAll("#tab-description [data-testid='tag']");
            Assert.Equal(["sunroof", "<b>tow</b>"], badges.Select(badge => badge.TextContent.Trim()));
            Assert.Empty(cut.FindAll("#tab-description [data-testid='tag'] button"));
            Assert.Empty(cut.FindAll("#tab-description b"));
        });
    }

    [Fact]
    public void JournalNotesAreListedReadOnly()
    {
        Notes = NotesPage([Note("Changed the oil", "Maintenance"), Note("Plain note")]);
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var notes = cut.FindAll("#tab-description [data-testid='note']");
            Assert.Equal(2, notes.Count);
            Assert.Contains("Changed the oil", notes[0].TextContent);
            Assert.Equal("Maintenance", notes[0].QuerySelector("[data-testid='note-category']")!.TextContent.Trim());
            Assert.Contains("Oct 31, 2026", notes[0].TextContent);
            Assert.Null(notes[1].QuerySelector("[data-testid='note-category']"));
        });
    }

    [Fact]
    public void EmptySectionsPointToTheEditPage()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}/edit?tab=media", cut.Find("#tab-media a").GetAttribute("href"));
            var description = cut.FindAll("#tab-description a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Equal([$"/vehicles/{vehicleId}/edit?tab=description", $"/vehicles/{vehicleId}/edit?tab=description"], description);
        });
    }

    [Fact]
    public void TheViewPageOffersNoEditingControlsExceptDelete()
    {
        Notes = NotesPage([Note("A note")]);
        RegisterApi(_ => Json(DetailWith(vehicleId, [new VehicleTagDto { Id = Guid.NewGuid(), Name = "sunroof" }], [])));

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Contains("A note", cut.Markup));
        var forms = cut.FindAll("form");
        Assert.Single(forms);
        Assert.NotNull(cut.Find("details[data-testid='delete-vehicle'] form"));
        Assert.Empty(cut.FindAll("input:not([type='hidden']), textarea, select, input[type='file']"));
    }

    [Fact]
    public void JournalPagerKeepsTheDescriptionTab()
    {
        Notes = NotesPage([Note("n")], page: 2, totalCount: 25);
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}?tab=description&notesPage=2");

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains($"/vehicles/{vehicleId}?tab=description", links);
            Assert.Contains($"/vehicles/{vehicleId}?tab=description&notesPage=3", links);
        });
    }

    [Fact]
    public void ShowsTheTagsAndJournalSections()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("section[aria-labelledby='tags-heading']"));
            Assert.NotNull(cut.Find("section[aria-labelledby='journal-heading']"));
        });
    }

    [Theory]
    [InlineData("2", "page=2")]
    [InlineData("abc", "page=1")]
    [InlineData("-3", "page=1")]
    public void NotesPageQuery_SelectsThePageOfNotes(string value, string expectedQuery)
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}?notesPage={value}");

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Contains(expectedQuery, Requests.Single(request => request.RequestUri!.AbsolutePath.EndsWith("/notes", StringComparison.Ordinal)).RequestUri!.Query));
    }

    [Fact]
    public void MissingOptionalValues_ShowNotSet()
    {
        var bare = Detail(vehicleId);
        RegisterApi(_ => Json(new MyGarage.Shared.Vehicles.VehicleDetailDto
        {
            Id = bare.Id, Nickname = "Bare", Make = "Ford", Model = "F-150", Tags = [], Images = [],
            CreatedAt = bare.CreatedAt, UpdatedAt = bare.UpdatedAt,
        }));

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.True(cut.FindAll("dd").Count(dd => dd.TextContent == "Not set") >= 3));
    }

    [Fact]
    public void HasEditLinkAndBackLink()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}/edit", cut.Find("a[data-testid='edit-vehicle']").GetAttribute("href"));
            Assert.Equal("/vehicles", cut.Find("nav[aria-label='Breadcrumb'] a[href='/vehicles']").GetAttribute("href"));
        });
    }

    [Fact]
    public void ShowsTrailWithTheVehicleNameAndEditActionInTheHeader()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "Daily"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal("/vehicles", items[1].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("page", items[2].GetAttribute("aria-current"));
            Assert.NotNull(cut.Find("h1").ParentElement!.QuerySelector("a[data-testid='edit-vehicle']"));
            Assert.DoesNotContain("My garage", cut.Markup);
        });
    }

    [Fact]
    public void UnknownVehicle_ShowsStandardNotFoundHeader()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles"], items.Select(item => item.TextContent.Trim()));
        });
    }

    [Fact]
    public void ApiFailure_ShowsGenericVehicleTitle()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle", cut.Find("h1").TextContent);
            Assert.Equal("Vehicles", cut.FindAll("nav[aria-label='Breadcrumb'] li").Last().TextContent.Trim());
        });
    }

    [Fact]
    public void DeleteConfirmation_UsesTheStandardWording()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var confirmation = cut.Find("details[data-testid='delete-vehicle']");
            Assert.Equal("Yes, delete this vehicle", confirmation.QuerySelector("button")!.TextContent.Trim());
            Assert.Equal("Keep vehicle", confirmation.QuerySelector("a.btn")!.TextContent.Trim());
        });
    }

    [Fact]
    public void DeleteSitsInTheHeaderNextToEdit_WithIconsAndTitles()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("h1").ParentElement!;
            var edit = row.QuerySelector("a[data-testid='edit-vehicle']")!;
            var delete = row.QuerySelector("details[data-testid='delete-vehicle']")!;
            Assert.Equal("Edit this vehicle", edit.GetAttribute("title"));
            Assert.Equal("Edit", edit.TextContent.Trim());
            Assert.Equal("true", edit.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
            var summary = delete.QuerySelector("summary")!;
            Assert.Equal("Delete this vehicle", summary.GetAttribute("title"));
            Assert.Equal("Delete", summary.TextContent.Trim());
            Assert.Equal("true", summary.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
            Assert.Single(cut.FindAll("details[data-testid='delete-vehicle']"));
        });
    }

    [Fact]
    public void UserText_IsEncoded()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "<img src=x onerror=alert(1)>")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Equal("<img src=x onerror=alert(1)>", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("h1 img"));
    }

    [Fact]
    public void UnknownVehicle_ShowsNotFoundWithBackLink()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.NotFound, cut.Markup);
            Assert.Equal("/vehicles", cut.Find("a[href='/vehicles']").GetAttribute("href"));
        });
    }

    [Fact]
    public void ApiUnavailable_ShowsFriendlyMessage()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.Unavailable, cut.Markup);
            Assert.DoesNotContain("boom", cut.Markup);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }

    [Fact]
    public void Delete_AsksForConfirmationNamingTheVehicleAndItsContents()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("details[data-testid='delete-vehicle']");
            Assert.False(section.HasAttribute("open"));
            Assert.Contains("Delete Daily?", section.TextContent);
            Assert.Contains("tags, journal notes, and pictures", section.TextContent);
            Assert.Equal($"/vehicles/{vehicleId}", cut.Find("details[data-testid='delete-vehicle'] a").GetAttribute("href"));
        });
    }

    [Fact]
    public void Delete_NothingIsSentUntilConfirmed()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='delete-vehicle']")));

        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Delete_Confirmed_SendsDeleteAndReturnsToTheGarage()
    {
        RegisterApi(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(Detail(vehicleId)));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='delete-vehicle'] form")));

        cut.Find("details[data-testid='delete-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/vehicles", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}", delete.RequestUri!.AbsolutePath);
    }
}

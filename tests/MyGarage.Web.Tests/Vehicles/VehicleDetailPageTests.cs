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
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Trim = basis.Trim, Color = basis.Color, Year = basis.Year,
            Mileage = basis.Mileage, Doors = basis.Doors, Tags = tags, Images = images, CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
        };
    }

    [Fact]
    public void ShowsEveryDetail()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2020 Ford F-150", cut.Find("h1").TextContent);
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
            Assert.NotNull(cut.Find("#group-basic").Closest(".card"));
            Assert.NotNull(cut.Find("#tab-description section.card"));
        });
    }

    [Fact]
    public void NothingSitsBetweenTheHeaderAndTheTabs()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find("h1").ParentElement!;
            Assert.Equal("ul", header.NextElementSibling!.TagName.ToLowerInvariant());
            Assert.Contains("mg-tabs", header.NextElementSibling.ClassList);
        });
    }

    [Fact]
    public void TheDeleteConfirmation_MentionsServiceRecords()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
            Assert.Contains(
                "This also removes its tags, journal notes, service records, charging and fueling entries, inspections, registrations, insurance, taxes, pictures, and kept documents and cannot be undone.",
                cut.Find("[data-testid='delete-vehicle']").TextContent));
    }

    [Fact]
    public void ShowsFourTabsWithDetailsActiveByDefault()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Details", "Media", "Description", "Maintenance"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["true", "false", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
            Assert.Equal(["tab-details"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Theory]
    [InlineData("details", "tab-details")]
    [InlineData("media", "tab-media")]
    [InlineData("description", "tab-description")]
    [InlineData("service-records", "tab-service-records")]
    [InlineData("zzz", "tab-details")]
    public void TabQuery_SelectsTheActiveTabAndIgnoresUnknownValues(string tab, string expectedPane)
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}?tab={tab}");

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Equal([expectedPane], cut.FindAll(".tab-pane.active").Select(pane => pane.Id)));
    }

    [Fact]
    public void ARecordSavedFromAnEmailDraft_IsConfirmedAtTheTopOfTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}?tab=service-records&saved=draft");

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='draft-saved']");
            Assert.Equal("status", alert.GetAttribute("role"));
            Assert.Contains("saved from your email draft", alert.TextContent);
        });
    }

    [Fact]
    public void WithoutTheSavedMarker_NoConfirmationIsShown()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        NavigateTo($"/vehicles/{vehicleId}?saved=other");

        var cut = RenderDetail();

        cut.WaitForElement(".tab-content");
        Assert.Empty(cut.FindAll("[data-testid='draft-saved']"));
    }

    [Fact]
    public void BasicInfoAndSpecificationsShowTheirValues()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#group-basic");
            Assert.Equal(
                ["Year", "Make", "Model", "Trim", "VIN", "Vehicle type", "Purchased or leased", "Date purchased or leased", "Mileage on Date purchased or leased", "Latest odometer reading"],
                basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(
                ["2020", "Ford", "F-150", "XLT", "Not set", "Not set", "Not set", "Not set", "42,000 mi", "Not set"],
                basic.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));

            var specifications = cut.Find("#group-specifications");
            Assert.Equal(
                ["Engine", "Horsepower", "Drivetrain", "Color", "Fuel type", "Transmission", "Seats", "Doors", "Wheel size", "Tire size"],
                specifications.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(
                ["Not set", "Not set", "Not set", "Blue", "Not set", "Not set", "Not set", "4", "Not set", "Not set"],
                specifications.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
        });
    }

    [Theory]
    [InlineData(null, "Date purchased or leased", "Mileage on Date purchased or leased")]
    [InlineData(OwnershipType.Purchased, "Date Purchased", "Mileage on Purchase Date")]
    [InlineData(OwnershipType.Leased, "Date Leased", "Mileage on Lease Date")]
    public void DateAndMileageLabels_FollowTheOwnership(OwnershipType? ownership, string dateLabel, string mileageLabel)
    {
        var basis = Detail(vehicleId);
        RegisterApi(_ => Json(new VehicleDetailDto
        {
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Year = basis.Year, Mileage = 42_000, Ownership = ownership,
            PurchasedOn = new DateOnly(2024, 3, 15), Tags = [], Images = [], CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
        }));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#group-basic");
            var labels = basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
            var values = basic.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()).ToList();
            Assert.Equal(dateLabel, labels[7]);
            Assert.Equal("Mar 15, 2024", values[7]);
            Assert.Equal("42,000 mi", values[labels.IndexOf(mileageLabel)]);
            Assert.Contains(mileageLabel, labels);
            Assert.DoesNotContain(cut.Find("#group-specifications").QuerySelectorAll("dt"), dt => dt.TextContent.Contains("Mileage", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(OwnershipType.Purchased, false)]
    [InlineData(OwnershipType.Leased, true)]
    public void DateLeaseEnds_IsShownOnlyForALeasedVehicle(OwnershipType? ownership, bool shown)
    {
        var basis = Detail(vehicleId);
        RegisterApi(_ => Json(new VehicleDetailDto
        {
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Year = basis.Year, Ownership = ownership,
            LeaseEndsOn = new DateOnly(2027, 3, 14), Tags = [], Images = [], CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt,
        }));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var basic = cut.Find("#group-basic");
            var labels = basic.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
            Assert.Equal(shown, labels.Contains("Date lease ends"));
            if (shown)
            {
                Assert.Equal("Mar 14, 2027", basic.QuerySelectorAll("dd")[labels.IndexOf("Date lease ends")].TextContent.Trim());
            }
        });
    }

    [Fact]
    public void BasicInfoHasNoNameRow_AndTheOldPurchasedLabelIsGone()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var labels = cut.Find("#group-basic").QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
            Assert.DoesNotContain("Name", labels);
            Assert.DoesNotContain("Date purchased", labels);
            Assert.Contains("Date purchased or leased", labels);
        });
    }

    [Fact]
    public void VehicleWithoutAYear_ShowsAPromptLinkingToTheBasicInfoEditTab()
    {
        RegisterApi(_ => Json(Detail(vehicleId, year: null)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var prompt = cut.Find("[data-testid='missing-year']");
            Assert.Contains("year", prompt.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal($"/vehicles/{vehicleId}/edit?tab=details", prompt.QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("Not set", cut.Find("#group-basic").QuerySelectorAll("dd")[0].TextContent.Trim());
            Assert.Equal("Ford F-150", cut.Find("h1").TextContent);
        });
    }

    [Fact]
    public void VehicleWithAYear_ShowsNoPrompt()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2020 Ford F-150", cut.Find("h1").TextContent);
            Assert.Empty(cut.FindAll("[data-testid='missing-year']"));
        });
    }

    [Fact]
    public void LegacyVehicle_StillShowsMileageOnBasicInfoAndColorAndDoorsOnSpecifications()
    {
        RegisterApi(_ => Json(Detail(vehicleId, year: null)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("42,000 mi", cut.Find("#group-basic").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
            var values = cut.Find("#group-specifications").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()).ToList();
            Assert.DoesNotContain("42,000 mi", values);
            Assert.Contains("Blue", values);
            Assert.Contains("4", values);
        });
    }

    [Fact]
    public void EveryNewDetailIsShownOnItsTab()
    {
        RegisterApi(_ => Json(FullDetail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["2019", "Subaru", "Outback", "Limited", "1HGCM82633A004352", "Wagon", "Leased", "Mar 15, 2024", "Mar 14, 2027", "42,000 mi", "Not set"],
                cut.Find("#group-basic").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
            Assert.Equal(
                ["2.5L flat-4", "182 hp", "AWD", "Blue", "Gasoline", "Automatic", "5", "4", "17.5 in", "225/60R17"],
                cut.Find("#group-specifications").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
        });
    }

    [Fact]
    public void DescriptionTabShowsTheFeaturesCardThenTheRecentJournalEntriesCardInEightAndFourColumns()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("#tab-description div.row.g-4");
            var columns = row.Children.ToList();
            Assert.Equal(2, columns.Count);
            Assert.Contains("col-lg-8", columns[0].ClassList);
            Assert.Contains("col-lg-4", columns[1].ClassList);
            Assert.NotNull(columns[0].QuerySelector("section[aria-labelledby='tags-heading']"));
            Assert.NotNull(columns[1].QuerySelector("section[aria-labelledby='recent-entries-heading']"));
            Assert.Equal("Features", columns[0].QuerySelector("#tags-heading")!.TextContent.Trim());
            var sections = cut.Find("#tab-description").QuerySelectorAll("section").Select(section => section.GetAttribute("aria-labelledby")).ToList();
            Assert.Equal(["tags-heading", "recent-entries-heading"], sections);
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
            Assert.Equal("Uploaded Images (2)", media.QuerySelector("section.card > h2.card-header")!.TextContent.Trim());
            var tiles = media.QuerySelectorAll("section.card > .card-body [data-testid='image']").ToList();
            Assert.Equal(2, tiles.Count);
            Assert.Equal($"/vehicles/{vehicleId}/images/{main.Id}", tiles[0].QuerySelector("img")!.GetAttribute("src"));
            Assert.Equal("Front view", tiles[0].QuerySelector("img")!.GetAttribute("alt"));
            Assert.NotNull(tiles[0].QuerySelector("[data-testid='main-badge']"));
            Assert.Null(tiles[1].QuerySelector("[data-testid='main-badge']"));
            Assert.Empty(media.QuerySelectorAll("form, button, input"));
        });
    }

    [Fact]
    public void FeaturesCardHasTheHeadingInTheHeaderAndTheItemsInTheBody()
    {
        var tags = new[] { new VehicleTagDto { Id = Guid.NewGuid(), Name = "sunroof" } };
        RegisterApi(_ => Json(DetailWith(vehicleId, tags, [])));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("section[aria-labelledby='tags-heading']");
            Assert.Contains("card", section.ClassList);
            Assert.Equal(["H2", "DIV"], section.Children.Select(child => child.TagName));
            Assert.Equal("Features", section.Children[0].TextContent.Trim());
            Assert.Contains("card-header", section.Children[0].ClassList);
            Assert.Contains("card-body", section.Children[1].ClassList);
            Assert.NotNull(section.Children[1].QuerySelector("[data-testid='feature-categories']"));
        });
    }

    [Fact]
    public void FeaturesAreGroupedByCategoryWithoutRemoveButtons()
    {
        var tags = new[]
        {
            new VehicleTagDto { Id = Guid.NewGuid(), Name = "sunroof", Category = "Exterior" },
            new VehicleTagDto { Id = Guid.NewGuid(), Name = "<b>tow</b>", Category = "Towing & Utility" },
        };
        RegisterApi(_ => Json(DetailWith(vehicleId, tags, [])));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Exterior", "Towing & Utility"], cut.FindAll("#tab-description [data-testid='feature-category']").Select(card => card.GetAttribute("data-category")));
            var items = cut.FindAll("#tab-description [data-testid='tag']");
            Assert.Equal(["sunroof", "<b>tow</b>"], items.Select(item => item.TextContent.Trim()));
            Assert.Empty(cut.FindAll("#tab-description [data-testid='tag'] button"));
            Assert.Empty(cut.FindAll("#tab-description b"));
        });
    }

    [Fact]
    public void JournalNotesAreListedReadOnlyAsATimeline()
    {
        var created = new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero);
        Notes = NotesPage([Note("Changed the oil", "Maintenance", created, created.AddDays(2)), Note("Plain note")]);
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var journal = cut.Find("#tab-description section[aria-labelledby='recent-entries-heading']");
            Assert.Contains("card", journal.ClassList);
            var heading = journal.QuerySelector("h2#recent-entries-heading.card-header")!;
            Assert.Equal("Recent Journal Entries", heading.TextContent.Trim());
            var timeline = journal.QuerySelector(".card-body > ul.mg-timeline")!;
            Assert.Equal("recent-entries-heading", timeline.GetAttribute("aria-labelledby"));
            Assert.Empty(cut.FindAll("#journal-heading"));

            var notes = timeline.QuerySelectorAll("li.mg-timeline-item[data-testid='note']").ToList();
            Assert.Equal(2, notes.Count);
            Assert.Equal("true", notes[0].QuerySelector("span.mg-timeline-marker")!.GetAttribute("aria-hidden"));
            var time = notes[0].QuerySelector("time.mg-timeline-time")!;
            Assert.Equal("2026-10-31 15:30:00 UTC (edited)", time.TextContent.Trim());
            Assert.Equal("2026-10-31T15:30:00.0000000Z", time.GetAttribute("datetime"));
            Assert.Equal("Maintenance", notes[0].QuerySelector("span.badge[data-testid='note-category']")!.TextContent.Trim());
            Assert.Equal("Changed the oil", notes[0].QuerySelector("span.note-body")!.TextContent.Trim());
            Assert.Equal("2026-10-31 15:30:00 UTC", notes[1].QuerySelector("time.mg-timeline-time")!.TextContent.Trim());
            Assert.Null(notes[1].QuerySelector("[data-testid='note-category']"));
            Assert.DoesNotContain("Oct 31, 2026", cut.Find("#tab-description").TextContent);
        });
    }

    [Fact]
    public void EmptyFeatures_ShowAMessageLinkingToTheEditPage()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("section[aria-labelledby='tags-heading']");
            Assert.Contains("No features yet. Add features", section.TextContent);
            Assert.Empty(section.QuerySelectorAll("ul"));
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
        var forms = cut.FindAll("form:not([role='search'])");
        Assert.Single(forms);
        Assert.Equal("get", cut.Find("form[role='search']").GetAttribute("method"));
        Assert.NotNull(cut.Find("details[data-testid='delete-vehicle'] form"));
        Assert.Empty(cut.FindAll("input:not([type='hidden']):not([type='search']), textarea, select, input[type='file']"));
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
            Assert.NotNull(cut.Find("section[aria-labelledby='recent-entries-heading']"));
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
            Id = bare.Id, Make = "Ford", Model = "F-150", Tags = [], Images = [],
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
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles", "2020 Ford F-150"], items.Select(item => item.TextContent.Trim()));
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
        RegisterApi(_ => Json(Detail(vehicleId)));

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
        RegisterApi(_ => Json(Detail(vehicleId, make: "<img src=x onerror=alert(1)>", year: null)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Equal("<img src=x onerror=alert(1)> F-150", cut.Find("h1").TextContent));
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
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("details[data-testid='delete-vehicle']");
            Assert.False(section.HasAttribute("open"));
            Assert.Contains("Delete 2020 Ford F-150?", section.TextContent);
            Assert.Contains("tags, journal notes, service records, charging and fueling entries, inspections, registrations, insurance, taxes, pictures, and kept documents", section.TextContent);
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

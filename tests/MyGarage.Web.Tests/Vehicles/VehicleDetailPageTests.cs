using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleDetailPageTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleDetail> RenderDetail() =>
        Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

    [Fact]
    public void ShowsEveryDetail()
    {
        RegisterApi(_ => Json(Detail(vehicleId, "Daily")));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily", cut.Find("h1").TextContent);
            Assert.Contains("Ford F-150", cut.Markup);
            Assert.Contains("XLT", cut.Markup);
            Assert.Contains("Blue", cut.Markup);
            Assert.Contains("42,000 mi", cut.Markup);
            Assert.Contains(">4<", cut.Markup);
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

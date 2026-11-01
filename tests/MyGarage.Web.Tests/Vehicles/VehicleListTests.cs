using System.Net;
using Bunit;
using MyGarage.Web.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleListTests : VehicleTestContext
{
    [Fact]
    public void ShowsPageHeaderWithTrailTitleAndAddAction()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("h1"));
            Assert.Equal("My Vehicles", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Vehicles"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal("page", items[1].GetAttribute("aria-current"));
            var add = cut.Find("h1").ParentElement!.QuerySelector("a[data-testid='add-vehicle']")!;
            Assert.Equal("Add vehicle", add.TextContent.Trim());
            Assert.Equal("/vehicles/new", add.GetAttribute("href"));
        });
    }

    [Fact]
    public void AddVehicleHasAnIconAndATitle()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            var add = cut.Find("a[data-testid='add-vehicle']");
            Assert.Equal("Add a new vehicle", add.GetAttribute("title"));
            Assert.Equal("true", add.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
        });
    }

    [Fact]
    public void EmptyGarage_ShowsFriendlyStateWithAddLink()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You have no vehicles yet", cut.Markup);
            Assert.Equal("/vehicles/new", cut.Find("a[data-testid='add-vehicle']").GetAttribute("href"));
        });
    }

    [Fact]
    public void Vehicles_AreShownAsCardsNotATable()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha"), Summary("Bravo"), Summary("Charlie")])));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".mg-vehicle-card").Count);
            Assert.DoesNotContain("<table", cut.Markup);
        });
    }

    [Fact]
    public void ManyPages_ShowPagerWithPageXOfY()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));
        NavigateTo("/vehicles?page=2");

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2 of 3", cut.Markup);
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains("/vehicles?page=1", links);
            Assert.Contains("/vehicles?page=3", links);
        });
    }

    [Fact]
    public void FirstPage_PreviousIsDisabled()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 1, totalCount: 30)));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            var previous = cut.Find("li[data-testid='pager-previous']");
            Assert.Contains("disabled", previous.ClassName);
        });
    }

    [Fact]
    public void RequestedPageOutOfRange_ShowsThePageTheApiServed()
    {
        RegisterApi(_ => Json(Page([Summary("Zed")], page: 3, totalCount: 25)));
        NavigateTo("/vehicles?page=999");

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() => Assert.Contains("Page 3 of 3", cut.Markup));
        Assert.Contains("page=999", ListRequests.Single().RequestUri!.Query);
    }

    [Theory]
    [InlineData("-4")]
    [InlineData("0")]
    [InlineData("abc")]
    public void InvalidPageQueryValue_RequestsPageOne(string value)
    {
        RegisterApi(_ => Json(Page([])));
        NavigateTo($"/vehicles?page={value}");

        Render<VehicleList>();

        Assert.Contains("page=1", ListRequests.Single().RequestUri!.Query);
    }

    [Fact]
    public void ApiUnavailable_ShowsFriendlyMessage()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.Unavailable, cut.Markup);
            Assert.DoesNotContain("boom", cut.Markup);
        });
    }

    [Fact]
    public void EmptyGarage_UsesTheStandardEmptyState()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("You have no vehicles yet.", cut.Find("p.lead").TextContent);
            var action = cut.Find(".text-center a.btn");
            Assert.Contains("btn-primary", action.ClassName);
            Assert.Equal("Add your first vehicle", action.TextContent);
            Assert.Equal("/vehicles/new", action.GetAttribute("href"));
        });
    }

    [Fact]
    public void NoMatches_UsesTheStandardNoMatchState()
    {
        RegisterApi(_ => Json(Page([])));
        NavigateTo("/vehicles?q=zzz");

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            var action = cut.Find("a[data-testid='no-match-clear']");
            Assert.Contains("btn-outline-primary", action.ClassName);
            Assert.Equal("Clear search", action.TextContent);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));

        var cut = Render<VehicleList>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.NotAccepted, cut.Markup);
            Assert.Equal("alert", cut.Find("div.alert.alert-warning").GetAttribute("role"));
            Assert.Equal("/authentication/login?returnUrl=%2Fvehicles", cut.Find("a[href^='/authentication/login']").GetAttribute("href"));
        });
    }
}

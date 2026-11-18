using System.Net;
using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleTimelineViewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static VehicleTimelineEntryDto Entry(
        VehicleTimelineKind kind, string title, DateOnly date, decimal? amount = null, int? odometer = null, string? detail = null, bool fromEmail = false) =>
        new() { Kind = kind, Id = Guid.NewGuid(), OccurredOn = date, Title = title, Amount = amount, OdometerReading = odometer, Detail = detail, FromEmail = fromEmail };

    private static PagedResultDto<VehicleTimelineEntryDto> TimelinePage(IReadOnlyList<VehicleTimelineEntryDto> items, int page = 1, int pageSize = 20, int totalCount = -1)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<VehicleTimelineEntryDto>
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    private IEnumerable<HttpRequestMessage> TimelineRequests => Requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/timeline", StringComparison.Ordinal));

    private IRenderedComponent<VehicleTimelineView> RenderTimeline(PagedResultDto<VehicleTimelineEntryDto> page, string? kind = null, int pageNumber = 1)
    {
        RegisterApi(_ => Json(page));
        return Render<VehicleTimelineView>(parameters => parameters
            .Add(view => view.VehicleId, vehicleId).Add(view => view.Kind, kind).Add(view => view.Page, pageNumber));
    }

    [Fact]
    public void ShowsEveryEntryWithItsDateKindTitleAndFigures()
    {
        var cut = RenderTimeline(TimelinePage(
        [
            Entry(VehicleTimelineKind.Service, "Oil change", new DateOnly(2026, 5, 1), 80m, 45_000, "Corner Garage"),
            Entry(VehicleTimelineKind.Journal, "Trip", new DateOnly(2026, 4, 2), detail: "Drove to the coast."),
        ]));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='timeline-entry']");
            Assert.Equal(2, rows.Count);
            var first = rows[0];
            Assert.Equal("May 1, 2026", first.QuerySelector("time")!.TextContent.Trim());
            Assert.Equal("2026-05-01", first.QuerySelector("time")!.GetAttribute("datetime"));
            Assert.Equal("Service", first.QuerySelector("[data-testid='timeline-kind']")!.TextContent.Trim());
            Assert.Contains("Oil change", first.TextContent);
            Assert.Contains("Corner Garage", first.TextContent);
            Assert.Equal("$80.00", first.QuerySelector("[data-testid='timeline-amount']")!.TextContent.Trim());
            Assert.Equal("45,000 mi", first.QuerySelector("[data-testid='timeline-odometer']")!.TextContent.Trim());
            Assert.Null(rows[1].QuerySelector("[data-testid='timeline-amount']"));
            Assert.Null(rows[1].QuerySelector("[data-testid='timeline-odometer']"));
        });
    }

    [Fact]
    public void AServiceEntryLinksToItsReceipt_OtherKindsDoNot()
    {
        var service = Entry(VehicleTimelineKind.Service, "Oil change", new DateOnly(2026, 5, 1));
        var cut = RenderTimeline(TimelinePage([service, Entry(VehicleTimelineKind.FuelEnergy, "Fuel", new DateOnly(2026, 4, 1))]));

        cut.WaitForAssertion(() =>
        {
            var receipt = Assert.Single(cut.FindAll("a[data-testid='timeline-receipt']"));
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{service.Id}", receipt.GetAttribute("href"));
        });
    }

    [Fact]
    public void AnEntryCreatedFromAnEmail_IsMarked()
    {
        var cut = RenderTimeline(TimelinePage([Entry(VehicleTimelineKind.FuelEnergy, "Fuel", new DateOnly(2026, 4, 1), fromEmail: true)]));

        cut.WaitForAssertion(() => Assert.Equal("From email", cut.Find("[data-testid='from-email']").TextContent.Trim()));
    }

    [Fact]
    public void FilterLinks_NameEveryKind_AndMarkTheCurrentOne()
    {
        var cut = RenderTimeline(TimelinePage([Entry(VehicleTimelineKind.Tax, "Personal property tax", new DateOnly(2026, 1, 1))]), kind: "tax");

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("[data-testid='timeline-filters'] a");
            Assert.Equal(["All", "Service", "Fuel and charging", "Inspections", "Registrations", "Insurance", "Taxes", "Journal"], links.Select(link => link.TextContent.Trim()));
            Assert.Equal($"/vehicles/{vehicleId}?tab=timeline", links[0].GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=timeline&kind=fuel-energy", links[2].GetAttribute("href"));
            var current = Assert.Single(links, link => link.GetAttribute("aria-current") == "page");
            Assert.Equal("Taxes", current.TextContent.Trim());
        });
        Assert.Contains("kind=tax", TimelineRequests.Single().RequestUri!.Query);
    }

    [Fact]
    public void AnUnknownKind_ShowsEverything()
    {
        var cut = RenderTimeline(TimelinePage([Entry(VehicleTimelineKind.Tax, "Tax", new DateOnly(2026, 1, 1))]), kind: "bogus");

        cut.WaitForAssertion(() => Assert.Equal("All", cut.Find("[data-testid='timeline-filters'] a[aria-current]").TextContent.Trim()));
        Assert.DoesNotContain("kind=", TimelineRequests.Single().RequestUri!.Query);
    }

    [Fact]
    public void NothingRecorded_SaysSo()
    {
        var all = RenderTimeline(TimelinePage([]));

        all.WaitForAssertion(() => Assert.Equal("Nothing has been recorded for this vehicle yet.", all.Find("[data-testid='timeline-empty']").TextContent.Trim()));
    }

    [Fact]
    public void AFilterWithNothingInIt_SaysThatInstead()
    {
        var filtered = RenderTimeline(TimelinePage([]), kind: "tax");

        filtered.WaitForAssertion(() => Assert.Equal("Nothing of this kind has been recorded yet.", filtered.Find("[data-testid='timeline-empty']").TextContent.Trim()));
    }

    [Fact]
    public void ManyPages_ShowAPagerThatKeepsTheKindFilter()
    {
        var cut = RenderTimeline(TimelinePage([Entry(VehicleTimelineKind.Journal, "Note", new DateOnly(2026, 4, 1))], page: 2, pageSize: 20, totalCount: 45), kind: "journal", pageNumber: 2);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2 of 3", cut.Markup);
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(link => link.GetAttribute("href")).ToList();
            Assert.Contains($"/vehicles/{vehicleId}?tab=timeline&kind=journal", links);
            Assert.Contains($"/vehicles/{vehicleId}?tab=timeline&kind=journal&timelinePage=3", links);
        });
        Assert.Contains("page=2", TimelineRequests.Single().RequestUri!.Query);
    }

    [Fact]
    public void UserText_IsEncoded()
    {
        var cut = RenderTimeline(TimelinePage([Entry(VehicleTimelineKind.Journal, "<script>alert(1)</script>", new DateOnly(2026, 4, 1), detail: "<b>x</b>")]));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("script"));
            Assert.Empty(cut.FindAll("[data-testid='timeline-entry'] b"));
            Assert.Contains("<script>alert(1)</script>", cut.Find(".mg-feed-title").TextContent);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void WhenTheApiRejectsTheToken_ShowsSignInAgain(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));

        var cut = Render<VehicleTimelineView>(parameters => parameters.Add(view => view.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }

    [Fact]
    public void WhenTheApiIsUnavailable_ShowsAFriendlyMessage()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));

        var cut = Render<VehicleTimelineView>(parameters => parameters.Add(view => view.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.Unavailable, cut.Markup);
            Assert.DoesNotContain("boom", cut.Markup);
        });
    }
}

using Bunit;
using Microsoft.AspNetCore.Components;
using MyGarage.Web.Common;
using MyGarage.Web.Maintenance;

namespace MyGarage.Web.Tests.Maintenance;

public class EntryListBlockTests : BunitContext
{
    private static RenderFragment<string> Row => item => builder => builder.AddMarkupContent(0, $"<span data-testid=\"row-text\">{item}</span>");

    private static RenderFragment Add => builder => builder.AddMarkupContent(0, "<a data-testid=\"add\" href=\"/add\">Add</a>");

    private IRenderedComponent<EntryListBlock<string>> RenderBlock(
        IReadOnlyList<string>? items, int totalCount, int totalPages = 1, GarageApiFailure? failure = null) =>
        Render<EntryListBlock<string>>(parameters => parameters
            .Add(block => block.Items, items)
            .Add(block => block.TotalCount, totalCount)
            .Add(block => block.Page, 1)
            .Add(block => block.TotalPages, totalPages)
            .Add(block => block.HrefFor, page => $"/list?page={page}")
            .Add(block => block.RowTemplate, Row)
            .Add(block => block.EmptyMessage, "Nothing here yet.")
            .Add(block => block.AddActions, Add)
            .Add(block => block.Failure, failure)
            .Add(block => block.ReturnUrl, "/back"));

    [Fact]
    public void NoItems_ShowsTheEmptyMessageAndStillShowsTheAddActions()
    {
        var cut = RenderBlock([], 0);

        Assert.Contains("Nothing here yet.", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid='add']"));
        Assert.Empty(cut.FindAll(".list-group"));
    }

    [Fact]
    public void Items_AreRenderedThroughTheRowTemplateInAListGroup()
    {
        var cut = RenderBlock(["one", "two"], 2);

        var rows = cut.FindAll(".list-group > li");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["one", "two"], cut.FindAll("[data-testid='row-text']").Select(row => row.TextContent));
        Assert.NotNull(cut.Find("[data-testid='add']"));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void ThePager_AppearsOnlyWithMoreThanOnePageAndUsesTheSuppliedLinks()
    {
        Assert.Empty(RenderBlock(["one"], 1, totalPages: 1).FindAll("nav[aria-label='Pagination']"));

        var cut = RenderBlock(["one"], 25, totalPages: 3);

        Assert.Equal("/list?page=2", cut.Find("[data-testid='pager-next'] a").GetAttribute("href"));
    }

    [Fact]
    public void AFailure_ShowsTheAlertInsteadOfTheListAndKeepsTheAddActions()
    {
        var cut = RenderBlock(null, 0, failure: GarageApiFailure.Unavailable);

        Assert.NotNull(cut.Find("[role='alert']"));
        Assert.Empty(cut.FindAll(".list-group"));
        Assert.NotNull(cut.Find("[data-testid='add']"));
    }
}

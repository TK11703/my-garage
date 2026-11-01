using Bunit;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class EmptyStateTests : BunitContext
{
    [Fact]
    public void Empty_ShowsLeadMessageAndPrimaryAction()
    {
        var cut = Render<EmptyState>(ps => ps
            .Add(p => p.Message, "You have no vehicles yet.")
            .Add(p => p.ActionText, "Add your first vehicle")
            .Add(p => p.ActionHref, "/vehicles/new"));

        Assert.Contains("text-center", cut.Find("div").ClassName);
        Assert.Contains("py-5", cut.Find("div").ClassName);
        Assert.Equal("You have no vehicles yet.", cut.Find("p.lead").TextContent);
        var action = cut.Find("a.btn");
        Assert.Contains("btn-primary", action.ClassName);
        Assert.Equal("/vehicles/new", action.GetAttribute("href"));
        Assert.Equal("Add your first vehicle", action.TextContent);
    }

    [Fact]
    public void NoMatch_ShowsAnOutlineAction()
    {
        var cut = Render<EmptyState>(ps => ps
            .Add(p => p.Variant, EmptyStateVariant.NoMatch)
            .Add(p => p.Message, "No vehicles match.")
            .Add(p => p.ActionText, "Clear")
            .Add(p => p.ActionHref, "/vehicles"));

        Assert.Contains("btn-outline-primary", cut.Find("a.btn").ClassName);
    }

    [Fact]
    public void WithoutAnAction_ShowsNoLink()
    {
        var cut = Render<EmptyState>(ps => ps.Add(p => p.Message, "Nothing here."));

        Assert.Empty(cut.FindAll("a"));
    }

    [Fact]
    public void Compact_RendersASingleSecondaryParagraph()
    {
        var cut = Render<EmptyState>(ps => ps
            .Add(p => p.Compact, true)
            .Add(p => p.Message, "No tags yet."));

        var paragraph = Assert.Single(cut.FindAll("p"));
        Assert.Contains("text-body-secondary", paragraph.ClassName);
        Assert.Empty(cut.FindAll(".lead"));
        Assert.Empty(cut.FindAll(".py-5"));
        Assert.Equal("No tags yet.", paragraph.TextContent);
    }

    [Fact]
    public void Message_IsEncoded()
    {
        var cut = Render<EmptyState>(ps => ps.Add(p => p.Message, "<script>alert(1)</script>"));

        Assert.Empty(cut.FindAll("script"));
    }
}

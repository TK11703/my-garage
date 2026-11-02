using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleTagsSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static VehicleTagDto Tag(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private IRenderedComponent<VehicleTagsSection> RenderSection(params VehicleTagDto[] tags) =>
        Render<VehicleTagsSection>(parameters => parameters
            .Add(section => section.VehicleId, vehicleId)
            .Add(section => section.Tags, tags));

    [Fact]
    public void NoFeatures_ShowsAnEncouragingEmptyState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        Assert.Equal("Features", cut.Find("h2#tags-heading").TextContent.Trim());
        Assert.Contains("No features yet. Start typing to reuse a feature from other vehicles, or add your own.", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='feature-list']"));
        Assert.DoesNotContain("Features and tags", cut.Markup);
    }

    [Fact]
    public void AddFormComesBeforeTheIncludedFeaturesHeadingAndList()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("sunroof"));

        var section = cut.Find("section.card[aria-labelledby='tags-heading']");
        Assert.Equal(["H2", "DIV"], section.Children.Select(child => child.TagName));
        Assert.Contains("card-header", section.Children[0].ClassList);
        var body = section.Children[1];
        Assert.Contains("card-body", body.ClassList);
        Assert.Equal(["FORM", "DIV", "H3", "UL"], body.Children.Select(child => child.TagName));
        Assert.Equal("tag-help", body.Children[1].Id);
        Assert.Equal("add-tag", body.Children[0].GetAttribute("data-testid"));
        Assert.Equal("Included Features", body.Children[2].TextContent.Trim());
        Assert.Equal("included-features-heading", body.Children[2].Id);
        Assert.Equal("included-features-heading", body.Children[3].GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void NoFeatures_KeepsTheIncludedFeaturesHeadingAfterTheAddFormWithTheEmptyStateBelowIt()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        var section = cut.Find("section[aria-labelledby='tags-heading'] .card-body");
        var children = section.Children.ToList();
        var heading = children.Single(child => child.Id == "included-features-heading");
        Assert.True(children.IndexOf(heading) > children.IndexOf(cut.Find("form[data-testid='add-tag']")));
        Assert.Contains("No features yet", children[children.IndexOf(heading) + 1].TextContent);
    }

    [Fact]
    public void Features_AreShownInAListGroupWithAccessibleRemoveButtons()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("sunroof"), Tag("tow package"));

        var list = cut.Find("ul.list-group[data-testid='feature-list']");
        var items = list.QuerySelectorAll("li.list-group-item[data-testid='tag']");
        Assert.Equal(["sunroof", "tow package"], items.Select(item => item.TextContent.Trim()));
        Assert.Equal(
            ["Remove feature sunroof", "Remove feature tow package"],
            items.Select(item => item.QuerySelector("form button[type='submit'].btn-close")!.GetAttribute("aria-label")));
        Assert.Empty(cut.FindAll("span.badge"));
        Assert.DoesNotContain("Remove tag", cut.Markup);
    }

    [Fact]
    public void EachFeature_HasItsOwnRemoveForm()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("sunroof"), Tag("tow package"));

        Assert.All(cut.FindAll("[data-testid='tag']"), item => Assert.Single(item.QuerySelectorAll("form")));
    }

    [Fact]
    public void FeatureText_IsEncoded()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("<b>bold</b>"));

        Assert.Empty(cut.FindAll("[data-testid='tag'] b"));
        Assert.Equal("<b>bold</b>", cut.Find("[data-testid='tag']").TextContent.Trim());
    }

    [Fact]
    public void AddForm_HasALabeledInputWithTheFeatureLimit()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        Assert.Equal("Add a feature", cut.Find("label[for='tag-name']").TextContent.Trim());
        Assert.Equal("30", cut.Find("input#tag-name").GetAttribute("maxlength"));
        Assert.Equal("Add feature", cut.Find("form[data-testid='add-tag'] button[type='submit']").TextContent.Trim());
    }

    [Fact]
    public void AddInput_IsWiredToTheSuggestionListAndEndpoint()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        var input = cut.Find("input#tag-name");
        Assert.Equal("feature-options", input.GetAttribute("list"));
        Assert.Equal("/features/suggest", input.GetAttribute("data-suggest-url"));
        Assert.Equal("off", input.GetAttribute("autocomplete"));
        var options = cut.Find("datalist#feature-options");
        Assert.Empty(options.Children);
        Assert.Equal("tag-help", input.GetAttribute("aria-describedby"));
        Assert.NotNull(cut.Find("#tag-help"));
    }

    [Fact]
    public void Add_Success_PostsTheNameAndReturnsToTheVehicle()
    {
        RegisterApi(_ => Json(Tag("sunroof"), HttpStatusCode.Created));
        var cut = RenderSection();
        cut.Find("input#tag-name").Change("sunroof");

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single();
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal($"/api/vehicles/{vehicleId}/tags", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Add_Duplicate_ShowsTheApiMessage()
    {
        RegisterApi(_ => Problem(HttpStatusCode.Conflict, "This vehicle already has that tag."));
        var cut = RenderSection(Tag("sunroof"));
        cut.Find("input#tag-name").Change("SUNROOF");

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() => Assert.Contains("This vehicle already has that tag.", cut.Find("#tag-error").TextContent));
    }

    [Fact]
    public void Add_LimitReached_ShowsTheApiMessage()
    {
        RegisterApi(_ => Problem(HttpStatusCode.Conflict, "A vehicle can have at most 20 tags."));
        var cut = RenderSection();
        cut.Find("input#tag-name").Change("one more");

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() => Assert.Contains("at most 20 tags", cut.Find("#tag-error").TextContent));
    }

    [Fact]
    public void Add_InvalidName_ShowsTheValidationMessage()
    {
        RegisterApi(_ => ValidationProblem(("Name", "Tag is required.")));
        var cut = RenderSection();

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Tag is required.", cut.Find("#tag-error").TextContent);
            Assert.Equal("true", cut.Find("input#tag-name").GetAttribute("aria-invalid"));
        });
    }

    [Fact]
    public void Add_InvalidName_UsesTheStandardFieldErrorMarkup()
    {
        RegisterApi(_ => ValidationProblem(("Name", "Tag is required.")));
        var cut = RenderSection();

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() =>
        {
            var error = cut.Find("#tag-error");
            Assert.Contains("invalid-feedback", error.ClassName);
            Assert.Contains("d-block", error.ClassName);
            Assert.Equal("alert", error.GetAttribute("role"));
            Assert.DoesNotContain("text-danger", error.ClassName);
            Assert.Equal("tag-help tag-error", cut.Find("input#tag-name").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void NoFeatures_UsesTheCompactEmptyState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        Assert.Contains("text-body-secondary", cut.Find("section p").ClassName);
        Assert.Contains("No features yet", cut.Find("section p").TextContent);
    }

    [Fact]
    public void ReturnPath_IsWhereAddAndRemoveReturn()
    {
        var sunroof = Tag("sunroof");
        RegisterApi(_ => Json(Tag("x"), HttpStatusCode.Created));
        var returnPath = $"/vehicles/{vehicleId}/edit?tab=description";
        var cut = Render<VehicleTagsSection>(parameters => parameters
            .Add(section => section.VehicleId, vehicleId)
            .Add(section => section.Tags, new[] { sunroof })
            .Add(section => section.ReturnPath, returnPath));
        cut.Find("input#tag-name").Change("x");

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith(returnPath, Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void Remove_SendsDeleteForThatFeatureAndReturnsToTheVehicle()
    {
        var sunroof = Tag("sunroof");
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderSection(sunroof, Tag("tow package"));

        cut.Find("[data-testid='tag'] form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{vehicleId}", Services.GetRequiredService<NavigationManager>().Uri));
        var delete = Requests.Single();
        Assert.Equal(HttpMethod.Delete, delete.Method);
        Assert.Equal($"/api/vehicles/{vehicleId}/tags/{sunroof.Id}", delete.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgainMessage(HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));
        var cut = RenderSection();
        cut.Find("input#tag-name").Change("x");

        cut.Find("form[data-testid='add-tag']").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Find("#tag-error").TextContent));
    }
}

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
    public void NoTags_ShowsAnEncouragingEmptyState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        Assert.Contains("No tags yet", cut.Markup);
    }

    [Fact]
    public void Tags_AreShownAsBadgesWithAccessibleRemoveButtons()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("sunroof"), Tag("tow package"));

        Assert.Equal(["sunroof", "tow package"], cut.FindAll("[data-testid='tag']").Select(badge => badge.TextContent.Trim()));
        Assert.Equal(
            ["Remove tag sunroof", "Remove tag tow package"],
            cut.FindAll("[data-testid='tag'] button").Select(button => button.GetAttribute("aria-label")));
    }

    [Fact]
    public void TagText_IsEncoded()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(Tag("<b>bold</b>"));

        Assert.Empty(cut.FindAll("[data-testid='tag'] b"));
        Assert.Equal("<b>bold</b>", cut.Find("[data-testid='tag']").TextContent.Trim());
    }

    [Fact]
    public void AddForm_HasALabeledInputWithTheTagLimit()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        Assert.NotNull(cut.Find("label[for='tag-name']"));
        Assert.Equal("30", cut.Find("input#tag-name").GetAttribute("maxlength"));
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
    public void Remove_SendsDeleteForThatTagAndReturnsToTheVehicle()
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

using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class JournalNoteCreateTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private IRenderedComponent<JournalNoteCreate> RenderPage(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : (respond ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound)))(request));
        return Render<JournalNoteCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    [Fact]
    public void ShowsTheTitleTrailAndALabeledForm()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Add journal note", cut.Find("h1").TextContent);
            var trail = cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()).ToList();
            Assert.Equal("Home", trail[0]);
            Assert.Contains(trail, crumb => crumb.Contains("Ford F-150", StringComparison.Ordinal));
            Assert.NotNull(cut.Find("label[for='note-body']"));
            Assert.Equal("4000", cut.Find("textarea#note-body").GetAttribute("maxlength"));
            Assert.Contains("optional", cut.Find("label[for='note-category']").TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("40", cut.Find("input#note-category").GetAttribute("maxlength"));
            Assert.Equal("create-note", cut.Find("[data-testid='form-actions'] button[type='submit']").GetAttribute("form"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=timeline&kind=journal", cut.Find("[data-testid='form-actions'] a.btn").GetAttribute("href"));
        });
    }

    [Fact]
    public void Save_PostsTheNoteThenOpensTheJournalOnTheTimeline()
    {
        var cut = RenderPage(_ => Json(Note("saved", "Oil change"), HttpStatusCode.Created));
        cut.WaitForElement("textarea#note-body").Change("Changed the oil");
        cut.Find("input#note-category").Change("Oil change");

        cut.Find("form#create-note").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?tab=timeline&kind=journal&saved=note", CurrentUri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/notes", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void InvalidInput_ShowsTheApiMessagesAndKeepsWhatWasTyped()
    {
        var cut = RenderPage(_ => ValidationProblem(("Body", "Note is required."), ("Category", "Category must be at most 40 characters.")));
        cut.WaitForElement("input#note-category").Change("a long category");

        cut.Find("form#create-note").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Note is required.", cut.Find("#note-body-error").TextContent);
            Assert.Contains("at most 40", cut.Find("#note-category-error").TextContent);
            Assert.Equal("a long category", cut.Find("input#note-category").GetAttribute("value"));
        });
    }

    [Fact]
    public void AnUnknownVehicle_ShowsTheNotFoundState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<JournalNoteCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Vehicle not found", cut.Find("h1").TextContent);
            Assert.Contains(VehicleMessages.NotFound, cut.Markup);
            Assert.Empty(cut.FindAll("form#create-note"));
        });
    }

    [Fact]
    public void AnOutage_ShowsTheUnavailableMessage()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = Render<JournalNoteCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.Unavailable, cut.Markup));
    }

    [Fact]
    public void ItRequiresSignIn() =>
        Assert.NotEmpty(typeof(JournalNoteCreate).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true));

    [Fact]
    public void TheSavedNoticeIsShownOnTheVehiclePage() =>
        Assert.Equal("Journal note added.", VehicleMessages.SavedNotice("note"));
}

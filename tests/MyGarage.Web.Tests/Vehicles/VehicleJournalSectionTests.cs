using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleJournalSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleJournalSection> RenderSection(int page = 1) =>
        Render<VehicleJournalSection>(parameters => parameters
            .Add(section => section.VehicleId, vehicleId)
            .Add(section => section.NotesPage, page));

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void AddForm_HasLabeledBodyAndOptionalFreeTextCategory()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("label[for='note-body']"));
            Assert.Equal("4000", cut.Find("textarea#note-body").GetAttribute("maxlength"));
            Assert.Contains("optional", cut.Find("label[for='note-category']").TextContent, StringComparison.OrdinalIgnoreCase);
            var category = cut.Find("input#note-category");
            Assert.Equal("40", category.GetAttribute("maxlength"));
            Assert.Empty(cut.FindAll("select"));
        });
    }

    [Fact]
    public void DeleteConfirmation_UsesTheStandardWording()
    {
        Notes = NotesPage([Note("A note")]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            var confirmation = cut.Find("details[data-testid='delete-note']");
            Assert.Equal("Yes, delete this note", confirmation.QuerySelector("button")!.TextContent.Trim());
            Assert.Equal("Keep note", confirmation.QuerySelector("a.btn")!.TextContent.Trim());
        });
    }

    [Fact]
    public void NoNotes_ShowsAnEmptyState()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Contains("No notes yet", cut.Markup));
    }

    [Fact]
    public void Notes_AreListedInTheOrderReturnedWithCategoryAndDate()
    {
        Notes = NotesPage([Note("Newest note", "Road trip"), Note("Older note", null, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            var notes = cut.FindAll("[data-testid='note']");
            Assert.Equal(2, notes.Count);
            Assert.Contains("Newest note", notes[0].TextContent);
            Assert.Contains("Road trip", notes[0].QuerySelector("[data-testid='note-category']")!.TextContent);
            Assert.Contains("Oct 31, 2026", notes[0].TextContent);
            Assert.Contains("Older note", notes[1].TextContent);
            Assert.Null(notes[1].QuerySelector("[data-testid='note-category']"));
            Assert.Contains("Jan 2, 2026", notes[1].TextContent);
        });
    }

    [Fact]
    public void NoteBodyAndCategory_AreEncodedAndKeepLineBreaks()
    {
        Notes = NotesPage([Note("<img src=x onerror=alert(1)>\nsecond line", "<b>cat</b>")]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='note'] img"));
            Assert.Empty(cut.FindAll("[data-testid='note-category'] b"));
            Assert.Equal("<b>cat</b>", cut.Find("[data-testid='note-category']").TextContent.Trim());
            Assert.Contains("second line", cut.Find("[data-testid='note'] .note-body").TextContent);
        });
    }

    [Fact]
    public void EditedNotes_AreMarkedAsEdited()
    {
        var created = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Notes = NotesPage([Note("changed", null, created, created.AddDays(2))]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Contains("edited", cut.Find("[data-testid='note']").TextContent));
    }

    [Fact]
    public void ManyPages_ShowAPagerThatKeepsTheNotesPageQuery()
    {
        Notes = NotesPage([Note("n")], page: 2, totalCount: 25);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection(2);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2 of 3", cut.Markup);
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains($"/vehicles/{vehicleId}", links);
            Assert.Contains($"/vehicles/{vehicleId}?notesPage=3", links);
        });
        Assert.Contains("page=2", Requests.First().RequestUri!.Query);
    }

    [Fact]
    public void ReturnPath_IsKeptByThePagerAndAfterAdding()
    {
        Notes = NotesPage([Note("n")], page: 2, totalCount: 25);
        RegisterApi(_ => Json(Note("saved"), HttpStatusCode.Created));
        var returnPath = $"/vehicles/{vehicleId}/edit?tab=description";
        var cut = Render<VehicleJournalSection>(parameters => parameters
            .Add(section => section.VehicleId, vehicleId)
            .Add(section => section.NotesPage, 2)
            .Add(section => section.ReturnPath, returnPath));

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains(returnPath, links);
            Assert.Contains($"{returnPath}&notesPage=3", links);
        });

        cut.Find("textarea#note-body").Change("Hello");
        cut.Find("form[data-testid='add-note']").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith(returnPath, CurrentUri));
    }

    [Fact]
    public void Add_PostsBodyAndCategoryThenReturnsToTheFirstPage()
    {
        RegisterApi(_ => Json(Note("saved", "Oil change"), HttpStatusCode.Created));
        var cut = RenderSection(2);
        cut.Find("textarea#note-body").Change("Changed the oil");
        cut.Find("input#note-category").Change("Oil change");

        cut.Find("form[data-testid='add-note']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}", CurrentUri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/notes", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Add_WithoutACategory_StillPosts()
    {
        RegisterApi(_ => Json(Note("saved"), HttpStatusCode.Created));
        var cut = RenderSection();
        cut.Find("textarea#note-body").Change("Just a note");

        cut.Find("form[data-testid='add-note']").Submit();

        cut.WaitForAssertion(() => Assert.Single(Requests, request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public void Add_InvalidInput_ShowsApiMessagesAndKeepsWhatWasTyped()
    {
        RegisterApi(_ => ValidationProblem(("Body", "Note is required."), ("Category", "Category must be at most 40 characters.")));
        var cut = RenderSection();
        cut.Find("input#note-category").Change("a long category");

        cut.Find("form[data-testid='add-note']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Note is required.", cut.Find("#note-body-error").TextContent);
            Assert.Contains("at most 40", cut.Find("#note-category-error").TextContent);
            Assert.Equal("a long category", cut.Find("input#note-category").GetAttribute("value"));
            Assert.Equal("true", cut.Find("textarea#note-body").GetAttribute("aria-invalid"));
        });
    }

    [Fact]
    public void Edit_IsPrefilledAndPutsTheChangesThenStaysOnTheSamePage()
    {
        var note = Note("before", "Old");
        Notes = NotesPage([note], page: 2, totalCount: 25);
        RegisterApi(_ => Json(note));
        var cut = RenderSection(2);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='edit-note'] form")));

        var body = cut.Find($"textarea#note-{note.Id}-body");
        Assert.Equal("before", body.GetAttribute("value") ?? body.TextContent.Trim());
        Assert.Equal("Old", cut.Find($"input#note-{note.Id}-category").GetAttribute("value"));
        cut.Find($"textarea#note-{note.Id}-body").Change("after");
        cut.Find("[data-testid='edit-note'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?notesPage=2", CurrentUri));
        var put = Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal($"/api/vehicles/{vehicleId}/notes/{note.Id}", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Edit_InvalidInput_ShowsTheMessageOnThatNote()
    {
        var note = Note("before");
        Notes = NotesPage([note]);
        RegisterApi(_ => ValidationProblem(("Body", "Note is required.")));
        var cut = RenderSection();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='edit-note'] form")));

        cut.Find("[data-testid='edit-note'] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Note is required.", cut.Find($"#note-{note.Id}-body-error").TextContent));
    }

    [Fact]
    public void Delete_AsksForConfirmationAndNothingIsSentUntilConfirmed()
    {
        var note = Note("bye");
        Notes = NotesPage([note]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            var details = cut.Find("[data-testid='delete-note']");
            Assert.False(details.HasAttribute("open"));
            Assert.Contains("Delete this note?", details.TextContent);
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Delete_Confirmed_SendsDeleteAndReturnsToTheSamePage()
    {
        var note = Note("bye");
        Notes = NotesPage([note]);
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderSection();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='delete-note'] form")));

        cut.Find("[data-testid='delete-note'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}", CurrentUri));
        var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/notes/{note.Id}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void ApiUnavailable_ShowsFriendlyMessageInsteadOfTheList()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));
        Notes = null!;

        var cut = RenderSection();

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
        RegisterApi(request => new HttpResponseMessage(status));
        Notes = null!;

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }
}

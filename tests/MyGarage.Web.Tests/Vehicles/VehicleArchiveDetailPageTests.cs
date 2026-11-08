using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

// The archive, restore, and delete controls on the vehicle page and how an archived vehicle looks (specs/014-archive-vehicle).
public class VehicleArchiveDetailPageTests : VehicleTestContext
{
    private static readonly DateTimeOffset ArchivedAt = new(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);

    private readonly Guid vehicleId = Guid.NewGuid();

    private IRenderedComponent<VehicleDetail> RenderDetail(string? saved = null)
    {
        if (saved is not null)
        {
            NavigateTo($"/vehicles/{vehicleId}?saved={saved}");
        }

        return Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
    }

    private VehicleDetailDto Archived()
    {
        var basis = Detail(vehicleId);
        return new VehicleDetailDto
        {
            Id = basis.Id, Make = basis.Make, Model = basis.Model, Trim = basis.Trim, Color = basis.Color, Year = basis.Year, Mileage = basis.Mileage,
            Doors = basis.Doors, Tags = [], Images = [], CreatedAt = basis.CreatedAt, UpdatedAt = basis.UpdatedAt, ArchivedAt = ArchivedAt,
        };
    }

    [Fact]
    public void AnActiveVehicle_OffersAConfirmedArchivePanel_NamingTheVehicle()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='archive-vehicle']");
            Assert.Equal("Archive this vehicle", panel.QuerySelector("summary")!.GetAttribute("title"));
            Assert.Contains("Archive 2020 Ford F-150? It leaves your garage figures and its reminders are disabled, but all its history is kept and you can restore it any time.", panel.TextContent);
            Assert.Equal("Yes, archive this vehicle", panel.QuerySelector("button")!.TextContent.Trim());
            Assert.Equal("Keep vehicle active", panel.QuerySelector("a")!.TextContent.Trim());
            Assert.Equal($"/vehicles/{vehicleId}", panel.QuerySelector("a")!.GetAttribute("href"));
            Assert.NotNull(panel.QuerySelector("form"));
            Assert.Empty(cut.FindAll("[data-testid='archived-alert']"));
            Assert.Empty(cut.FindAll("details[data-testid='restore-vehicle']"));
        });
    }

    [Fact]
    public void Archive_NothingIsSentUntilConfirmed()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='archive-vehicle']")));

        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Archive_Confirmed_PostsAndReturnsToTheVehicleWithANotice()
    {
        RegisterApi(request => request.Method == HttpMethod.Post ? Json(Archived()) : Json(Detail(vehicleId)));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='archive-vehicle'] form")));

        cut.Find("details[data-testid='archive-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?saved=archived", Services.GetRequiredService<NavigationManager>().Uri));
        var post = Requests.Single(request => request.Method == HttpMethod.Post);
        Assert.Equal($"/api/vehicles/{vehicleId}/archive", post.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Archive_AlreadyArchivedElsewhere_ShowsTheCurrentStateWithAMessage()
    {
        var archived = false;
        RegisterApi(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                archived = true;
                return Problem(HttpStatusCode.Conflict, "This vehicle is already archived.");
            }

            return Json(archived ? Archived() : Detail(vehicleId));
        });
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='archive-vehicle'] form")));

        cut.Find("details[data-testid='archive-vehicle'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("This vehicle was already archived.", cut.Find("[data-testid='state-message']").TextContent.Trim());
            Assert.NotNull(cut.Find("[data-testid='archived-alert']"));
        });
    }

    [Fact]
    public void Archive_OfAVehicleThatIsGone_ShowsTheNotFoundMessage()
    {
        RegisterApi(request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(Detail(vehicleId)));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='archive-vehicle'] form")));

        cut.Find("details[data-testid='archive-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotFound, cut.Markup));
    }

    [Fact]
    public void Archive_WhenTheSignInIsRejected_ShowsTheFailureAlert()
    {
        RegisterApi(request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(Detail(vehicleId)));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='archive-vehicle'] form")));

        cut.Find("details[data-testid='archive-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NotAccepted, cut.Markup));
    }

    [Fact]
    public void AnArchivedVehicle_ShowsTheAlertAndBadgeWithTheDate_AndNoEditOrAddControls()
    {
        RegisterApi(_ => Json(Archived()));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='archived-alert']");
            Assert.Equal("Archived", alert.QuerySelector("[data-testid='archived-badge']")!.TextContent.Trim());
            Assert.Contains("This vehicle was archived on Nov 8, 2026.", alert.TextContent);
            Assert.Empty(cut.FindAll("a[data-testid='edit-vehicle']"));
            Assert.Empty(cut.FindAll("[data-testid='add-service-record']"));
            Assert.Empty(cut.FindAll("details[data-testid='archive-vehicle']"));
            Assert.NotNull(cut.Find("details[data-testid='delete-vehicle']"));
            Assert.Equal("2020 Ford F-150", cut.Find("h1").TextContent);
        });
    }

    [Fact]
    public void AnArchivedVehicle_OffersAConfirmedRestorePanel()
    {
        RegisterApi(_ => Json(Archived()));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='restore-vehicle']");
            Assert.Equal("Restore this vehicle", panel.QuerySelector("summary")!.GetAttribute("title"));
            Assert.Contains("Restore 2020 Ford F-150? It returns to your garage and its reminders are enabled again.", panel.TextContent);
            Assert.Equal("Yes, restore this vehicle", panel.QuerySelector("button")!.TextContent.Trim());
            Assert.Equal("Keep archived", panel.QuerySelector("a")!.TextContent.Trim());
        });
    }

    [Fact]
    public void Restore_Confirmed_PostsAndReturnsToTheVehicleWithANotice()
    {
        RegisterApi(request => request.Method == HttpMethod.Post ? Json(Detail(vehicleId)) : Json(Archived()));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='restore-vehicle'] form")));

        cut.Find("details[data-testid='restore-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?saved=restored", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{vehicleId}/restore", Requests.Single(request => request.Method == HttpMethod.Post).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Restore_AlreadyActiveElsewhere_ShowsTheCurrentStateWithAMessage()
    {
        var restored = false;
        RegisterApi(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                restored = true;
                return Problem(HttpStatusCode.Conflict, "This vehicle is not archived.");
            }

            return Json(restored ? Detail(vehicleId) : Archived());
        });
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='restore-vehicle'] form")));

        cut.Find("details[data-testid='restore-vehicle'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("This vehicle was already active.", cut.Find("[data-testid='state-message']").TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='archived-alert']"));
        });
    }

    [Theory]
    [InlineData("archived", "Vehicle archived. Find it under Archived in My Vehicles.")]
    [InlineData("restored", "Vehicle restored.")]
    public void TheSavedValue_ShowsASuccessNotice(string saved, string text)
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail(saved);

        cut.WaitForAssertion(() =>
        {
            var notice = cut.Find("[data-testid='vehicle-saved']");
            Assert.Equal("status", notice.GetAttribute("role"));
            Assert.Equal(text, notice.TextContent.Trim());
        });
    }

    [Fact]
    public void AnUnknownSavedValue_IsIgnored()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail("whatever");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("h1")));
        Assert.Empty(cut.FindAll("[data-testid='vehicle-saved']"));
    }

    [Fact]
    public void TheDeleteConfirmation_OfAnActiveVehicle_PointsToArchivingAsTheWayToKeepTheHistory()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("details[data-testid='delete-vehicle'] p").TextContent;
            Assert.Contains("reminders, pictures, and kept documents and cannot be undone.", text);
            Assert.EndsWith("To keep the history instead, archive the vehicle.", text.Trim());
        });
    }

    [Fact]
    public void TheDeleteConfirmation_OfAnArchivedVehicle_KeepsTheCurrentTextWithoutTheArchiveHint()
    {
        RegisterApi(_ => Json(Archived()));

        var cut = RenderDetail();

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("details[data-testid='delete-vehicle'] p").TextContent;
            Assert.Contains("cannot be undone.", text);
            Assert.DoesNotContain("archive the vehicle", text);
        });
    }

    [Fact]
    public void Delete_OfAnArchivedVehicle_SendsDeleteAndReturnsToTheGarage()
    {
        RegisterApi(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(Archived()));
        var cut = RenderDetail();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("details[data-testid='delete-vehicle'] form")));

        cut.Find("details[data-testid='delete-vehicle'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/vehicles", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal($"/api/vehicles/{vehicleId}", Requests.Single(request => request.Method == HttpMethod.Delete).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void KeepVehicle_Cancels_WithoutAnyChange()
    {
        RegisterApi(_ => Json(Detail(vehicleId)));
        var cut = RenderDetail();

        cut.WaitForAssertion(() => Assert.Equal($"/vehicles/{vehicleId}", cut.Find("details[data-testid='delete-vehicle'] a").GetAttribute("href")));

        Assert.DoesNotContain(Requests, request => request.Method != HttpMethod.Get);
    }
}

using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleGallerySectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static VehicleImageDto Image(bool isMain = false, string? caption = null) =>
        new() { Id = Guid.NewGuid(), Caption = caption, IsMain = isMain, ContentType = "image/png", SizeBytes = 100, CreatedAt = DateTimeOffset.UtcNow };

    private IRenderedComponent<VehicleGallerySection> RenderGallery(IReadOnlyList<VehicleImageDto> images, string? status = null) =>
        Render<VehicleGallerySection>(parameters => parameters
            .Add(gallery => gallery.VehicleId, vehicleId)
            .Add(gallery => gallery.VehicleName, "2020 Ford F-150")
            .Add(gallery => gallery.Images, images)
            .Add(gallery => gallery.Status, status));

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void NoImages_ShowsAnEmptyStateAndTheUploadForm()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([]);

        Assert.Contains("No pictures yet", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='image']"));
        Assert.NotNull(cut.Find("form[data-testid='upload-image']"));
    }

    [Fact]
    public void DeleteConfirmation_UsesTheStandardWording()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([Image(isMain: true)]);

        var confirmation = cut.Find("details[data-testid='delete-image']");
        Assert.Equal("Yes, delete this picture", confirmation.QuerySelector("button")!.TextContent.Trim());
        Assert.Equal("Keep picture", confirmation.QuerySelector("a.btn")!.TextContent.Trim());
    }

    [Fact]
    public void OnTheEditPage_UploadAsksTheEndpointToReturnThere_AndChangesStayOnTheTab()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var returnPath = $"/vehicles/{vehicleId}/edit?tab=media";
        var cut = Render<VehicleGallerySection>(parameters => parameters
            .Add(gallery => gallery.VehicleId, vehicleId)
            .Add(gallery => gallery.VehicleName, "2020 Ford F-150")
            .Add(gallery => gallery.Images, new[] { Image(isMain: true), Image() })
            .Add(gallery => gallery.ReturnPath, returnPath));

        Assert.Equal("edit", cut.Find("form[data-testid='upload-image'] input[name='returnTo']").GetAttribute("value"));

        cut.Find("[data-testid='image']:nth-child(2) form").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith(returnPath, CurrentUri));
    }

    [Fact]
    public void OnTheVehiclePage_UploadSendsNoReturnTarget()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([]);

        Assert.Empty(cut.FindAll("input[name='returnTo']"));
    }

    [Fact]
    public void UploadForm_PostsMultipartToTheVehicleImageRoute()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([]);

        var form = cut.Find("form[data-testid='upload-image']");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal($"/vehicles/{vehicleId}/images", form.GetAttribute("action"));
        Assert.Equal("multipart/form-data", form.GetAttribute("enctype"));
        var file = cut.Find("input#image-file");
        Assert.Equal("file", file.GetAttribute("type"));
        Assert.Equal("file", file.GetAttribute("name"));
        Assert.Equal("image/jpeg,image/png,image/webp", file.GetAttribute("accept"));
        Assert.True(file.HasAttribute("required"));
        Assert.True(file.HasAttribute("multiple"));
        Assert.NotNull(cut.Find("label[for='image-file']"));
    }

    [Fact]
    public void UploadForm_IsAClickOrDropAreaWithNoCaptionField()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([]);

        var form = cut.Find("form[data-testid='upload-image']");
        Assert.True(form.HasAttribute("data-dropzone"));
        Assert.Contains("mg-dropzone", form.ClassList);
        Assert.Contains("Drag & Drop Images Here", form.TextContent);
        Assert.Contains("or click to browse", form.TextContent);
        Assert.Contains("JPG, PNG, WebP", form.TextContent);
        Assert.Contains("visually-hidden", form.QuerySelector("label[for='image-file']")!.ClassList);
        Assert.Empty(cut.FindAll("input#image-caption"));
        Assert.Empty(cut.FindAll("input[name='caption']"));
        Assert.NotNull(form.QuerySelector("svg.bi[aria-hidden='true']"));
    }

    [Fact]
    public void UploadForm_OffersAnUploadButtonOnlyWhenScriptIsOff()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([]);

        Assert.Contains("<noscript>", cut.Find("form[data-testid='upload-image']").InnerHtml);
        Assert.Empty(cut.FindAll("form[data-testid='upload-image'] > button"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void UploadedHeading_CountsThePictures(int count)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var images = Enumerable.Range(0, count).Select(i => Image(isMain: i == 0)).ToList();

        var cut = RenderGallery(images);

        var heading = cut.Find("h3[data-testid='uploaded-heading']");
        Assert.Equal($"Uploaded Images ({count})", heading.TextContent.Trim());
        Assert.Equal(count, cut.FindAll("[data-testid='image']").Count);
    }

    [Fact]
    public void AtTheLimit_TheHeadingStillCountsAndTilesStayVisible()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var images = Enumerable.Range(0, 10).Select(i => Image(isMain: i == 0)).ToList();

        var cut = RenderGallery(images);

        Assert.Equal("Uploaded Images (10)", cut.Find("[data-testid='uploaded-heading']").TextContent.Trim());
        Assert.Equal(10, cut.FindAll("[data-testid='image'] .mg-image-tile").Count);
    }

    [Fact]
    public void EachTile_HasAnIconOnlyDeleteButtonWithAnAccessibleName()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([Image(isMain: true, caption: "Front")]);

        var summary = cut.Find("[data-testid='image'] details[data-testid='delete-image'] summary");
        Assert.Equal("Delete picture Front", summary.GetAttribute("aria-label"));
        Assert.NotNull(summary.QuerySelector("svg.bi"));
        Assert.Equal(string.Empty, summary.TextContent.Trim());
    }

    [Fact]
    public void Images_AreLazyThumbnailsWithAltTextFromTheCaptionOrTheVehicleName()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var captioned = Image(isMain: true, caption: "Front view");
        var plain = Image();

        var cut = RenderGallery([captioned, plain]);

        var images = cut.FindAll("[data-testid='image'] img");
        Assert.Equal(2, images.Count);
        Assert.Equal($"/vehicles/{vehicleId}/images/{captioned.Id}", images[0].GetAttribute("src"));
        Assert.Equal("Front view", images[0].GetAttribute("alt"));
        Assert.Equal("lazy", images[0].GetAttribute("loading"));
        Assert.Equal("2020 Ford F-150", images[1].GetAttribute("alt"));
    }

    [Fact]
    public void OnlyTheMainImage_IsMarkedMainAndHasNoMakeMainButton()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var main = Image(isMain: true);
        var other = Image();

        var cut = RenderGallery([main, other]);

        var tiles = cut.FindAll("[data-testid='image']");
        Assert.NotNull(tiles[0].QuerySelector("[data-testid='main-badge']"));
        Assert.Null(tiles[1].QuerySelector("[data-testid='main-badge']"));
        Assert.Empty(tiles[0].QuerySelectorAll("button[aria-label^='Make']"));
        Assert.Single(tiles[1].QuerySelectorAll("button[aria-label^='Make']"));
    }

    [Fact]
    public void CaptionText_IsEncoded()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([Image(caption: "<b>bold</b>")]);

        Assert.Empty(cut.FindAll("[data-testid='image'] b"));
        Assert.Contains("<b>bold</b>", cut.Find("[data-testid='image'] .mg-image-caption").TextContent);
    }

    [Fact]
    public void AtTheLimit_TheUploadFormIsReplacedByAMessage()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var images = Enumerable.Range(0, 10).Select(i => Image(isMain: i == 0)).ToList();

        var cut = RenderGallery(images);

        Assert.NotNull(cut.Find("[data-testid='image-limit']"));
        Assert.Empty(cut.FindAll("form[data-testid='upload-image']"));
        Assert.Empty(cut.FindAll("input#image-file"));
    }

    [Fact]
    public void MakeMain_PutsThenReturnsToTheVehicle()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var main = Image(isMain: true);
        var other = Image();
        var cut = RenderGallery([main, other]);

        cut.Find("[data-testid='image']:nth-child(2) form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}", CurrentUri));
        var put = Requests.Single();
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal($"/api/vehicles/{vehicleId}/images/{other.Id}/main", put.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Delete_AsksForConfirmationAndNothingIsSentUntilConfirmed()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([Image(isMain: true)]);

        var details = cut.Find("[data-testid='delete-image']");
        Assert.False(details.HasAttribute("open"));
        Assert.Contains("Delete this picture?", details.TextContent);
        Assert.Empty(Requests);
    }

    [Fact]
    public void Delete_Confirmed_SendsDeleteAndReturnsToTheVehicle()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var image = Image(isMain: true);
        var cut = RenderGallery([image]);

        cut.Find("[data-testid='delete-image'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}", CurrentUri));
        var delete = Requests.Single();
        Assert.Equal(HttpMethod.Delete, delete.Method);
        Assert.Equal($"/api/vehicles/{vehicleId}/images/{image.Id}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Buttons_HaveAccessibleNamesThatIdentifyThePicture()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([Image(isMain: true, caption: "Front"), Image(caption: "Rear")]);

        Assert.Equal("Delete picture Front", cut.FindAll("summary")[0].GetAttribute("aria-label"));
        Assert.Equal("Make Rear the main picture", cut.Find("button[aria-label^='Make']").GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData("uploaded", "Picture added.", "success")]
    [InlineData("invalid", "not a valid JPEG, PNG, or WebP", "danger")]
    [InlineData("toolarge", "larger than 5 MB", "danger")]
    [InlineData("limit", "already has 10 pictures", "danger")]
    [InlineData("unavailable", "Nothing was saved", "warning")]
    [InlineData("notfound", "could not be found", "warning")]
    [InlineData("signin", "sign in again", "warning")]
    public void UploadStatus_ShowsAMatchingMessage(string status, string expectedText, string level)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([], status);

        var notice = cut.Find("[data-testid='image-notice']");
        Assert.Contains(expectedText, notice.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"alert-{level}", notice.ClassName);
    }

    [Fact]
    public void UnknownStatus_ShowsNoMessage_AndNeverEchoesTheValue()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderGallery([], "<script>alert(1)</script>");

        Assert.Empty(cut.FindAll("[data-testid='image-notice']"));
        Assert.DoesNotContain("alert(1)", cut.Markup);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, VehicleMessages.NotAccepted)]
    [InlineData(HttpStatusCode.Forbidden, VehicleMessages.NotAccepted)]
    public void ApiRejectingTheToken_ShowsSignInAgain(HttpStatusCode status, string message)
    {
        RegisterApi(_ => new HttpResponseMessage(status));
        var cut = RenderGallery([Image(isMain: true)]);

        cut.Find("[data-testid='delete-image'] form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(message, cut.Find("[data-testid='image-notice']").TextContent));
    }

    [Fact]
    public void ApiOutage_ShowsAFriendlyMessage()
    {
        RegisterApi(_ => throw new HttpRequestException("boom"));
        var cut = RenderGallery([Image(isMain: true)]);

        cut.Find("[data-testid='delete-image'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.Unavailable, cut.Find("[data-testid='image-notice']").TextContent);
            Assert.DoesNotContain("boom", cut.Markup);
        });
    }
}

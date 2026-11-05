using System.Net;
using Bunit;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class DraftUploadPageTests : DraftTestContext
{
    private IRenderedComponent<DraftUploadPage> RenderPage(string uri = "/drafts/upload")
    {
        RegisterDrafts();
        NavigateTo(uri);
        return Render<DraftUploadPage>();
    }

    [Fact]
    public void ShowsTheTitleTrailAndWhatHappensNext()
    {
        var cut = RenderPage();

        Assert.Equal("Add Maintenance", cut.Find("h1").TextContent);
        Assert.Equal(["Home", "Maintenance Drafts", "Add maintenance"], cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim()));
        Assert.Equal("/drafts", cut.Find("nav[aria-label='Breadcrumb'] li a[href='/drafts']").GetAttribute("href"));
        var text = cut.Find("[data-testid='upload-record']").TextContent;
        Assert.Contains("PDF, JPG, PNG, or WebP", text);
        Assert.Contains("Nothing becomes a record until you review and include it.", text);
    }

    [Fact]
    public void HasAMultipartPostFormNamedUploadRecordWithALabelledMultipleFileInput()
    {
        var cut = RenderPage();

        var form = cut.Find("form");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("multipart/form-data", form.GetAttribute("enctype"));
        var input = form.QuerySelector("input[type='file']")!;
        Assert.Equal("recordFiles", input.GetAttribute("name"));
        Assert.True(input.HasAttribute("multiple"));
        Assert.True(input.HasAttribute("required"));
        Assert.Equal(DraftUploadLimits.Accept, input.GetAttribute("accept"));
        Assert.Equal("record-files-help", input.GetAttribute("aria-describedby"));
        Assert.Equal("Choose the files", cut.Find("label[for='record-files']").TextContent);
        Assert.Contains("Up to 5 files, 10 MB each, and 15 MB in total.", cut.Find("#record-files-help").TextContent);
    }

    [Fact]
    public void TheSubmitButtonHasABusyStateAndTheFormSaysReadingHappensAfterTheUpload()
    {
        var cut = RenderPage();

        var button = cut.Find("button[data-testid='upload-submit']");
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Contains("Upload", button.TextContent);
        Assert.Equal("Uploading...", button.GetAttribute("data-busy-text"));
        Assert.Contains("Reading starts after the upload", cut.Find("form").TextContent);
    }

    private IRenderedComponent<DraftUploadPage> RenderForVehicle(string vehicleIdText, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        NavigateTo($"/drafts/upload?vehicleId={vehicleIdText}");
        return Render<DraftUploadPage>();
    }

    [Fact]
    public void WithAVehicle_ShowsItsNameAndCarriesItInTheForm()
    {
        var id = Guid.NewGuid();

        var cut = RenderForVehicle(id.ToString(), request =>
            request.RequestUri!.AbsolutePath == $"/api/vehicles/{id}" ? Json(Detail(id, "Ford", "F-150", 2020)) : new HttpResponseMessage(HttpStatusCode.NotFound));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Import Record", cut.Find("h1").TextContent);
            Assert.Equal("Importing a record for 2020 Ford F-150.", cut.Find("[data-testid='upload-vehicle']").TextContent.Trim());
            Assert.Equal(id.ToString(), cut.Find("input[type='hidden'][name='vehicleId']").GetAttribute("value"));
        });
    }

    [Fact]
    public void AVehicleName_IsRenderedEscaped()
    {
        var id = Guid.NewGuid();

        var cut = RenderForVehicle(id.ToString(), _ => Json(Detail(id, "<b>Ford</b>", "F-150", 2020)));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='upload-vehicle'] b"));
            Assert.Contains("<b>Ford</b>", cut.Find("[data-testid='upload-vehicle']").TextContent);
        });
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void AMalformedOrEmptyVehicleId_IsIgnored(string value)
    {
        var cut = RenderForVehicle(value);

        Assert.Empty(cut.FindAll("[data-testid='upload-vehicle']"));
        Assert.Empty(cut.FindAll("input[name='vehicleId']"));
        Assert.NotNull(cut.Find("form"));
        Assert.DoesNotContain(Requests, request => request.RequestUri!.AbsolutePath.StartsWith("/api/vehicles/", StringComparison.Ordinal));
    }

    [Fact]
    public void AVehicleTheApiDoesNotKnow_ShowsTheNotFoundMessageAndNoForm()
    {
        var cut = RenderForVehicle(Guid.NewGuid().ToString());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(DraftUploadMessages.NotFound, cut.Markup);
            Assert.Empty(cut.FindAll("form"));
            Assert.Equal("/vehicles", cut.Find("a[href='/vehicles']").GetAttribute("href"));
        });
    }

    [Fact]
    public void WhenTheVehicleLookupFails_TheStandardFailureAlertReplacesTheForm()
    {
        var cut = RenderForVehicle(Guid.NewGuid().ToString(), _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[role='alert']"));
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void WithoutAVehicle_ThereIsNoVehicleNameAndNoHiddenVehicleField()
    {
        var cut = RenderPage();

        Assert.Empty(cut.FindAll("[data-testid='upload-vehicle']"));
        Assert.Empty(cut.FindAll("input[name='vehicleId']"));
        Assert.DoesNotContain(Requests, request => request.RequestUri!.AbsolutePath.StartsWith("/api/vehicles/", StringComparison.Ordinal));
    }
}

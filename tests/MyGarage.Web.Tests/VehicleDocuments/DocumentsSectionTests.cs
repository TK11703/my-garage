using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.VehicleDocuments;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.VehicleDocuments;

namespace MyGarage.Web.Tests.VehicleDocuments;

public class DocumentsSectionTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private PagedResultDto<VehicleDocumentDto> documents = Documents([]);
    private HttpStatusCode? listFailure;
    private Func<HttpResponseMessage>? onDelete;

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static PagedResultDto<VehicleDocumentDto> Documents(IReadOnlyList<VehicleDocumentDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<VehicleDocumentDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    private static VehicleDocumentDto Document(
        string fileName = "invoice.pdf", string contentType = "application/pdf", long size = 120_000, DraftKind? source = DraftKind.ServiceRecord) =>
        new()
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = size,
            SourceKind = source,
            SourceRecordId = source is null ? null : Guid.NewGuid(),
            CreatedAt = new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero),
        };

    private IRenderedComponent<DocumentsSection> RenderSection(int page = 1, bool expanded = false)
    {
        // The shared context answers the documents list itself, so this test's page and failure go through its responder.
        MaintenanceListResponders["documents"] = _ => listFailure is { } status ? new HttpResponseMessage(status) : Json(documents);
        RegisterApi(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                return onDelete?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return Render<DocumentsSection>(parameters => parameters
            .Add(section => section.VehicleId, vehicleId)
            .Add(section => section.Page, page)
            .Add(section => section.Expanded, expanded));
    }

    [Fact]
    public void IsAnAccordionItemNamedDocuments_WithItsCount()
    {
        documents = Documents([Document(), Document()]);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='accordion-documents']"));
            var button = cut.Find(".accordion-header button");
            Assert.Equal("collapse", button.GetAttribute("data-bs-toggle"));
            Assert.Equal("#maintenance-documents", button.GetAttribute("data-bs-target"));
            Assert.Equal("maintenance-documents", button.GetAttribute("aria-controls"));
            Assert.Equal("Documents (2)", cut.Find("[data-testid='documents-count']").TextContent.Trim());
            Assert.DoesNotContain("show", cut.Find("#maintenance-documents").ClassList);
            Assert.Empty(cut.FindAll("[data-bs-parent]"));
        });
    }

    [Fact]
    public void WhenExpanded_TheItemStartsOpen()
    {
        var cut = RenderSection(expanded: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("show", cut.Find("#maintenance-documents").ClassList);
            Assert.Equal("true", cut.Find(".accordion-header button").GetAttribute("aria-expanded"));
        });
    }

    [Fact]
    public void WithNoDocuments_ShowsTheEmptyState()
    {
        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No documents kept yet.", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='document']"));
        });
    }

    [Fact]
    public void Documents_AreListedWithTheirDetails_AndAViewLinkThroughTheProxy()
    {
        var pdf = Document("invoice.pdf", "application/pdf", 120_000, DraftKind.ServiceRecord);
        var photo = Document("receipt.png", "image/png", 2_500_000, DraftKind.Fuel);
        var orphan = Document("scan.jpg", "image/jpeg", 800, null);
        documents = Documents([pdf, photo, orphan]);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='document']");
            Assert.Equal(3, rows.Count);
            Assert.Equal("invoice.pdf", rows[0].QuerySelector("[data-testid='document-name']")!.TextContent.Trim());
            Assert.Equal("PDF", rows[0].QuerySelector("[data-testid='document-type']")!.TextContent.Trim());
            Assert.Equal("117 KB", rows[0].QuerySelector("[data-testid='document-size']")!.TextContent.Trim());
            Assert.Equal("From service record", rows[0].QuerySelector("[data-testid='document-source']")!.TextContent.Trim());
            Assert.Equal("Kept Nov 3, 2026", rows[0].QuerySelector("[data-testid='document-kept']")!.TextContent.Trim());

            var view = rows[0].QuerySelector("a[data-testid='view-document']")!;
            Assert.Equal($"/vehicles/{vehicleId}/documents/{pdf.Id}", view.GetAttribute("href"));
            Assert.Equal("_blank", view.GetAttribute("target"));
            Assert.Contains("noopener", view.GetAttribute("rel"));
            Assert.Equal("View invoice.pdf", view.GetAttribute("aria-label"));
            Assert.Contains("btn-sm", view.ClassList);

            Assert.Equal("Image", rows[1].QuerySelector("[data-testid='document-type']")!.TextContent.Trim());
            Assert.Equal("2.4 MB", rows[1].QuerySelector("[data-testid='document-size']")!.TextContent.Trim());
            Assert.Equal("From fuel", rows[1].QuerySelector("[data-testid='document-source']")!.TextContent.Trim());

            Assert.Equal("800 bytes", rows[2].QuerySelector("[data-testid='document-size']")!.TextContent.Trim());
            Assert.Equal("Kept from an email", rows[2].QuerySelector("[data-testid='document-source']")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("table"));
        });
    }

    [Fact]
    public void FileNames_AreEncoded()
    {
        documents = Documents([Document("<b>x</b>.pdf")]);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Equal("<b>x</b>.pdf", cut.Find("[data-testid='document-name']").TextContent.Trim());
        });
    }

    [Fact]
    public void Delete_AsksForConfirmationNamingTheDocument_AndSendsNothingUntilConfirmed()
    {
        documents = Documents([Document("invoice.pdf")]);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("details[data-testid='delete-document']");
            Assert.False(panel.HasAttribute("open"));
            Assert.Equal("Delete invoice.pdf", panel.QuerySelector("summary")!.GetAttribute("aria-label"));
            Assert.Contains("Delete this document? This cannot be undone.", panel.TextContent);
            Assert.Equal("Yes, delete this document", panel.QuerySelector("button[type='submit']")!.TextContent.Trim());
            Assert.Equal("Keep document", panel.QuerySelector("a.btn")!.TextContent.Trim());
            Assert.Equal("alert", panel.QuerySelector(".alert")!.GetAttribute("role"));
        });
        Assert.DoesNotContain(Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingDelete_CallsTheApi_AndReturnsToTheDocumentsItem()
    {
        var document = Document();
        documents = Documents([document]);
        var cut = RenderSection();
        cut.WaitForElement("details[data-testid='delete-document'] form");

        cut.Find("details[data-testid='delete-document'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=documents", CurrentUri));
        var delete = Assert.Single(Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal($"/api/vehicles/{vehicleId}/documents/{document.Id}", delete.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void AFailedDelete_ShowsAMessage_AndStaysPut()
    {
        documents = Documents([Document()]);
        onDelete = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var cut = RenderSection();
        cut.WaitForElement("details[data-testid='delete-document'] form");

        cut.Find("details[data-testid='delete-document'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.Unavailable, cut.Find("[data-testid='document'] .alert-warning").TextContent);
            Assert.DoesNotContain("/vehicles/", CurrentUri);
        });
    }

    [Fact]
    public void Pager_KeepsTheDocumentsItemOpen_AndUsesDocumentsPage()
    {
        documents = Documents([Document()], page: 2, totalCount: 25);

        var cut = RenderSection(page: 2, expanded: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=documents", cut.Find("[data-testid='pager-previous'] a").GetAttribute("href"));
            Assert.Equal($"/vehicles/{vehicleId}?tab=service-records&section=documents&documentsPage=3", cut.Find("[data-testid='pager-next'] a").GetAttribute("href"));
            Assert.Contains(Requests, request => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AnApiFailure_ShowsAnAlert_AndTheItemStillRenders()
    {
        listFailure = HttpStatusCode.InternalServerError;

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[role='alert']"));
            Assert.Empty(cut.FindAll("[data-testid='document']"));
            Assert.NotNull(cut.Find("[data-testid='accordion-documents']"));
        });
    }

    [Fact]
    public void AnUnknownVehicle_IsTreatedAsHavingNoDocuments()
    {
        listFailure = HttpStatusCode.NotFound;

        var cut = RenderSection();

        cut.WaitForAssertion(() => Assert.Contains("No documents kept yet.", cut.Markup));
    }
}

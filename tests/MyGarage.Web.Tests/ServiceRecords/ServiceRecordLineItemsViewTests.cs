using System.Net;
using Bunit;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

public class ServiceRecordLineItemsViewTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();

    private static ServiceLineItemDto FlatLine(string description = "Tire rotation", decimal cost = 40m) =>
        new() { Id = Guid.NewGuid(), Description = description, FlatCost = cost, Parts = [], TotalCost = cost };

    private static ServiceLineItemDto OilChange() =>
        new()
        {
            Id = Guid.NewGuid(),
            Description = "Oil change",
            LaborHours = 1.5m,
            LaborRate = 90m,
            LaborCost = 135m,
            Parts =
            [
                new ServicePartDto { Name = "Oil filter", Quantity = 1m, UnitPrice = 12.50m, TotalPrice = 12.50m },
                new ServicePartDto { Name = "Oil", Quantity = 5m, UnitPrice = 8.25m, TotalPrice = 41.25m },
            ],
            TotalCost = 188.75m,
        };

    private IRenderedComponent<ServiceRecordDetail> RenderPage(ServiceRecordDto record, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        RegisterApi(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}")
            {
                return Json(Detail(vehicleId));
            }

            return request.Method == HttpMethod.Get ? Json(record) : respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        return Render<ServiceRecordDetail>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId)
            .Add(page => page.RecordId, recordId));
    }

    [Fact]
    public void LineItems_AreListedInOrderWithTheirPricingAndTotals()
    {
        var oil = OilChange();
        var record = RecordDetail(vehicleId, recordId, lineItems: [FlatLine(), oil]);

        var cut = RenderPage(record);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid='line-item']");
            Assert.Equal(2, rows.Count);

            Assert.Equal("Tire rotation", rows[0].QuerySelector("[data-testid='line-description']")!.TextContent.Trim());
            Assert.Equal("Flat cost $40.00", rows[0].QuerySelector("[data-testid='line-flat']")!.TextContent.Trim());
            Assert.Equal("$40.00", rows[0].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());

            Assert.Equal("Oil change", rows[1].QuerySelector("[data-testid='line-description']")!.TextContent.Trim());
            Assert.Equal("Labor: 1.5 h x $90.00 = $135.00", rows[1].QuerySelector("[data-testid='line-labor']")!.TextContent.Trim());
            Assert.Equal(
                ["1 x Oil filter at $12.50 = $12.50", "5 x Oil at $8.25 = $41.25"],
                rows[1].QuerySelectorAll("[data-testid='line-part']").Select(part => part.TextContent.Trim()));
            Assert.Equal("$188.75", rows[1].QuerySelector("[data-testid='line-total']")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("[data-testid='line-flat']").Skip(1));

            Assert.Equal("$228.75", cut.Find("[data-testid='total-cost']").TextContent.Trim());
            Assert.DoesNotContain("No line items yet", cut.Markup);
        });
    }

    [Fact]
    public void EachLineHasEditAndDeleteWithAccessibleNames_AndThereIsAnAddLink()
    {
        var line = FlatLine("Brake pads");
        var cut = RenderPage(RecordDetail(vehicleId, recordId, lineItems: [line]));

        cut.WaitForAssertion(() =>
        {
            var edit = cut.Find("[data-testid='edit-line-item']");
            Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}/line-items/{line.Id}/edit", edit.GetAttribute("href"));
            Assert.Equal("Edit Brake pads", edit.GetAttribute("aria-label"));
            Assert.Contains("btn-sm", edit.ClassList);

            var delete = cut.Find("details[data-testid='delete-line-item']");
            Assert.Equal("Delete Brake pads", delete.QuerySelector("summary")!.GetAttribute("aria-label"));
            Assert.Contains("btn-sm", delete.QuerySelector("summary")!.ClassList);
            Assert.Contains("Delete this line item? This cannot be undone.", delete.TextContent);

            Assert.Equal($"/vehicles/{vehicleId}/service-records/{recordId}/line-items/new", cut.Find("[data-testid='add-line-item']").GetAttribute("href"));
        });
    }

    [Fact]
    public void ConfirmingALineItemDelete_CallsTheApiAndReturnsToTheRecord()
    {
        var line = FlatLine();
        var cut = RenderPage(RecordDetail(vehicleId, recordId, lineItems: [line]));
        cut.WaitForElement("details[data-testid='delete-line-item'] form");

        cut.Find("details[data-testid='delete-line-item'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            var delete = Requests.Single(request => request.Method == HttpMethod.Delete);
            Assert.Equal($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{line.Id}", delete.RequestUri!.AbsolutePath);
        });
    }

    [Fact]
    public void WithNoLineItems_TheEmptyStateAndAddLinkShowAndTheTotalIsZero()
    {
        var cut = RenderPage(RecordDetail(vehicleId, recordId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No line items yet. Add what was done and what it cost.", cut.Markup);
            Assert.NotNull(cut.Find("[data-testid='add-line-item']"));
            Assert.Equal("$0.00", cut.Find("[data-testid='total-cost']").TextContent.Trim());
        });
    }

    [Fact]
    public void LineItemTextIsEncoded()
    {
        var line = FlatLine("<b>Bold</b>");
        var cut = RenderPage(RecordDetail(vehicleId, recordId, lineItems: [line]));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='line-item'] b"));
            Assert.Contains("<b>Bold</b>", cut.Find("[data-testid='line-description']").TextContent);
        });
    }
}

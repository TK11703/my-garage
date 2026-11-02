using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

// FR-014: service record contents are entered in form bodies and never travel in a page address.
public class ServiceRecordsPrivacyTests : VehicleTestContext
{
    private const string Shop = "QZSHOP-PRIVATE";
    private const string Work = "QZWORK-PRIVATE";
    private const string PartName = "QZPART-PRIVATE";

    private readonly Guid vehicleId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();

    private static readonly string[] Secrets = [Shop, Work, PartName, "7654.32", "98765"];

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static IEnumerable<string?> Addresses(IEnumerable<IElement> elements) =>
        elements.SelectMany(element => new[] { element.GetAttribute("href"), element.GetAttribute("action"), element.GetAttribute("src") });

    private void AssertNoSecretInAddresses(IRenderedComponent<IComponent> cut)
    {
        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(Addresses(cut.FindAll("[href], [action], [src]")), address => address?.Contains(secret, StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(secret, CurrentUri, StringComparison.OrdinalIgnoreCase);
            Assert.All(Requests, request => Assert.DoesNotContain(secret, request.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    private ServiceLineItemDto SecretLine() =>
        new()
        {
            Id = Guid.NewGuid(),
            Description = Work,
            LaborHours = 1m,
            LaborRate = 7654.32m,
            LaborCost = 7654.32m,
            Parts = [new ServicePartDto { Name = PartName, Quantity = 1m, UnitPrice = 7654.32m, TotalPrice = 7654.32m }],
            TotalCost = 15308.64m,
        };

    private void RegisterRecordApi(ServiceRecordDto record) =>
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : request.Method == HttpMethod.Get ? Json(record) : Json(record, System.Net.HttpStatusCode.Created));

    [Fact]
    public void CreatingARecord_RedirectsToAnAddressWithoutTheEnteredValues()
    {
        var created = RecordDetail(vehicleId, recordId, odometer: 98765, shop: Shop);
        RegisterRecordApi(created);
        var cut = Render<ServiceRecordCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
        cut.WaitForElement("input#odometer-reading").Change("98765");
        cut.Find("input#shop-or-person").Change(Shop);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        Assert.NotEqual("get", cut.Find("form").GetAttribute("method")?.ToLowerInvariant());
        AssertNoSecretInAddresses(cut);
    }

    [Fact]
    public void TheRecordAndTheEditPages_NeverPutTheContentsInALinkOrFormAddress()
    {
        var record = RecordDetail(vehicleId, recordId, odometer: 98765, shop: Shop, lineItems: [SecretLine()]);
        RegisterRecordApi(record);

        var viewing = Render<ServiceRecordDetail>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.RecordId, recordId));
        viewing.WaitForAssertion(() => Assert.Contains(Shop, viewing.Markup));
        var editing = Render<ServiceRecordEdit>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.RecordId, recordId));
        editing.WaitForAssertion(() => Assert.Equal(Shop, editing.Find("input#shop-or-person").GetAttribute("value")));
        var lineId = record.LineItems.Single().Id;
        var editingLine = Render<ServiceLineItemEdit>(parameters => parameters
            .Add(page => page.VehicleId, vehicleId).Add(page => page.RecordId, recordId).Add(page => page.LineItemId, lineId));
        editingLine.WaitForAssertion(() => Assert.Equal(Work, editingLine.Find("input#line-description").GetAttribute("value")));

        AssertNoSecretInAddresses(viewing);
        AssertNoSecretInAddresses(editing);
        AssertNoSecretInAddresses(editingLine);
    }

    [Fact]
    public void SavingALineItem_RedirectsToAnAddressWithoutTheEnteredValues()
    {
        var record = RecordDetail(vehicleId, recordId, lineItems: [SecretLine()]);
        RegisterRecordApi(record);
        var cut = Render<ServiceLineItemCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId).Add(page => page.RecordId, recordId));
        cut.WaitForElement("input#line-description").Change(Work);
        cut.Find("input#pricing-breakdown").Change("Breakdown");
        cut.Find("input#labor-hours").Change("1");
        cut.Find("input#labor-rate").Change("7654.32");
        cut.Find("input#part-0-name").Change(PartName);

        cut.Find("form[data-testid='line-item-form']").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}/service-records/{recordId}", CurrentUri));
        AssertNoSecretInAddresses(cut);
    }

    [Theory]
    [InlineData(typeof(ServiceRecordCreate))]
    [InlineData(typeof(ServiceRecordDetail))]
    [InlineData(typeof(ServiceRecordEdit))]
    [InlineData(typeof(ServiceLineItemCreate))]
    [InlineData(typeof(ServiceLineItemEdit))]
    public void EveryServiceRecordPage_RequiresSignIn(Type page) =>
        Assert.NotEmpty(page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
}

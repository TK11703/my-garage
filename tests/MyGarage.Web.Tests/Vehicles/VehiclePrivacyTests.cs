using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

// FR-022: the VIN is entered in a form body and never travels in a page address.
public class VehiclePrivacyTests : VehicleTestContext
{
    private const string Vin = "WZW9WZW9WZW9WZW9W";

    private static IEnumerable<string?> Addresses(IEnumerable<IElement> elements) =>
        elements.SelectMany(element => new[] { element.GetAttribute("href"), element.GetAttribute("action"), element.GetAttribute("src") });

    [Fact]
    public void CreatingAVehicleWithAVin_RedirectsToAnAddressWithoutIt_AndTheFormPostsInTheBody()
    {
        var created = FullDetail(nickname: "Daily");
        RegisterApi(_ => Json(created, System.Net.HttpStatusCode.Created));
        var cut = Render<VehicleCreate>();
        cut.Find("input#nickname").Change("Daily");
        cut.Find("input#make").Change("Ford");
        cut.Find("input#model").Change("F-150");
        cut.Find("input#year").Change("2019");
        cut.Find("input#vin").Change(Vin);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var uri = Services.GetRequiredService<NavigationManager>().Uri;
            Assert.EndsWith($"/vehicles/{created.Id}", uri);
            Assert.DoesNotContain(Vin, uri, StringComparison.OrdinalIgnoreCase);
        });
        Assert.NotEqual("get", cut.Find("form").GetAttribute("method")?.ToLowerInvariant());
        Assert.All(Requests, request => Assert.DoesNotContain(Vin, request.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Addresses(cut.FindAll("[href], [action], [src]")), address => address?.Contains(Vin, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void TheDetailAndEditPages_NeverPutTheVinInALinkOrFormAddress()
    {
        var detail = FullDetail(Guid.NewGuid());
        RegisterApi(_ => Json(new MyGarage.Shared.Vehicles.VehicleDetailDto
        {
            Id = detail.Id, Nickname = detail.Nickname, Make = detail.Make, Model = detail.Model, Vin = Vin, Year = 2019,
            Tags = [], Images = [], CreatedAt = detail.CreatedAt, UpdatedAt = detail.UpdatedAt,
        }));

        var viewing = Render<VehicleDetail>(parameters => parameters.Add(page => page.VehicleId, detail.Id));
        viewing.WaitForAssertion(() => Assert.Contains(Vin, viewing.Markup));
        var editing = Render<VehicleEdit>(parameters => parameters.Add(page => page.VehicleId, detail.Id));
        editing.WaitForAssertion(() => Assert.Equal(Vin, editing.Find("input#vin").GetAttribute("value")));

        Assert.DoesNotContain(Addresses(viewing.FindAll("[href], [action], [src]")), address => address?.Contains(Vin, StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(Addresses(editing.FindAll("[href], [action], [src]")), address => address?.Contains(Vin, StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(Requests, request => Assert.DoesNotContain(Vin, request.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(typeof(VehicleList))]
    [InlineData(typeof(VehicleCreate))]
    [InlineData(typeof(VehicleEdit))]
    [InlineData(typeof(VehicleDetail))]
    [InlineData(typeof(MyGarage.Web.Preferences.Preferences))]
    public void EveryVehicleAndPreferencesPage_RequiresSignIn(Type page) =>
        Assert.NotEmpty(page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
}

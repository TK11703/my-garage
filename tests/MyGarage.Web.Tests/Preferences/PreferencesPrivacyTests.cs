using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Lookups;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Preferences;

// FR-022: custom item names are entered in a form body and never travel in a page address.
public class PreferencesPrivacyTests : VehicleTestContext
{
    private const string Secret = "Zq-Private-Item-77";

    [Fact]
    public void AddingAnItem_RedirectsToAnAddressWithoutItsName_AndNoLinkOrFormActionCarriesIt()
    {
        RegisterApi(_ => Json(new LookupItemDto { Id = Guid.NewGuid(), Name = Secret, IsDefault = false, IsHidden = false }, HttpStatusCode.Created));
        Lookups = new LookupsDto
        {
            VehicleTypes = [], FuelTypes = [Item(Secret, isDefault: false), Item("Gasoline")], Transmissions = [], Drivetrains = [],
        };
        var cut = Render<MyGarage.Web.Preferences.Preferences>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("input#lookup-fuel-types-name")));
        cut.Find("input#lookup-fuel-types-name").Change(Secret);

        cut.Find("form[data-testid='add-lookup-fuel-types']").Submit();

        cut.WaitForAssertion(() =>
        {
            var uri = Services.GetRequiredService<NavigationManager>().Uri;
            Assert.EndsWith("/preferences?tab=fuel-types", uri);
            Assert.DoesNotContain(Secret, uri, StringComparison.OrdinalIgnoreCase);
        });
        var addresses = cut.FindAll("[href], [action]").SelectMany(element => new[] { element.GetAttribute("href"), element.GetAttribute("action") });
        Assert.DoesNotContain(addresses, address => address?.Contains(Secret, StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(Requests, request => Assert.DoesNotContain(Secret, request.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.All(cut.FindAll("form"), form => Assert.NotEqual("get", form.GetAttribute("method")?.ToLowerInvariant()));
    }
}

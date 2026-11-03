using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.FuelEnergyEntries;
using MyGarage.Web.Inspections;
using MyGarage.Web.Insurance;
using MyGarage.Web.Lookups;
using MyGarage.Web.Registrations;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Taxes;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.ServiceRecords;

// The component tests register the API clients themselves, so this guards the real host's registrations.
public class ApiClientRegistrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public ApiClientRegistrationTests(WebAppFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(typeof(VehiclesApiClient))]
    [InlineData(typeof(LookupsApiClient))]
    [InlineData(typeof(ServiceRecordsApiClient))]
    [InlineData(typeof(FuelEnergyEntriesApiClient))]
    [InlineData(typeof(InspectionsApiClient))]
    [InlineData(typeof(RegistrationsApiClient))]
    [InlineData(typeof(InsuranceApiClient))]
    [InlineData(typeof(TaxesApiClient))]
    public void EveryTypedApiClient_ResolvesFromTheRealHost(Type client)
    {
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(client));
    }
}

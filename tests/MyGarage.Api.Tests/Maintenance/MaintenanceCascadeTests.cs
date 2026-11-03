using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Taxes;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Maintenance;

public class MaintenanceCascadeTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeletingTheVehicle_Returns204AndEveryKindOfEntryIsGone()
    {
        var client = factory.CreateClientForUser("cascade-user");
        var vehicle = await CreateVehicleAsync(client);
        var baseUrl = $"/api/vehicles/{vehicle.Id}";
        var date = new DateOnly(2026, 5, 1);
        var created = new[]
        {
            await client.PostAsJsonAsync($"{baseUrl}/fuel-energy-entries", new FuelEnergyEntryRequest { Kind = FuelEnergyKind.Fuel, EntryDate = date, UnitPrice = 3m, Quantity = 10m }),
            await client.PostAsJsonAsync($"{baseUrl}/inspections", new InspectionRequest { Type = InspectionType.Safety, InspectionDate = date, Cost = 25m }),
            await client.PostAsJsonAsync($"{baseUrl}/registrations", new RegistrationRequest { State = "WA", Cost = 78.75m, ExpirationDate = date.AddYears(1) }),
            await client.PostAsJsonAsync($"{baseUrl}/insurance", new InsurancePolicyRequest { StartDate = date, EndDate = date.AddYears(1), Duration = InsuranceDuration.Annual, Cost = 1240m }),
            await client.PostAsJsonAsync($"{baseUrl}/taxes", new TaxRequest { Type = TaxType.Purchase, Amount = 1850m, Recurrence = TaxRecurrence.Once }),
        };
        Assert.All(created, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(baseUrl)).StatusCode);

        foreach (var segment in new[] { "fuel-energy-entries", "inspections", "registrations", "insurance", "taxes" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{baseUrl}/{segment}")).StatusCode);
        }
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.Maintenance;

// Keeps the running API aligned with specs/006-maintenance-record-types/contracts/api.openapi.yaml.
public class MaintenanceOpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] Segments = ["fuel-energy-entries", "inspections", "registrations", "insurance", "taxes"];

    private static readonly string[] OperationIds =
    [
        "ListFuelEnergyEntries", "CreateFuelEnergyEntry", "GetFuelEnergyEntry", "UpdateFuelEnergyEntry", "DeleteFuelEnergyEntry",
        "ListInspections", "CreateInspection", "GetInspection", "UpdateInspection", "DeleteInspection",
        "ListRegistrations", "CreateRegistration", "GetRegistration", "UpdateRegistration", "DeleteRegistration",
        "ListInsurancePolicies", "CreateInsurancePolicy", "GetInsurancePolicy", "UpdateInsurancePolicy", "DeleteInsurancePolicy",
        "ListTaxes", "CreateTax", "GetTax", "UpdateTax", "DeleteTax",
    ];

    private async Task<JsonElement> LoadAsync()
    {
        var client = factory.CreateClientForUser("openapi-user");
        return (await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json"))!;
    }

    [Fact]
    public async Task EveryContractOperation_IsPublishedWithItsDocumentedStatusCodes()
    {
        var document = await LoadAsync();
        var paths = document.GetProperty("paths");
        var published = new Dictionary<string, JsonElement>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Object))
            {
                if (operation.Value.TryGetProperty("operationId", out var id))
                {
                    published[id.GetString()!] = operation.Value;
                }
            }
        }

        foreach (var id in OperationIds)
        {
            Assert.True(published.ContainsKey(id), $"{id} is not published.");
        }

        foreach (var segment in Segments)
        {
            Assert.True(paths.TryGetProperty($"/api/vehicles/{{vehicleId}}/{segment}", out _), $"Missing the {segment} collection path.");
            Assert.True(paths.TryGetProperty($"/api/vehicles/{{vehicleId}}/{segment}/{{entryId}}", out _), $"Missing the {segment} item path.");
        }

        foreach (var id in OperationIds)
        {
            var codes = published[id].GetProperty("responses").EnumerateObject().Select(response => response.Name).ToHashSet();
            var expected = id.StartsWith("Delete", StringComparison.Ordinal) ? "204" : id.StartsWith("Create", StringComparison.Ordinal) ? "201" : "200";
            Assert.Contains(expected, codes);
            Assert.Contains("401", codes);
            Assert.Contains("404", codes);
            if (id.StartsWith("Create", StringComparison.Ordinal) || id.StartsWith("Update", StringComparison.Ordinal) || id.StartsWith("List", StringComparison.Ordinal))
            {
                Assert.Contains("400", codes);
            }
        }
    }
}

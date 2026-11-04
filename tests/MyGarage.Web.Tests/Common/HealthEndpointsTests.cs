using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace MyGarage.Web.Tests.Common;

// The container platform probes /health and /alive without a token, in Production as well as Development.
public class HealthEndpointsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task HealthEndpoint_InProduction_IsAnonymousAndReturnsOnlyTheStatus(string path)
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }
}

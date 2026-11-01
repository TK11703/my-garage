using System.Net;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Vehicles;

// The image routes as wired into the real pipeline (authorization and antiforgery); the API is never reached.
public class VehicleImageRouteTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly string Route = $"/vehicles/{Guid.NewGuid()}/images";

    private HttpClient CreateClient(bool signedIn)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        }

        return client;
    }

    [Fact]
    public async Task Get_Anonymous_IsChallengedAndNeverServed()
    {
        var response = await CreateClient(signedIn: false).GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("/images/", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Post_Anonymous_IsChallenged()
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "photo.png" } };

        var response = await CreateClient(signedIn: false).PostAsync(Route, form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_IsRejected()
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "photo.png" } };

        var response = await CreateClient(signedIn: true).PostAsync(Route, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithANonGuidId_IsNotRouted()
    {
        var response = await CreateClient(signedIn: true).GetAsync("/vehicles/abc/images/def");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

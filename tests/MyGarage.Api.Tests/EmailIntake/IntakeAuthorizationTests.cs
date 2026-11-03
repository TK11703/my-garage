using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.Tests.EmailIntake;

public class IntakeAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> PostValidAsync(HttpClient client) =>
        IntakeClient.PostAsync(client, IntakeClient.NewEmail(from: "unknown@nowhere.test"));

    private static void AssertProblem(HttpResponseMessage response) =>
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await PostValidAsync(factory.CreateClientWithToken(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProblem(response);
        Assert.Equal(0, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task UserToken_Returns403AndStoresNothing()
    {
        var response = await PostValidAsync(factory.CreateClientForUser("user-1"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertProblem(response);
        Assert.Equal(0, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task AppTokenWithoutTheRole_Returns403()
    {
        var response = await PostValidAsync(factory.CreateClientForApp(["Some.Other.Role"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertProblem(response);
    }

    [Fact]
    public async Task TokenWithNeitherRoleNorScope_IsRefused()
    {
        var response = await PostValidAsync(factory.CreateClientWithNeitherRoleNorScope());

        // Microsoft.Identity.Web itself rejects such a token with 401 before the policy runs.
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"answered {(int)response.StatusCode}");
        Assert.Equal(0, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task UserTokenThatAlsoCarriesTheRole_Returns403BecauseTheWorkflowMustBeAppOnly()
    {
        var token = TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = "user-1", ["roles"] = new[] { TestJwt.EmailIngestRole } });

        var response = await PostValidAsync(factory.CreateClientWithToken(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WorkflowToken_Returns202()
    {
        var response = await PostValidAsync(factory.CreateClientForWorkflow());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var acknowledgement = await response.Content.ReadFromJsonAsync<IntakeAcknowledgement>();
        Assert.Equal("received", acknowledgement!.Status);
    }

    [Theory]
    [InlineData("/api/users/me")]
    [InlineData("/api/vehicles")]
    [InlineData("/api/lookups")]
    [InlineData("/api/features?query=a")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/service-records")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/fuel-energy-entries")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/inspections")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/registrations")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/insurance")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/taxes")]
    [InlineData("/api/vehicles/00000000-0000-0000-0000-000000000001/images/00000000-0000-0000-0000-000000000002/content")]
    public async Task WorkflowToken_IsRefusedByTheUserEndpoints(string path)
    {
        var response = await factory.CreateClientForWorkflow().GetAsync(path);

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"{path} answered {(int)response.StatusCode} to the workflow token");
        AssertProblem(response);
    }
}

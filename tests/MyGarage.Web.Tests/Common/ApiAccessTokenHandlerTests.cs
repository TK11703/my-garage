using System.Net;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class ApiAccessTokenHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeTokenProvider(Func<string> token) : IApiTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token());
    }

    [Fact]
    public async Task SendAsync_AttachesBearerToken()
    {
        var inner = new CapturingHandler();
        var handler = new ApiAccessTokenHandler(new FakeTokenProvider(() => "tok-123")) { InnerHandler = inner };

        using var client = new HttpClient(handler);
        await client.GetAsync("http://api.test/api/users/me");

        Assert.Equal("Bearer", inner.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", inner.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_PropagatesTokenAcquisitionFailure_WithoutCallingTheApi()
    {
        var inner = new CapturingHandler();
        var handler = new ApiAccessTokenHandler(new FakeTokenProvider(() => throw new InvalidOperationException("no token"))) { InnerHandler = inner };

        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("http://api.test/api/users/me"));
        Assert.Null(inner.Request);
    }

    [Fact]
    public void ApiScope_IsBuiltFromClientIdAndScopeName()
    {
        var settings = new EntraWebSettings
        {
            Api = new EntraWebSettings.ApiSection { ClientId = "33333333-3333-3333-3333-333333333333", Scope = "access_as_user" },
        };

        Assert.Equal("api://33333333-3333-3333-3333-333333333333/access_as_user", settings.ApiScope);
    }
}

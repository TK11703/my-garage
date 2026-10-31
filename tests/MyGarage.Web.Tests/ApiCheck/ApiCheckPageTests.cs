using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.SampleData;
using MyGarage.Shared.Users;
using MyGarage.Web.ApiCheck;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.ApiCheck;

public class ApiCheckPageTests : BunitContext
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private void RegisterClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://api.test/") };
        Services.AddSingleton(new GarageApiClient(http));
    }

    [Fact]
    public void Success_ShowsIdentityAndSampleItems()
    {
        RegisterClient(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/users/me" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CurrentUserDto { UserId = "oid-1", DisplayName = "Alice Garage" }),
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new SampleItemDto { Id = 1, Name = "Sample item 1" },
                    new SampleItemDto { Id = 2, Name = "Sample item 2" },
                }),
            },
        });

        var cut = Render<MyGarage.Web.ApiCheck.ApiCheck>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alice Garage", cut.Markup);
            Assert.Contains("Sample item 1", cut.Markup);
            Assert.Contains("Sample item 2", cut.Markup);
        });
    }

    [Fact]
    public void ApiUnavailable_ShowsFriendlyMessage()
    {
        RegisterClient(_ => throw new HttpRequestException("connection refused"));

        var cut = Render<MyGarage.Web.ApiCheck.ApiCheck>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(ApiCheckMessages.Unavailable, cut.Markup);
            Assert.DoesNotContain("connection refused", cut.Markup);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgainMessage(HttpStatusCode status)
    {
        RegisterClient(_ => new HttpResponseMessage(status));

        var cut = Render<MyGarage.Web.ApiCheck.ApiCheck>();

        cut.WaitForAssertion(() => Assert.Contains(ApiCheckMessages.NotAccepted, cut.Markup));
    }
}

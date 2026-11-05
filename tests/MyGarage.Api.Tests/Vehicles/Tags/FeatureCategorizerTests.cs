using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class FeatureCategorizerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("tok", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static FeatureCategorizer Create(HttpMessageHandler handler, string? endpoint = "https://aoai.example.test/") =>
        new(
            new HttpClient(handler),
            Options.Create(new ExtractionOptions { Endpoint = endpoint, Deployment = "gpt-test" }),
            Options.Create(new FeatureCategorizationOptions()),
            new FakeCredential(),
            NullLogger<FeatureCategorizer>.Instance);

    private static FakeHandler Answering(string answerJson) =>
        new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { output = new object[] { new { type = "message", content = new[] { new { type = "output_text", text = answerJson } } } } }),
                Encoding.UTF8,
                "application/json"),
        }));

    [Fact]
    public async Task ValidAnswers_AreMappedByNumber()
    {
        var handler = Answering("""{"items":[{"index":2,"category":"Safety"},{"index":1,"category":"exterior"}]}""");

        var result = await Create(handler).CategorizeAsync(["Sunroof", "Airbags"], CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(["Exterior", "Safety"], result.Categories);
    }

    [Fact]
    public async Task AnUnknownCategory_AnOutOfRangeNumber_AndAMissingItem_LeaveTheNameWithoutAnAnswer()
    {
        var handler = Answering("""{"items":[{"index":1,"category":"Gadgets"},{"index":9,"category":"Safety"},{"index":3,"category":"Interior"}]}""");

        var result = await Create(handler).CategorizeAsync(["A", "B", "C"], CancellationToken.None);

        Assert.Equal([null, null, "Interior"], result.Categories);
    }

    [Fact]
    public async Task TheRequest_CarriesTheNumberedNames_AndTheFixedCategoriesAsTheOnlyAllowedAnswers()
    {
        var handler = Answering("""{"items":[]}""");

        await Create(handler).CategorizeAsync(["Sunroof", "Airbags"], CancellationToken.None);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("1. Sunroof\\n2. Airbags", body);
        Assert.DoesNotContain("input_image", body);
        Assert.DoesNotContain("input_file", body);
        Assert.All(FeatureCategories.All, category => Assert.Contains($"\"{category.Replace("&", "\\u0026")}\"", body));
    }

    [Fact]
    public async Task NoNames_MakeNoCall()
    {
        var handler = Answering("""{"items":[]}""");

        var result = await Create(handler).CategorizeAsync([], CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Empty(handler.Bodies);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"other":1}""")]
    [InlineData("""{"items":[{"index":1,"category":"Safety","extra":true}]}""")]
    public async Task AnAnswerThatDoesNotMatchTheSchema_FailsAsInvalidOutput(string answer)
    {
        var result = await Create(Answering(answer)).CategorizeAsync(["Airbags"], CancellationToken.None);

        Assert.Equal("invalid_output", result.FailureReason);
    }

    [Fact]
    public async Task AServiceError_FailsWithItsReason()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }));

        var result = await Create(handler).CategorizeAsync(["Airbags"], CancellationToken.None);

        Assert.Equal("rate_limited", result.FailureReason);
    }

    [Fact]
    public void WithoutAnEndpoint_ItIsDisabled() =>
        Assert.False(Create(Answering("{}"), endpoint: null).IsEnabled);
}

using Azure.Core;
using Microsoft.Extensions.Configuration;
using MyGarage.Api.Common;
using Npgsql;

namespace MyGarage.Api.Tests.Common;

public class EntraDatabaseAuthenticationTests
{
    private const string Scope = "https://ossrdbms-aad.database.windows.net/.default";

    private static IConfiguration Settings(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Database:UseEntraAuthentication"] = value })
            .Build();

    [Fact]
    public async Task PasswordProvider_RequestsTheDatabaseScopeAndReturnsTheToken()
    {
        var credential = new FakeCredential("token-1");
        var provider = EntraDatabaseAuthentication.CreatePasswordProvider(credential);

        var password = await provider(new NpgsqlConnectionStringBuilder(), CancellationToken.None);

        Assert.Equal("token-1", password);
        Assert.Equal([Scope], credential.RequestedScopes);
    }

    [Fact]
    public async Task PasswordProvider_WhenTheCredentialFails_ThrowsInsteadOfReturningAnEmptyPassword()
    {
        var provider = EntraDatabaseAuthentication.CreatePasswordProvider(new FakeCredential(null));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider(new NpgsqlConnectionStringBuilder(), CancellationToken.None));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void Configure_RegistersTheProviderOnlyWhenTheSettingIsTrue(string? setting, bool expected)
    {
        var builder = new NpgsqlDataSourceBuilder("Host=localhost;Database=test;Username=u");

        var applied = EntraDatabaseAuthentication.Configure(builder, Settings(setting), new FakeCredential("t"));

        Assert.Equal(expected, applied);
    }

    private sealed class FakeCredential(string? token) : TokenCredential
    {
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return token is null
                ? throw new InvalidOperationException("no token")
                : ValueTask.FromResult(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}

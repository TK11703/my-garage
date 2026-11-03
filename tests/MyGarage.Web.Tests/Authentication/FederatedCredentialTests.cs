using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Authentication;

public class FederatedCredentialTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static MicrosoftIdentityOptions Options(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IOptionsMonitor<MicrosoftIdentityOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

    [Fact]
    public void WithAManagedIdentityClientId_TheOnlyCredentialIsASignedAssertionFromTheManagedIdentity()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Web:ClientSecret"] = "",
                ["AzureAd:Web:ManagedIdentityClientId"] = "44444444-4444-4444-4444-444444444444",
            })));

        var options = Options(host);

        var credential = Assert.Single(options.ClientCredentials ?? []);
        Assert.Equal(CredentialSource.SignedAssertionFromManagedIdentity, credential.SourceType);
        Assert.Equal("44444444-4444-4444-4444-444444444444", credential.ManagedIdentityClientId);
        Assert.True(string.IsNullOrEmpty(options.ClientSecret));
    }

    [Fact]
    public void WithAClientSecretAndNoManagedIdentity_TheSecretIsKept()
    {
        var options = Options(factory);

        Assert.Equal("test-secret", options.ClientSecret);
        Assert.DoesNotContain(options.ClientCredentials ?? [], c => c.SourceType == CredentialSource.SignedAssertionFromManagedIdentity);
    }
}

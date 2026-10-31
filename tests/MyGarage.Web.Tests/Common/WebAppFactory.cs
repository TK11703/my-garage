using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MyGarage.Web.Tests.Common;

public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    public const string FakeAuthority = "https://login.test";

    public ConcurrentQueue<string?> RedirectUris { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:Web:ClientId"] = "22222222-2222-2222-2222-222222222222",
                ["AzureAd:Web:ClientSecret"] = "test-secret",
                ["AzureAd:Api:ClientId"] = "33333333-3333-3333-3333-333333333333",
                ["AzureAd:Api:Scope"] = "access_as_user",
            }));

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName);

            // Static metadata so no test ever calls the real identity provider.
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = FakeAuthority,
                    AuthorizationEndpoint = $"{FakeAuthority}/authorize",
                    TokenEndpoint = $"{FakeAuthority}/token",
                    EndSessionEndpoint = $"{FakeAuthority}/logout",
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                var previous = options.Events.OnRedirectToIdentityProvider;
                options.Events.OnRedirectToIdentityProvider = async context =>
                {
                    RedirectUris.Enqueue(context.Properties.RedirectUri);
                    await previous(context);
                };
            });
        });
    }
}

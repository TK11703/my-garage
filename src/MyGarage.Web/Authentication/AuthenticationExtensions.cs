using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using MyGarage.Web.Common;

namespace MyGarage.Web.Authentication;

public static class AuthenticationExtensions
{
    // Settings are read lazily so configuration added after the builder is created (for example by tests) is honoured.
    public static WebApplicationBuilder AddEntraAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(_ => EntraWebSettings.Load(builder.Configuration));

        builder.Services
            .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(options =>
            {
                var settings = EntraWebSettings.Load(builder.Configuration);
                options.Instance = "https://login.microsoftonline.com/";
                options.TenantId = settings.TenantId;
                options.ClientId = settings.Web.ClientId;
                options.ClientSecret = settings.Web.ClientSecret;
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
            })
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var settings = EntraWebSettings.Load(builder.Configuration);
            options.Scope.Add(settings.ApiScope);
            options.SignedOutRedirectUri = "/authentication/signed-out";

            options.Events.OnRemoteFailure = context =>
            {
                var reason = AuthFailureMapper.Map(context.Failure);
                context.Response.Redirect($"/authentication/error?reason={reason}");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });

        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return builder;
    }
}

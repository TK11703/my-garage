using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace MyGarage.Api.Common;

public static class AuthenticationExtensions
{
    private const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

    // Settings are read lazily so configuration added after the builder is created (for example by tests) is honoured.
    public static WebApplicationBuilder AddEntraApiAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(_ => EntraApiSettings.Load(builder.Configuration));

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                jwtOptions =>
                {
                    var settings = EntraApiSettings.Load(builder.Configuration);
                    jwtOptions.MapInboundClaims = false;
                    jwtOptions.TokenValidationParameters.ValidAudiences = [settings.Api.ClientId, $"api://{settings.Api.ClientId}"];
                },
                identityOptions =>
                {
                    var settings = EntraApiSettings.Load(builder.Configuration);
                    identityOptions.Instance = "https://login.microsoftonline.com/";
                    identityOptions.TenantId = settings.TenantId;
                    identityOptions.ClientId = settings.Api.ClientId;
                });

        // Every endpoint needs a signed-in caller holding the API scope unless it opts out with AllowAnonymous.
        builder.Services.AddOptions<AuthorizationOptions>().Configure<EntraApiSettings>((options, settings) =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasScope(context.User, settings.Api.Scope))
                .Build());

        return builder;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope) =>
        user.FindAll("scp").Concat(user.FindAll(ScopeClaimType))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);
}

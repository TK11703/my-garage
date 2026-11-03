using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace MyGarage.Api.Common;

// The intake endpoint is for the trusted workflow only: an app token holding the Email.Ingest role and carrying no delegated user scope.
public static class EmailIntakeAuthorization
{
    public const string PolicyName = "EmailIntake";
    public const string RoleName = "Email.Ingest";

    private const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

    // A named policy on the endpoint group replaces the fallback (user scope) policy for that group only.
    public static IServiceCollection AddEmailIntakePolicy(this IServiceCollection services)
    {
        services.AddOptions<AuthorizationOptions>().Configure(options =>
            options.AddPolicy(PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => IsWorkflow(context.User))));
        return services;
    }

    private static bool IsWorkflow(ClaimsPrincipal user) =>
        user.FindAll("roles").Concat(user.FindAll(ClaimTypes.Role)).Any(claim => string.Equals(claim.Value, RoleName, StringComparison.Ordinal))
        && !user.HasClaim(claim => claim.Type is "scp" or ScopeClaimType);
}

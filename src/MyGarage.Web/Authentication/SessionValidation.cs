using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MyGarage.Web.Authentication;

public static class SessionValidation
{
    public const string InstanceClaim = "mg_instance";

    // Identifies this run of the app. The Microsoft token cache is in memory, so a session started by an
    // earlier run still has a valid cookie but no tokens behind it.
    public static readonly string CurrentInstance = Guid.NewGuid().ToString("N");

    // Called at sign-in so the session cookie remembers which run created it.
    public static void StampInstance(ClaimsPrincipal? principal, string instance)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var existing in identity.FindAll(InstanceClaim).ToList())
        {
            identity.RemoveClaim(existing);
        }

        identity.AddClaim(new Claim(InstanceClaim, instance));
    }

    // Rejecting such a session sends the user through sign-in again (normally instant single sign-on)
    // instead of showing a "not accepted" error and logging a failed token request.
    public static async Task RejectSessionFromAnotherInstanceAsync(CookieValidatePrincipalContext context, string currentInstance)
    {
        if (context.Principal?.FindFirst(InstanceClaim)?.Value == currentInstance)
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Sign-in and sign-out must work even when the session is stale.
    public static bool ShouldCheck(PathString path) => !path.StartsWithSegments("/authentication");
}

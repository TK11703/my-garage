using System.Security.Claims;

namespace MyGarage.Web.Common;

public static class ClaimsPrincipalExtensions
{
    public const string NeutralName = "Signed-in user";

    public static string GetDisplayName(this ClaimsPrincipal user)
    {
        foreach (var claimType in new[] { "name", "preferred_username" })
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return NeutralName;
    }
}

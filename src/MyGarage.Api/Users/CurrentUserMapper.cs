using System.Security.Claims;
using MyGarage.Shared.Users;

namespace MyGarage.Api.Users;

public static class CurrentUserMapper
{
    private const string ObjectIdentifierClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    // Identity comes only from the validated token claims; returns null when the token carries no stable identifier.
    public static CurrentUserDto? Map(ClaimsPrincipal principal)
    {
        var userId = FirstValue(principal, "oid", ObjectIdentifierClaimType, "sub", ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return null;
        }

        var preferredUsername = FirstValue(principal, "preferred_username");

        return new CurrentUserDto
        {
            UserId = userId,
            DisplayName = FirstValue(principal, "name") ?? preferredUsername ?? "Unknown",
            Email = FirstValue(principal, "email") ?? preferredUsername,
        };
    }

    private static string? FirstValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

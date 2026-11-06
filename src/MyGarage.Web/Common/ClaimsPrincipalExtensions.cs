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

    // The email claim, else a sign-in name that looks like an address; empty when neither is present.
    public static string GetEmail(this ClaimsPrincipal user)
    {
        foreach (var claimType in new[] { "email", ClaimTypes.Email, "preferred_username" })
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value) && value.Contains('@'))
            {
                return value;
            }
        }

        return string.Empty;
    }

    // Up to two uppercase letters: first letter of the first and last name words; empty for the neutral name.
    public static string GetInitials(this ClaimsPrincipal user)
    {
        var name = user.GetDisplayName();
        if (name == NeutralName)
        {
            return string.Empty;
        }

        var letters = name
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.FirstOrDefault(char.IsLetter))
            .Where(letter => letter != default)
            .ToList();

        return letters.Count switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(letters[0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(letters[0]), char.ToUpperInvariant(letters[^1])),
        };
    }
}

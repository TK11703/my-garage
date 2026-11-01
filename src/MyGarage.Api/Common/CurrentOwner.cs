using System.Security.Claims;
using MyGarage.Api.Users;

namespace MyGarage.Api.Common;

// The owner of every record is the authenticated caller, derived only from the validated token claims.
public static class CurrentOwner
{
    public static string? Get(ClaimsPrincipal principal) => CurrentUserMapper.Map(principal)?.UserId;
}

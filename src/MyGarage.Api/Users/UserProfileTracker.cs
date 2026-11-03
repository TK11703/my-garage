using System.Security.Claims;
using MyGarage.Database.Users;

namespace MyGarage.Api.Users;

// Keeps user_profiles current so the intake endpoint can match a sender address to a user; the profile comes only from validated claims.
// Runs after authorization, writes at most once per owner per hour, and never fails the request.
public sealed class UserProfileTracker(
    RequestDelegate next,
    UserProfileRepository profiles,
    TimeProvider timeProvider,
    ILogger<UserProfileTracker> logger)
{
    private const int MaxEmailLength = 320;
    private const int MaxDisplayNameLength = 200;
    private const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // One tracker lives per application host, so the throttle never leaks between hosts.
    private readonly Dictionary<string, DateTimeOffset> lastWritten = [];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsUserPrincipal(context.User) && CurrentUserMapper.Map(context.User) is { } current)
        {
            await TrackAsync(current.UserId, current.Email, current.DisplayName, context.RequestAborted);
        }

        await next(context);
    }

    // A delegated user token carries the scp claim; an app-only token (the workflow) never does.
    private static bool IsUserPrincipal(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(claim => claim.Type is "scp" or ScopeClaimType);

    private async Task TrackAsync(string ownerId, string? email, string displayName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        lock (lastWritten)
        {
            if (lastWritten.TryGetValue(ownerId, out var last) && now - last < Interval)
            {
                return;
            }
        }

        try
        {
            await profiles.UpsertAsync(
                ownerId, Truncate(email, MaxEmailLength), Truncate(displayName, MaxDisplayNameLength)!, now, cancellationToken);
            lock (lastWritten)
            {
                lastWritten[ownerId] = now;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not update the profile of user {OwnerId}", ownerId);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

using System.Text.RegularExpressions;

namespace MyGarage.Web.Authentication;

public static partial class AuthFailureMapper
{
    public static string Map(Exception? failure)
    {
        if (failure is null)
        {
            return AuthFailureReason.Unknown;
        }

        var match = ProtocolErrorPattern().Match(failure.Message);
        if (match.Success)
        {
            var error = match.Groups["error"].Value;
            var description = match.Groups["description"].Value;

            switch (error)
            {
                case "access_denied" when IsConsentDecline(description):
                    return AuthFailureReason.Cancelled;
                case "access_denied":
                    return AuthFailureReason.Denied;
                case "temporarily_unavailable" or "server_error":
                    return AuthFailureReason.Unavailable;
            }
        }

        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or TimeoutException or TaskCanceledException)
            {
                return AuthFailureReason.Unavailable;
            }
        }

        return AuthFailureReason.Unknown;
    }

    private static bool IsConsentDecline(string description) =>
        description.Contains("AADSTS65004", StringComparison.OrdinalIgnoreCase)
        || description.Contains("declined", StringComparison.OrdinalIgnoreCase)
        || description.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("error: '(?<error>[^']*)', error_description: '(?<description>[^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex ProtocolErrorPattern();
}

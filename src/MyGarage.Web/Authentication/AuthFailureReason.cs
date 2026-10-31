namespace MyGarage.Web.Authentication;

public static class AuthFailureReason
{
    public const string Cancelled = "cancelled";
    public const string Denied = "denied";
    public const string Unavailable = "unavailable";
    public const string Unknown = "unknown";
}

public static class AuthFailureMessages
{
    // Fixed, non-technical text; raw provider errors are never shown to the user.
    public static string For(string? reason) => reason switch
    {
        AuthFailureReason.Cancelled => "Sign-in was cancelled. You can try again whenever you are ready.",
        AuthFailureReason.Denied => "Your account is not allowed to use My Garage. Sign in with an account from the organization that owns it.",
        AuthFailureReason.Unavailable => "Sign-in is temporarily unavailable. Please try again in a few minutes.",
        _ => "Something went wrong while signing you in. Please try again.",
    };
}

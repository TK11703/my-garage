namespace MyGarage.Web.Authentication;

public static class ReturnUrlValidator
{
    // Accepts only an application-local path such as "/vehicles"; everything else falls back to home.
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }

        return returnUrl.Any(char.IsControl) ? "/" : returnUrl;
    }
}

using MyGarage.Shared.Registrations;
using MyGarage.Web.Common;

namespace MyGarage.Web.Registrations;

public static class RegistrationFormat
{
    // "Expires May 1, 2027".
    public static string Expires(DateOnly expirationDate) => $"Expires {DisplayFormat.Date(expirationDate)}";

    // "Washington (WA)".
    public static string StateDisplay(string code) => UsStates.Display(code);

    public static string EditName(RegistrationDto registration) => $"Edit registration expiring {DisplayFormat.Date(registration.ExpirationDate)}";
}

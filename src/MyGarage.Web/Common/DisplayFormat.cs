using System.Globalization;

namespace MyGarage.Web.Common;

// One currency, one date style, always invariant so output does not depend on the server culture.
public static class DisplayFormat
{
    public const string NotSet = "Not set";

    public static string Money(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // At least two and at most four decimals, so $3.459, $0.1342, and $4.00.
    public static string UnitPrice(decimal price) =>
        (price < 0 ? "-$" : "$") + Math.Abs(price).ToString("0.00##", CultureInfo.InvariantCulture);

    // Up to three decimals, trailing zeros trimmed.
    public static string Quantity(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
}

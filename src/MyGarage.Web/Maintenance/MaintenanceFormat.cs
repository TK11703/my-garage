using System.Globalization;

namespace MyGarage.Web.Maintenance;

// Count wording for the Maintenance headings, for example "1 inspection" or "3 taxes".
public static class MaintenanceFormat
{
    public static string Inspections(int count) => Count(count, "inspection", "inspections");

    public static string Registrations(int count) => Count(count, "registration", "registrations");

    // Insurance has no plural form.
    public static string Insurance(int count) => count.ToString(CultureInfo.InvariantCulture) + " insurance";

    public static string Taxes(int count) => Count(count, "tax", "taxes");

    private static string Count(int count, string singular, string plural) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
}

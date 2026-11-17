using System.Globalization;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

public static class VehicleFormat
{
    public const string NotSet = "Not set";

    public static string Mileage(int? mileage) =>
        mileage is { } value ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} mi" : "Mileage not set";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? NotSet : value;

    public static string Number(int? value) => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : NotSet;

    public static string MileageOrNotSet(int? mileage) => mileage is null ? NotSet : Mileage(mileage);

    public static string Horsepower(int? horsepower) => horsepower is { } value ? $"{value.ToString(CultureInfo.InvariantCulture)} hp" : NotSet;

    public static string WheelSize(decimal? size) => size is { } value ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} in" : NotSet;

    public static string Date(DateOnly? date) => date is { } value ? value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : NotSet;

    public static string Ownership(OwnershipType? ownership) => ownership?.ToString() ?? NotSet;

    public static string Acquired(OwnershipType? ownership, int year) =>
        ownership switch { OwnershipType.Purchased => $"Purchased {year}", OwnershipType.Leased => $"Leased {year}", _ => $"Acquired {year}" };

    // "3,780 mi left" or "3 days overdue": with both a distance and a time left, the one that is overdue or nearest its due-soon limit.
    public static string NextDueRemaining(VehicleNextDueDto due)
    {
        if (due.MilesRemaining is null && due.DaysRemaining is null)
        {
            return "Needs a mileage";
        }

        var useDays = due.DaysRemaining is { } days
            && (due.MilesRemaining is not { } m || (days <= 0 && m > 0) || (m > ReminderThresholds.DueSoonMiles && days <= ReminderThresholds.DueSoonDays));
        return useDays ? DaysRemaining(due.DaysRemaining!.Value) : MilesRemaining(due.MilesRemaining!.Value);
    }

    private static string MilesRemaining(int miles) => miles switch
    {
        0 => "due now",
        > 0 => $"{miles.ToString("N0", CultureInfo.InvariantCulture)} mi left",
        _ => $"{(-miles).ToString("N0", CultureInfo.InvariantCulture)} mi overdue",
    };

    private static string DaysRemaining(int days) => days switch
    {
        0 => "due today",
        > 0 => $"in {days.ToString(CultureInfo.InvariantCulture)} day{(days == 1 ? string.Empty : "s")}",
        _ => $"{(-days).ToString(CultureInfo.InvariantCulture)} day{(days == -1 ? string.Empty : "s")} overdue",
    };

    public static string DateLabel(OwnershipType? ownership) =>
        ownership switch { OwnershipType.Purchased => "Date Purchased", OwnershipType.Leased => "Date Leased", _ => "Date purchased or leased" };

    public static string MileageLabel(OwnershipType? ownership) =>
        ownership switch { OwnershipType.Purchased => "Mileage on Purchase Date", OwnershipType.Leased => "Mileage on Lease Date", _ => "Mileage on Date purchased or leased" };
}

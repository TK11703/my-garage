using MyGarage.Shared.Inspections;
using MyGarage.Web.Common;

namespace MyGarage.Web.Inspections;

public static class InspectionFormat
{
    public static string TypeLabel(InspectionType type) => type == InspectionType.Safety ? "Safety" : "Emissions";

    // "Next due 2027-03-01" or "Next due Not set".
    public static string NextDue(DateOnly? nextDueDate) =>
        $"Next due {(nextDueDate is { } date ? DisplayFormat.Date(date) : "Not set")}";

    public static string EditName(InspectionDto inspection) => $"Edit inspection from {DisplayFormat.Date(inspection.InspectionDate)}";

    public static string TypeValue(InspectionType type) => type.ToString();

    public static InspectionType? ParseType(string? value) =>
        Enum.TryParse<InspectionType>(value, ignoreCase: true, out var type) && Enum.IsDefined(type) ? type : null;
}

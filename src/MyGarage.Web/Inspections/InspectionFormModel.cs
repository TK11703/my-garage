using MyGarage.Shared.Inspections;

namespace MyGarage.Web.Inspections;

public sealed class InspectionFormModel
{
    public InspectionType? Type { get; set; }

    public DateOnly? InspectionDate { get; set; }

    public decimal? Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public InspectionRequest ToRequest() =>
        new() { Type = Type, InspectionDate = InspectionDate, Cost = Cost, NextDueDate = NextDueDate };

    public static InspectionFormModel From(InspectionDto inspection) =>
        new()
        {
            Type = inspection.Type,
            InspectionDate = inspection.InspectionDate,
            Cost = inspection.Cost,
            NextDueDate = inspection.NextDueDate,
        };
}

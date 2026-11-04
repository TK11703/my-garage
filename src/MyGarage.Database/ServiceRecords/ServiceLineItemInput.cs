namespace MyGarage.Database.ServiceRecords;

public sealed record ServicePartInput(string Name, decimal Quantity, decimal UnitPrice);

// Either FlatCost (which may be negative, a discount), or labor and/or parts; the API validates that before it gets here.
// LaborCost is an entered labor total, used only when there are no hours and rate to calculate from.
public sealed record ServiceLineItemInput(
    string Description,
    decimal? FlatCost,
    decimal? LaborHours,
    decimal? LaborRate,
    IReadOnlyList<ServicePartInput> Parts,
    decimal? LaborCost = null)
{
    // The entered amount is stored only when it is not derivable from hours and rate.
    public decimal? StoredLaborCost => LaborHours is null && LaborRate is null ? LaborCost : null;

    public decimal TotalCost => ServiceCostCalculator.LineTotal(
        FlatCost, LaborHours, LaborRate, StoredLaborCost, Parts.Select(part => (part.Quantity, part.UnitPrice)));
}

public enum AddLineItemOutcome
{
    Created,
    NotFound,
    LimitReached,
}

public sealed record AddLineItemResult(AddLineItemOutcome Outcome, ServiceRecordDetailRecord? Detail);

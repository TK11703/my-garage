namespace MyGarage.Database.ServiceRecords;

public sealed record ServicePartInput(string Name, decimal Quantity, decimal UnitPrice);

// Either FlatCost, or labor (hours with a rate) and/or parts; the API validates that before it gets here.
public sealed record ServiceLineItemInput(
    string Description,
    decimal? FlatCost,
    decimal? LaborHours,
    decimal? LaborRate,
    IReadOnlyList<ServicePartInput> Parts);

public enum AddLineItemOutcome
{
    Created,
    NotFound,
    LimitReached,
}

public sealed record AddLineItemResult(AddLineItemOutcome Outcome, ServiceRecordDetailRecord? Detail);

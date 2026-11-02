namespace MyGarage.Database.ServiceRecords;

// The single place a line item's cost is computed; every write stores the result in service_line_items.total_cost.
public static class ServiceCostCalculator
{
    public static decimal LaborCost(decimal hours, decimal rate) => Round(hours * rate);

    public static decimal PartTotal(decimal quantity, decimal unitPrice) => Round(quantity * unitPrice);

    public static decimal LineTotal(
        decimal? flatCost,
        decimal? laborHours,
        decimal? laborRate,
        IEnumerable<(decimal Quantity, decimal UnitPrice)> parts)
    {
        if (flatCost is { } flat)
        {
            return flat;
        }

        var total = laborHours is { } hours && laborRate is { } rate ? LaborCost(hours, rate) : 0m;
        foreach (var (quantity, unitPrice) in parts)
        {
            total += PartTotal(quantity, unitPrice);
        }

        return total;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

namespace MyGarage.Database.Dashboard;

// The cost of one kind ("fuel" or "energy") on one vehicle in one calendar month; Month is the first day of that month.
public sealed record FuelEnergySpendRecord
{
    public DateOnly Month { get; init; }

    public string Kind { get; init; } = string.Empty;

    public Guid VehicleId { get; init; }

    public int? VehicleYear { get; init; }

    public string VehicleMake { get; init; } = string.Empty;

    public string VehicleModel { get; init; } = string.Empty;

    public decimal Total { get; init; }

    public bool VehicleArchived { get; init; }
}

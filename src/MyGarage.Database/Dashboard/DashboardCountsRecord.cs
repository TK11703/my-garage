namespace MyGarage.Database.Dashboard;

public sealed record DashboardCountsRecord
{
    public int Vehicles { get; init; }

    public int ServiceRecords { get; init; }

    public int FuelEnergyEntries { get; init; }

    public int DraftsAwaitingReview { get; init; }
}

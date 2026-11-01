namespace MyGarage.Shared.Vehicles;

// Create and update both send the full set of editable fields.
public sealed class VehicleRequest
{
    public string? Nickname { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Trim { get; init; }

    public string? Color { get; init; }

    public int? Mileage { get; init; }

    public int? Doors { get; init; }
}

using System.Text.Json.Serialization;

namespace MyGarage.Shared.Vehicles;

public sealed class VehicleSummaryDto
{
    public required Guid Id { get; init; }

    // Null for vehicles saved before the year was collected.
    public int? Year { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    // Shown as "[Year] [Make] [Model]"; computed, not sent over the wire.
    [JsonIgnore]
    public string Name => VehicleName.Compose(Year, Make, Model);

    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }

    public string? FuelType { get; init; }

    public string? Transmission { get; init; }

    public OwnershipType? Ownership { get; init; }

    // Year of the purchase or lease date, for the garage card.
    public int? PurchasedYear { get; init; }
}

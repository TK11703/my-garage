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

    // The greater of the latest service record's odometer, the latest odometer reading, and the mileage on purchase or lease.
    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }

    public string? FuelType { get; init; }

    public string? Transmission { get; init; }

    public OwnershipType? Ownership { get; init; }

    // Year of the purchase or lease date, for the garage card.
    public int? PurchasedYear { get; init; }

    // When the owner archived the vehicle; null for an active vehicle.
    public DateTimeOffset? ArchivedAt { get; init; }

    // Where else the search text was found (trim, color, VIN, engine, features); empty without a search.
    public IReadOnlyList<VehicleMatchDto> Matches { get; init; } = [];
}

// One field of a vehicle that contains the search text.
public sealed class VehicleMatchDto
{
    public required string Field { get; init; }

    public required string Text { get; init; }
}

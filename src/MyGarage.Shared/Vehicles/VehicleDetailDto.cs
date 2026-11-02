using System.Text.Json.Serialization;
using MyGarage.Shared.Lookups;

namespace MyGarage.Shared.Vehicles;

public sealed class VehicleDetailDto
{
    public required Guid Id { get; init; }

    public string? Vin { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    // Shown as "[Year] [Make] [Model]"; computed, not sent over the wire.
    [JsonIgnore]
    public string Name => VehicleName.Compose(Year, Make, Model);

    public string? Trim { get; init; }

    // Null for vehicles saved before the year was collected.
    public int? Year { get; init; }

    public LookupValueDto? VehicleType { get; init; }

    public OwnershipType? Ownership { get; init; }

    public DateOnly? PurchasedOn { get; init; }

    public DateOnly? LeaseEndsOn { get; init; }

    public string? Engine { get; init; }

    public int? Horsepower { get; init; }

    public LookupValueDto? FuelType { get; init; }

    public LookupValueDto? Transmission { get; init; }

    public int? Mileage { get; init; }

    // Entered by the owner; the garage card shows the greater of this, the latest service record's odometer, and the mileage above.
    public int? LatestOdometerReading { get; init; }

    public string? Color { get; init; }

    public int? Seats { get; init; }

    public int? Doors { get; init; }

    public LookupValueDto? Drivetrain { get; init; }

    public decimal? WheelSize { get; init; }

    public string? TireSize { get; init; }

    public required IReadOnlyList<VehicleTagDto> Tags { get; init; }

    public required IReadOnlyList<VehicleImageDto> Images { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

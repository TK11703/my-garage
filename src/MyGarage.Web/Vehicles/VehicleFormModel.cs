using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

// Form state shared by the add and edit pages; the API validates and trims, so text is sent as entered.
public sealed class VehicleFormModel
{
    public string? Vin { get; set; }

    public string? Make { get; set; }

    public string? Model { get; set; }

    public string? Trim { get; set; }

    public int? Year { get; set; }

    public Guid? VehicleTypeId { get; set; }

    public OwnershipType? Ownership { get; set; }

    public DateOnly? PurchasedOn { get; set; }

    public DateOnly? LeaseEndsOn { get; set; }

    public string? Engine { get; set; }

    public int? Horsepower { get; set; }

    public Guid? FuelTypeId { get; set; }

    public Guid? TransmissionId { get; set; }

    public int? Mileage { get; set; }

    public int? LatestOdometerReading { get; set; }

    public string? Color { get; set; }

    public int? Seats { get; set; }

    public int? Doors { get; set; }

    public Guid? DrivetrainId { get; set; }

    public decimal? WheelSize { get; set; }

    public string? TireSize { get; set; }

    public VehicleRequest ToRequest() =>
        new()
        {
            Vin = Vin,
            Make = Make,
            Model = Model,
            Trim = Trim,
            Year = Year,
            VehicleTypeId = VehicleTypeId,
            Ownership = Ownership,
            PurchasedOn = PurchasedOn,
            // A lease end only applies to leased vehicles, so one left in a hidden field is not sent.
            LeaseEndsOn = Ownership == OwnershipType.Leased ? LeaseEndsOn : null,
            Engine = Engine,
            Horsepower = Horsepower,
            FuelTypeId = FuelTypeId,
            TransmissionId = TransmissionId,
            Mileage = Mileage,
            LatestOdometerReading = LatestOdometerReading,
            Color = Color,
            Seats = Seats,
            Doors = Doors,
            DrivetrainId = DrivetrainId,
            WheelSize = WheelSize,
            TireSize = TireSize,
        };

    public static VehicleFormModel From(VehicleDetailDto vehicle) =>
        new()
        {
            Vin = vehicle.Vin,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Trim = vehicle.Trim,
            Year = vehicle.Year,
            VehicleTypeId = vehicle.VehicleType?.Id,
            Ownership = vehicle.Ownership,
            PurchasedOn = vehicle.PurchasedOn,
            LeaseEndsOn = vehicle.LeaseEndsOn,
            Engine = vehicle.Engine,
            Horsepower = vehicle.Horsepower,
            FuelTypeId = vehicle.FuelType?.Id,
            TransmissionId = vehicle.Transmission?.Id,
            Mileage = vehicle.Mileage,
            LatestOdometerReading = vehicle.LatestOdometerReading,
            Color = vehicle.Color,
            Seats = vehicle.Seats,
            Doors = vehicle.Doors,
            DrivetrainId = vehicle.Drivetrain?.Id,
            WheelSize = vehicle.WheelSize,
            TireSize = vehicle.TireSize,
        };
}

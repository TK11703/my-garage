using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Vehicles.StickerImport;

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

    // Features proposed from a window sticker; they travel as plain form fields (StickerFeatureChoice.ReadFrom), not through the model binder.
    public List<StickerFeatureChoice> StickerFeatures { get => field ??= []; set; }

    // Puts what the sticker provided over the current values; a value the sticker did not provide stays as it is.
    public void ApplyImport(StickerImportResultDto result)
    {
        Year = result.Year ?? Year;
        Make = result.Make ?? Make;
        Model = result.Model ?? Model;
        Trim = result.Trim ?? Trim;
        Vin = result.Vin ?? Vin;
        VehicleTypeId = result.VehicleTypeId ?? VehicleTypeId;
        Engine = result.Engine ?? Engine;
        Horsepower = result.Horsepower ?? Horsepower;
        FuelTypeId = result.FuelTypeId ?? FuelTypeId;
        TransmissionId = result.TransmissionId ?? TransmissionId;
        Color = result.Color ?? Color;
        Seats = result.Seats ?? Seats;
        Doors = result.Doors ?? Doors;
        DrivetrainId = result.DrivetrainId ?? DrivetrainId;
        WheelSize = result.WheelSize ?? WheelSize;
        TireSize = result.TireSize ?? TireSize;
        StickerFeatures = [.. result.Features.Select(StickerFeatureChoice.From)];
    }

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
            StickerFeatures = StickerFeatures.Any(row => row.Include) ? [.. StickerFeatures.Where(row => row.Include).Select(row => row.ToRequest())] : null,
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

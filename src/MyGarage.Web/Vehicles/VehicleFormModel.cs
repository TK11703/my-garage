using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

// Form state shared by the add and edit pages; the API validates and trims, so text is sent as entered.
public sealed class VehicleFormModel
{
    public string? Nickname { get; set; }

    public string? Make { get; set; }

    public string? Model { get; set; }

    public string? Trim { get; set; }

    public string? Color { get; set; }

    public int? Mileage { get; set; }

    public int? Doors { get; set; }

    public VehicleRequest ToRequest() =>
        new() { Nickname = Nickname, Make = Make, Model = Model, Trim = Trim, Color = Color, Mileage = Mileage, Doors = Doors };

    public static VehicleFormModel From(VehicleDetailDto vehicle) =>
        new()
        {
            Nickname = vehicle.Nickname,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Trim = vehicle.Trim,
            Color = vehicle.Color,
            Mileage = vehicle.Mileage,
            Doors = vehicle.Doors,
        };
}

using MyGarage.Database.Drafts;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.EmailIntake.Extraction;

public sealed record VehicleMatchResult(Guid? VehicleId, VehicleMatch Match);

// Finds which of the owner's own vehicles an extraction describes. Pure code: the AI model never sees the vehicle list (research R8, R14).
// A vehicle the owner chose when uploading comes first. Otherwise: exact VIN, then a unique year + make + model, then a unique make + model, then the owner's only vehicle, else none.
public static class VehicleMatcher
{
    public static VehicleMatchResult Match(IReadOnlyList<VehicleMatchCandidate> vehicles, VehicleHint? hint, Guid? preselectedVehicleId = null)
    {
        if (preselectedVehicleId is { } chosen && vehicles.Any(vehicle => vehicle.Id == chosen))
        {
            return new VehicleMatchResult(chosen, VehicleMatch.Selected);
        }

        if (hint is not null)
        {
            var vin = Normalize(hint.Vin);
            if (vin.Length > 0 && vehicles.FirstOrDefault(vehicle => Normalize(vehicle.Vin) == vin) is { } byVin)
            {
                return new VehicleMatchResult(byVin.Id, VehicleMatch.Vin);
            }

            var make = Normalize(hint.Make);
            var model = Normalize(hint.Model);
            if (make.Length > 0 && model.Length > 0)
            {
                var sameModel = vehicles.Where(vehicle => Normalize(vehicle.Make) == make && Normalize(vehicle.Model) == model).ToList();
                if (hint.Year is { } year && Unique(sameModel.Where(vehicle => vehicle.Year == year).ToList()) is { } byYear)
                {
                    return new VehicleMatchResult(byYear.Id, VehicleMatch.Description);
                }

                if (Unique(sameModel) is { } byModel)
                {
                    return new VehicleMatchResult(byModel.Id, VehicleMatch.Description);
                }
            }
        }

        return vehicles.Count == 1
            ? new VehicleMatchResult(vehicles[0].Id, VehicleMatch.OnlyVehicle)
            : new VehicleMatchResult(null, VehicleMatch.None);
    }

    private static VehicleMatchCandidate? Unique(IReadOnlyList<VehicleMatchCandidate> candidates) => candidates.Count == 1 ? candidates[0] : null;

    private static string Normalize(string? text) => (text ?? string.Empty).Trim().ToUpperInvariant();
}

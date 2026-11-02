using MyGarage.Database.Lookups;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

// Checks that the lookup items a request refers to exist, have the right kind, and are available to the caller.
public sealed class VehicleLookupChecker(LookupRepository lookups, VehicleRepository vehicles)
{
    // vehicleId is set when updating: an item the caller has hidden stays valid on a vehicle that already uses it.
    public async Task<Dictionary<string, string[]>> CheckAsync(string ownerId, VehicleRequest request, Guid? vehicleId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        (string Field, string Label, string Kind, Guid? Id, Func<VehicleRecord, Guid?> Stored)[] fields =
        [
            (nameof(VehicleRequest.VehicleTypeId), "vehicle type", LookupKind.VehicleType, request.VehicleTypeId, v => v.VehicleTypeId),
            (nameof(VehicleRequest.FuelTypeId), "fuel type", LookupKind.FuelType, request.FuelTypeId, v => v.FuelTypeId),
            (nameof(VehicleRequest.TransmissionId), "transmission", LookupKind.Transmission, request.TransmissionId, v => v.TransmissionId),
            (nameof(VehicleRequest.DrivetrainId), "drivetrain", LookupKind.Drivetrain, request.DrivetrainId, v => v.DrivetrainId),
        ];

        var requested = fields.Where(field => field.Id is not null).ToList();
        if (requested.Count == 0)
        {
            return errors;
        }

        var found = (await lookups.FindAvailableAsync(ownerId, [.. requested.Select(field => field.Id!.Value)], cancellationToken))
            .ToDictionary(item => item.Id);
        var stored = vehicleId is { } id ? await vehicles.GetByIdAsync(ownerId, id, cancellationToken) : null;

        foreach (var (field, label, kind, itemId, storedId) in requested)
        {
            var available = found.TryGetValue(itemId!.Value, out var item)
                            && item.Kind == kind
                            && (!item.IsHidden || (stored is not null && storedId(stored) == itemId));
            if (!available)
            {
                // Missing, wrong-kind, and another user's items all get this one message so nothing is disclosed.
                errors[field] = [$"The selected {label} is not available."];
            }
        }

        return errors;
    }
}

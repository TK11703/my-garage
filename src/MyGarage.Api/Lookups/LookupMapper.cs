using MyGarage.Database.Lookups;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Lookups;

public static class LookupMapper
{
    public static LookupItemDto ToDto(LookupItemRecord item) =>
        new() { Id = item.Id, Name = item.Name, IsDefault = item.IsDefault, IsHidden = item.IsHidden };

    // Records arrive ordered (defaults first, then the user's own); each kind keeps that order.
    public static LookupsDto ToLookupsDto(IEnumerable<LookupItemRecord> items)
    {
        var list = items.ToList();

        IReadOnlyList<LookupItemDto> Of(string kind) => list.Where(item => item.Kind == kind).Select(ToDto).ToList();

        return new LookupsDto
        {
            VehicleTypes = Of(LookupKind.VehicleType),
            FuelTypes = Of(LookupKind.FuelType),
            Transmissions = Of(LookupKind.Transmission),
            Drivetrains = Of(LookupKind.Drivetrain),
        };
    }

    // The stored kind for a route slug, or null for an unknown slug.
    public static string? ToStoredKind(string? slug) =>
        slug switch
        {
            LookupKinds.VehicleTypes => LookupKind.VehicleType,
            LookupKinds.FuelTypes => LookupKind.FuelType,
            LookupKinds.Transmissions => LookupKind.Transmission,
            LookupKinds.Drivetrains => LookupKind.Drivetrain,
            _ => null,
        };
}

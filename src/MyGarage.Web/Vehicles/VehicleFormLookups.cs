using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

// The choices offered by the vehicle form's dropdowns.
public sealed record VehicleFormLookups
{
    public static VehicleFormLookups Empty { get; } = new();

    public IReadOnlyList<LookupValueDto> VehicleTypes { get; init; } = [];

    public IReadOnlyList<LookupValueDto> FuelTypes { get; init; } = [];

    public IReadOnlyList<LookupValueDto> Transmissions { get; init; } = [];

    public IReadOnlyList<LookupValueDto> Drivetrains { get; init; } = [];

    // The user's shown items; an item the vehicle already uses stays selectable (marked hidden) when the user has hidden it.
    public static VehicleFormLookups From(LookupsDto lookups, VehicleDetailDto? vehicle = null) =>
        new()
        {
            VehicleTypes = Choices(lookups.VehicleTypes, vehicle?.VehicleType),
            FuelTypes = Choices(lookups.FuelTypes, vehicle?.FuelType),
            Transmissions = Choices(lookups.Transmissions, vehicle?.Transmission),
            Drivetrains = Choices(lookups.Drivetrains, vehicle?.Drivetrain),
        };

    private static IReadOnlyList<LookupValueDto> Choices(IReadOnlyList<LookupItemDto> items, LookupValueDto? current)
    {
        var choices = items
            .Where(item => !item.IsHidden)
            .Select(item => new LookupValueDto { Id = item.Id, Name = item.Name })
            .ToList();

        if (current is not null && choices.All(choice => choice.Id != current.Id))
        {
            choices.Add(new LookupValueDto { Id = current.Id, Name = $"{current.Name} (hidden)" });
        }

        return choices;
    }
}

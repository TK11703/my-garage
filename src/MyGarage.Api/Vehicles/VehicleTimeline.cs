using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Database.Common;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

// Query string of GET /api/vehicles/{id}/timeline: optional kind and paging.
public sealed record VehicleTimelineQuery(int Page = 1, int? PageSize = null, string? Kind = null);

public sealed class VehicleTimelineQueryValidator : AbstractValidator<VehicleTimelineQuery>
{
    public VehicleTimelineQueryValidator()
    {
        this.AddPagingRules(query => query.Page, query => query.PageSize);
        RuleFor(query => query.Kind)
            .Must(kind => kind is null || VehicleTimelineMapper.KindsByName.ContainsKey(kind))
            .WithMessage("Kind must be one of: " + string.Join(", ", VehicleTimelineMapper.KindsByName.Keys) + ".");
    }
}

public static class VehicleTimelineMapper
{
    // The query and stored text of each kind.
    internal static readonly IReadOnlyDictionary<string, VehicleTimelineKind> KindsByName = new Dictionary<string, VehicleTimelineKind>(StringComparer.Ordinal)
    {
        ["service"] = VehicleTimelineKind.Service,
        ["fuel-energy"] = VehicleTimelineKind.FuelEnergy,
        ["inspection"] = VehicleTimelineKind.Inspection,
        ["registration"] = VehicleTimelineKind.Registration,
        ["insurance"] = VehicleTimelineKind.Insurance,
        ["tax"] = VehicleTimelineKind.Tax,
        ["journal"] = VehicleTimelineKind.Journal,
    };

    public static VehicleTimelineEntryDto ToDto(VehicleTimelineRecord record) =>
        new()
        {
            Kind = KindsByName[record.Kind],
            Id = record.Id,
            OccurredOn = record.OccurredOn,
            Title = record.Title,
            Detail = record.Detail,
            Amount = record.Amount,
            OdometerReading = record.OdometerReading,
            FromEmail = record.FromEmail,
        };

    public static PagedResultDto<VehicleTimelineEntryDto> ToPageDto(PagedResult<VehicleTimelineRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}

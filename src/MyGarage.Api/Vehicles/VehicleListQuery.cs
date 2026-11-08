using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

// Query string of GET /api/vehicles: optional search text, filters, and paging.
public sealed record VehicleListQuery(
    string? Q = null,
    string? Make = null,
    string? Color = null,
    int? Doors = null,
    string? Tag = null,
    int? MinMileage = null,
    int? MaxMileage = null,
    int Page = 1,
    int? PageSize = null,
    int? MinYear = null,
    int? MaxYear = null,
    Guid? VehicleTypeId = null,
    Guid? FuelTypeId = null,
    Guid? TransmissionId = null,
    Guid? DrivetrainId = null,
    string? Status = null);

public sealed class VehicleListQueryValidator : AbstractValidator<VehicleListQuery>
{
    public VehicleListQueryValidator()
    {
        RuleFor(query => query.Q).TrimmedText("Search text", 100, required: false);
        RuleFor(query => query.Make).TrimmedText("Make", 50, required: false);
        RuleFor(query => query.Color).TrimmedText("Color", 30, required: false);
        RuleFor(query => query.Tag).TrimmedText("Tag", 30, required: false);
        RuleFor(query => query.Doors).InclusiveBetween(1, 6).WithMessage("Doors must be between 1 and 6.");
        RuleFor(query => query.MinMileage).InclusiveBetween(0, 9_999_999).WithMessage("Minimum mileage must be between 0 and 9,999,999.");
        RuleFor(query => query.MaxMileage).InclusiveBetween(0, 9_999_999).WithMessage("Maximum mileage must be between 0 and 9,999,999.");
        RuleFor(query => query.MinMileage)
            .Must((query, min) => min is null || query.MaxMileage is null || min <= query.MaxMileage)
            .WithMessage("Minimum mileage must not exceed maximum mileage.");
        RuleFor(query => query.MinYear).InclusiveBetween(1900, 2200).WithMessage("Minimum year must be between 1900 and 2200.");
        RuleFor(query => query.MaxYear).InclusiveBetween(1900, 2200).WithMessage("Maximum year must be between 1900 and 2200.");
        RuleFor(query => query.MinYear)
            .Must((query, min) => min is null || query.MaxYear is null || min <= query.MaxYear)
            .WithMessage("Minimum year must not exceed maximum year.");
        this.AddPagingRules(query => query.Page, query => query.PageSize);
        RuleFor(query => query.Status)
            .Must(VehicleStatusFilter.IsValid)
            .WithMessage(StatusMessage);
    }

    internal const string StatusMessage = "Status must be active, archived, or all.";
}

using FluentValidation;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Drafts.Uploads;

// multipart/form-data body of the upload endpoint.
public sealed class DraftUploadRequest
{
    public IFormFileCollection? Files { get; set; }

    // The caller's vehicle to propose on the drafts; omitted when uploading from the main screen.
    public Guid? VehicleId { get; set; }
}

// Counts and totals only: each file's type and size are judged when it is read, so one bad file never blocks the others.
public sealed class DraftUploadRequestValidator : AbstractValidator<DraftUploadRequest>
{
    public DraftUploadRequestValidator()
    {
        RuleFor(request => request.Files)
            .Must(files => files is { Count: > 0 }).WithMessage("Choose at least one file.")
            .Must(files => files is null || files.Count <= DraftUploadLimits.MaxFiles)
            .WithMessage($"Upload at most {DraftUploadLimits.MaxFiles} files at a time.")
            .Must(files => files is null || files.Sum(file => file.Length) <= DraftUploadLimits.MaxTotalBytes)
            .WithMessage($"The files must add up to {DraftUploadLimits.MaxTotalBytes / 1_048_576} MB or less.");

        RuleFor(request => request.VehicleId)
            .Must(vehicleId => vehicleId != Guid.Empty).WithMessage("vehicleId is not a vehicle.");
    }
}

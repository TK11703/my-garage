using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Common;

namespace MyGarage.Api.Vehicles.Images;

// multipart/form-data body of the upload endpoint.
public sealed class UploadImageRequest
{
    public const long MaxFileBytes = 5_242_880;

    public IFormFile? File { get; set; }

    public string? Caption { get; set; }
}

public sealed class UploadImageRequestValidator : AbstractValidator<UploadImageRequest>
{
    public UploadImageRequestValidator()
    {
        RuleFor(request => request.File)
            .NotNull().WithMessage("A file is required.")
            .Must(file => file is null || file.Length > 0).WithMessage("The file is empty.")
            .Must(file => file is null || file.Length <= UploadImageRequest.MaxFileBytes).WithMessage("The file must be 5 MB or smaller.");
        RuleFor(request => request.Caption).TrimmedText("Caption", 200, required: false);
    }
}

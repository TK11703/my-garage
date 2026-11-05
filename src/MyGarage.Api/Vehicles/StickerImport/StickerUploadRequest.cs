using FluentValidation;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Vehicles.StickerImport;

// multipart/form-data body of the extraction endpoint.
public sealed class StickerUploadRequest
{
    public IFormFile? File { get; set; }

    // The vehicle being edited; omitted when adding one.
    public Guid? VehicleId { get; set; }
}

public sealed class StickerUploadRequestValidator : AbstractValidator<StickerUploadRequest>
{
    public StickerUploadRequestValidator() =>
        RuleFor(request => request.File)
            .NotNull().WithMessage("A file is required.")
            .Must(file => file is null || file.Length > 0).WithMessage("The file is empty.");
}

// Rejects an oversized upload with 413 before the body is validated or read.
public sealed class StickerSizeFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<StickerUploadRequest>().FirstOrDefault();
        return request?.File is { Length: > StickerImportLimits.MaxFileBytes }
            ? ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file must be 10 MB or smaller."))
            : next(context);
    }
}

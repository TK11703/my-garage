using Microsoft.AspNetCore.Http;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Web.Vehicles.StickerImport;

// What the Import from sticker form post produced: values to put in the form, or a message to show instead.
public sealed record StickerImportAttempt(StickerImportResultDto? Result, StickerImportFailure Failure, string? Message, string Level)
{
    public static StickerImportAttempt Failed(StickerImportFailure failure, string message) =>
        new(null, failure, message, failure is StickerImportFailure.Invalid or StickerImportFailure.TooLarge ? "danger" : "warning");
}

// Reads the uploaded sticker from the posted form and hands it to the API; the file is only ever streamed through.
public static class StickerImportHandler
{
    public const string FileField = "stickerFile";

    public static async Task<StickerImportAttempt> RunAsync(HttpContext? http, StickerImportApiClient client, Guid? vehicleId, CancellationToken cancellationToken)
    {
        var file = http is { Request.HasFormContentType: true }
            ? (await http.Request.ReadFormAsync(cancellationToken)).Files[FileField]
            : null;
        if (file is null || file.Length == 0)
        {
            return StickerImportAttempt.Failed(StickerImportFailure.Invalid, StickerImportMessages.ChooseFile);
        }

        if (file.Length > StickerImportLimits.MaxFileBytes)
        {
            return StickerImportAttempt.Failed(StickerImportFailure.TooLarge, StickerImportMessages.TooLarge);
        }

        await using var stream = file.OpenReadStream();
        var response = await client.ExtractAsync(stream, file.ContentType, vehicleId, cancellationToken);
        if (response.Failure != StickerImportFailure.None)
        {
            return response.Failure == StickerImportFailure.NotFound
                ? new StickerImportAttempt(null, StickerImportFailure.NotFound, null, "warning")
                : StickerImportAttempt.Failed(response.Failure, StickerImportMessages.For(response.Failure)!);
        }

        return new StickerImportAttempt(response.Result, StickerImportFailure.None, null, "info");
    }
}

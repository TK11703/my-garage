using Microsoft.AspNetCore.Http;
using MyGarage.Shared.Drafts;

namespace MyGarage.Web.Drafts;

// A refused file, named by the name the user's browser sent (display only; always rendered escaped).
public sealed record DraftUploadRefusal(string FileName, string Message);

// What the upload form post produced: the accepted count and any refused files, or a message to show instead.
public sealed record DraftUploadAttempt(
    DraftUploadResultDto? Result, IReadOnlyList<DraftUploadRefusal> Refused, string? Message, string Level, bool VehicleNotFound = false)
{
    public static DraftUploadAttempt Failed(string message, string level = "danger") => new(null, [], message, level);
}

// Reads the uploaded files from the posted form, checks the limits, and hands them to the Api; the files are only ever streamed through.
public static class DraftUploadHandler
{
    public const string FileField = "recordFiles";

    public static async Task<DraftUploadAttempt> RunAsync(HttpContext? http, DraftUploadApiClient client, Guid? vehicleId, CancellationToken cancellationToken)
    {
        IReadOnlyList<IFormFile> files = http is { Request.HasFormContentType: true }
            ? (await http.Request.ReadFormAsync(cancellationToken)).Files.GetFiles(FileField)
            : [];
        var chosen = files.Where(file => file.FileName.Length > 0 || file.Length > 0).ToList();

        if (chosen.Count == 0)
        {
            return DraftUploadAttempt.Failed(DraftUploadMessages.ChooseFile);
        }

        if (chosen.Count > DraftUploadLimits.MaxFiles)
        {
            return DraftUploadAttempt.Failed(DraftUploadMessages.TooManyFiles);
        }

        if (chosen.Any(file => file.Length > DraftUploadLimits.MaxFileBytes))
        {
            return DraftUploadAttempt.Failed(DraftUploadMessages.FileTooLarge);
        }

        if (chosen.Sum(file => file.Length) > DraftUploadLimits.MaxTotalBytes)
        {
            return DraftUploadAttempt.Failed(DraftUploadMessages.TotalTooLarge);
        }

        var streams = chosen.Select(file => file.OpenReadStream()).ToList();
        try
        {
            var response = await client.UploadAsync([.. streams.Select((stream, index) => new UploadContent(stream, chosen[index].ContentType, DisplayName(chosen[index].FileName)))], vehicleId, cancellationToken);
            return response.Failure switch
            {
                DraftUploadFailure.None => new DraftUploadAttempt(
                    response.Result, [.. response.Result!.Refused.Select(refusal => Refusal(chosen, refusal.Index, DraftUploadMessages.ForReason(refusal.Reason)))], null, "info"),
                DraftUploadFailure.Invalid => Invalid(chosen, response.Errors),
                DraftUploadFailure.TooLarge => DraftUploadAttempt.Failed(DraftUploadMessages.TotalTooLarge),
                DraftUploadFailure.RateLimited => DraftUploadAttempt.Failed(DraftUploadMessages.RateLimited, "warning"),
                _ => new DraftUploadAttempt(null, [], null, "warning", VehicleNotFound: true),
            };
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static DraftUploadAttempt Invalid(IReadOnlyList<IFormFile> files, IReadOnlyDictionary<int, string> errors)
    {
        var refused = errors.Where(error => error.Key >= 0 && error.Key < files.Count).Select(error => Refusal(files, error.Key, error.Value)).ToList();
        return refused.Count > 0
            ? new DraftUploadAttempt(null, refused, null, "danger")
            : DraftUploadAttempt.Failed(errors.TryGetValue(-1, out var general) ? general : DraftUploadMessages.Invalid);
    }

    private static DraftUploadRefusal Refusal(IReadOnlyList<IFormFile> files, int index, string message) =>
        new(index >= 0 && index < files.Count ? DisplayName(files[index].FileName) : "A file", message);

    // A display name only: any path the browser sent is dropped.
    private static string DisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return name.Length == 0 ? "A file" : name;
    }
}

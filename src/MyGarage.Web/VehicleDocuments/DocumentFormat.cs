using System.Globalization;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.VehicleDocuments;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.VehicleDocuments;

public static class DocumentFormat
{
    public static string TypeLabel(string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? "PDF"
        : contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "Image"
        : "File";

    // "800 bytes", "117 KB", or "2.4 MB".
    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes.ToString(CultureInfo.InvariantCulture)} bytes"
        : bytes < 1024 * 1024 ? $"{(bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture)} KB"
        : $"{(bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture)} MB";

    // The record type the file was kept with, for example "From service record".
    public static string Source(DraftKind? kind) =>
        kind is { } value ? $"From {DraftFormat.KindLabel(value).ToLowerInvariant()}" : "Kept from an email";

    public static string KeptOn(VehicleDocumentDto document) =>
        $"Kept {MyGarage.Web.Common.DisplayFormat.Date(DateOnly.FromDateTime(document.CreatedAt.UtcDateTime))}";
}

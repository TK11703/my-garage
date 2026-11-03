using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Common;

// Streams a private file from the API to the signed-in browser. The bytes are untrusted, so they are never sniffed or cached.
// Images keep a sandbox policy; PDFs cannot, because Chrome's PDF viewer does not render under `Content-Security-Policy: sandbox`
// (the viewer already runs in an isolated process, and the file is the owner's own attachment). PDFs may only be framed by this site.
public static class PrivateFileProxy
{
    public static async Task<IResult> ServeAsync(Func<CancellationToken, Task<ImageDownload?>> open, HttpContext http, CancellationToken cancellationToken)
    {
        ImageDownload? download;
        try
        {
            download = await open(cancellationToken);
        }
        catch (GarageApiException ex)
        {
            return ex.Failure == GarageApiFailure.NotAccepted
                ? Results.StatusCode(StatusCodes.Status401Unauthorized)
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (download is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.ContentDisposition = "inline";
        if (string.Equals(download.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Headers.XFrameOptions = "SAMEORIGIN";
        }
        else
        {
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
        }

        return Results.Stream(
            async output =>
            {
                await using (download)
                {
                    await download.Stream.CopyToAsync(output, cancellationToken);
                }
            },
            download.ContentType);
    }
}

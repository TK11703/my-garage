using System.Diagnostics;
using System.Security.Claims;
using Azure.Core;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tags;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;
using OpenTelemetry.Metrics;

namespace MyGarage.Api.Vehicles.StickerImport;

public static class StickerImportEndpoints
{
    // A little above the 10 MB file limit to allow for the multipart envelope.
    private const long RequestSizeLimit = 11_534_336;

    public static IServiceCollection AddStickerImport(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<StickerUploadRequest>, StickerUploadRequestValidator>();
        services.AddSingleton<StickerResultMapper>();
        services.AddOptions<ExtractionOptions>().BindConfiguration(ExtractionOptions.SectionName);
        services.AddOptions<StickerImportOptions>().BindConfiguration(StickerImportOptions.SectionName);
        services.AddStickerImportRateLimiting();
        services.TryAddSingleton<TokenCredential>(sp => AzureCredentials.Create(sp.GetRequiredService<IHostEnvironment>()));

        // The user is waiting in the form and a retry would be billed twice, so this client has one short attempt and no retry.
        // RemoveAllResilienceHandlers is experimental, but it is the only way to replace the default handler that ServiceDefaults adds to every client.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<StickerExtractor>()
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(95);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(100);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            });
#pragma warning restore EXTEXP0001
        services.TryAddTransient<IStickerExtractor>(sp => sp.GetRequiredService<StickerExtractor>());
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(StickerImportMetrics.MeterName));
        return services;
    }

    public static IEndpointRouteBuilder MapStickerImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/vehicles/sticker-extractions", ExtractAsync)
            .WithTags("StickerImport")
            .DisableAntiforgery()
            .RequireRateLimiting(StickerImportRateLimiting.PolicyName)
            .Accepts<StickerUploadRequest>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(RequestSizeLimit))
            .AddEndpointFilter<StickerSizeFilter>()
            .AddEndpointFilter<ValidationFilter<StickerUploadRequest>>()
            .WithName("ExtractWindowSticker")
            .WithSummary("Read one window sticker (PDF or image) and return the vehicle details found on it; nothing is saved or kept.")
            .Produces<StickerImportResultDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private sealed record StickerRun(IResult Result, string Outcome, string? ContentType = null);

    private static async Task<IResult> ExtractAsync(
        [FromForm] StickerUploadRequest request,
        ClaimsPrincipal user,
        IStickerExtractor extractor,
        StickerResultMapper mapper,
        LookupRepository lookups,
        VehicleRepository vehicles,
        VehicleTagRepository tags,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var run = await RunAsync(request, user, extractor, mapper, lookups, vehicles, tags, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);

        // Only the outcome, the file's type and size, and the time are recorded: never the VIN, the features, or the file name.
        StickerImportMetrics.Record(run.Outcome, elapsed);
        loggers.CreateLogger("MyGarage.Api.Vehicles.StickerImport").LogInformation(
            "Sticker import finished: {Outcome} ({ContentType}, {SizeBytes} bytes) in {ElapsedMs} ms",
            run.Outcome,
            run.ContentType ?? "unknown",
            request.File?.Length ?? 0,
            (long)elapsed.TotalMilliseconds);
        return run.Result;
    }

    private static async Task<StickerRun> RunAsync(
        StickerUploadRequest request,
        ClaimsPrincipal user,
        IStickerExtractor extractor,
        StickerResultMapper mapper,
        LookupRepository lookups,
        VehicleRepository vehicles,
        VehicleTagRepository tags,
        CancellationToken cancellationToken)
    {
        if (CurrentOwner.Get(user) is not { } ownerId)
        {
            return new StickerRun(VehiclesEndpoints.NoOwner(), "refused");
        }

        // On an edit, the vehicle must be the caller's before its feature names are read or sent anywhere.
        IReadOnlyList<VehicleTagRecord> existingTags = [];
        if (request.VehicleId is { } vehicleId)
        {
            if (await vehicles.GetByIdAsync(ownerId, vehicleId, cancellationToken) is null)
            {
                return new StickerRun(VehiclesEndpoints.NotFound(), "refused");
            }

            existingTags = await tags.ListAsync(ownerId, vehicleId, cancellationToken);
        }

        if (!extractor.IsEnabled)
        {
            return new StickerRun(Unavailable("not_configured"), "unavailable");
        }

        var file = request.File!;
        await using var stream = file.OpenReadStream();
        var contentType = await DetectAsync(stream, cancellationToken);
        if (contentType is null)
        {
            return new StickerRun(
                Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The file must be a PDF, JPEG, PNG, or WebP."] }),
                "refused");
        }

        var extraction = await extractor.ExtractAsync(
            new StickerInput(stream, contentType, FileNameFor(contentType), existingTags.Count == 0 ? null : [.. existingTags.Select(tag => tag.Name)]),
            cancellationToken);
        if (extraction.IsFailure)
        {
            return new StickerRun(Unavailable(extraction.FailureReason!), "unavailable", contentType);
        }

        var shown = await lookups.ListAsync(ownerId, includeHidden: false, cancellationToken);
        var duplicateVin = extraction.Vin is { } vin
                           && string.Concat(vin.Where(character => !char.IsWhiteSpace(character))) is { Length: > 0 } normalized
                           && await vehicles.VinExistsAsync(ownerId, normalized, request.VehicleId, cancellationToken);
        var result = mapper.Map(extraction, shown, existingTags, duplicateVin);
        var outcome = result.Outcome switch
        {
            StickerImportOutcome.Filled => "filled",
            StickerImportOutcome.Partial => "partial",
            _ => "no_details",
        };
        return new StickerRun(Results.Ok(result), outcome, contentType);
    }
    // Identified by the file's leading bytes; the stream is rewound for the reader.
    private static async Task<string?> DetectAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = 0;
        while (read < header.Length)
        {
            var count = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        stream.Position = 0;
        return FileSignature.Detect(header.AsSpan(0, read));
    }

    // The user's file name is never sent on: the model needs only a name that matches the type.
    private static string FileNameFor(string contentType) => contentType switch
    {
        "application/pdf" => "window-sticker.pdf",
        "image/jpeg" => "window-sticker.jpg",
        "image/png" => "window-sticker.png",
        _ => "window-sticker.webp",
    };

    private static IResult Unavailable(string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: code switch
            {
                "not_configured" => "Reading window stickers is not set up.",
                "timeout" => "Reading the sticker took too long.",
                "rate_limited" => "The reading service is busy. Try again shortly.",
                _ => "The sticker could not be read right now.",
            },
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

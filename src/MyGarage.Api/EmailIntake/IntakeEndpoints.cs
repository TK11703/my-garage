using Azure.Core;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.EmailIntake;
using OpenTelemetry.Metrics;

namespace MyGarage.Api.EmailIntake;

public static class IntakeEndpoints
{
    // 15 MB of attachments become about 20 MB of base64 text, plus the email text and JSON framing.
    private const long RequestSizeLimit = 22_000_000;

    public static IServiceCollection AddEmailIntake(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<IntakeEmailRequest>, IntakeRequestValidator>();
        services.AddEmailIntakePolicy();
        services.AddIntakeRateLimiting();
        services.AddSingleton<IntakeService>();
        services.AddHostedService<IntakeContainersHostedService>();
        services.AddHostedService<OrphanBlobSweepService>();
        services.AddEmailExtraction();
        return services;
    }

    // The AI extractor, its options, and the background processor that turns queued submissions into drafts.
    private static void AddEmailExtraction(this IServiceCollection services)
    {
        services.AddOptions<ExtractionOptions>().BindConfiguration(ExtractionOptions.SectionName);
        services.AddOptions<SubmissionProcessingOptions>().BindConfiguration(SubmissionProcessingOptions.SectionName);

        // No key anywhere: the managed identity (or the developer's sign-in) gets the token. Tests replace IDraftExtractor, so this is never built there.
        services.TryAddSingleton<TokenCredential>(sp => AzureCredentials.Create(sp.GetRequiredService<IHostEnvironment>()));

        // The processor is a singleton, so the client is long-lived: connections are recycled by lifetime instead of by handler rotation.
        // The model call takes far longer than the 10 s default of the standard resilience handler, so this client gets its own pipeline.
        // RemoveAllResilienceHandlers is experimental, but it is the only way to replace the default handler that ServiceDefaults adds to every client.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<ResponsesApiExtractor>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
                options.Retry.MaxRetryAttempts = 2;
            });
#pragma warning restore EXTEXP0001
        services.TryAddSingleton<IDraftExtractor>(sp => sp.GetRequiredService<ResponsesApiExtractor>());

        services.AddSingleton<SubmissionProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<SubmissionProcessor>());
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(EmailIntakeMetrics.MeterName));
    }

    public static IEndpointRouteBuilder MapEmailIntakeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The only endpoint group that is not for signed-in users: the named policy requires the Email.Ingest app role.
        var group = endpoints.MapGroup("/api/intake")
            .WithTags("EmailIntake")
            .RequireAuthorization(EmailIntakeAuthorization.PolicyName)
            .RequireRateLimiting(IntakeRateLimiting.PolicyName)
            .AddEndpointFilter<IntakeStorageFilter>();

        group.MapPost("/emails", async (IntakeEmailRequest request, IntakeService intake, CancellationToken cancellationToken) =>
                Results.Accepted(value: await intake.AcceptAsync(request, cancellationToken)))
            .WithMetadata(new RequestSizeLimitAttribute(RequestSizeLimit), new BodySizeLimit(RequestSizeLimit))
            .AddEndpointFilter<ValidationFilter<IntakeEmailRequest>>()
            .WithName("IntakeEmail")
            .WithSummary("Hand over one transformed email; the answer is the same whether or not the sender is a known user.")
            .Produces<IntakeAcknowledgement>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

// Answers 503 with Retry-After when attachment storage is unreachable, so the workflow retries the whole message.
public sealed class IntakeStorageFilter(ILogger<IntakeStorageFilter> logger) : IEndpointFilter
{
    private const string RetryAfterSeconds = "30";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (IntakeStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Email attachment storage is unavailable");
            context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds;
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage is temporarily unavailable. Retry the request.");
        }
    }
}

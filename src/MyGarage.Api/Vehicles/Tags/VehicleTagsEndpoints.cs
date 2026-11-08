using System.Security.Claims;
using Azure.Core;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

public static class VehicleTagsEndpoints
{
    private const int MaxSuggestions = 10;

    public static IServiceCollection AddVehicleTags(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<AddVehicleTagRequest>, AddVehicleTagRequestValidator>();
        services.AddOptions<ExtractionOptions>().BindConfiguration(ExtractionOptions.SectionName);
        services.AddOptions<FeatureCategorizationOptions>().BindConfiguration(FeatureCategorizationOptions.SectionName);
        services.TryAddSingleton<TokenCredential>(sp => AzureCredentials.Create(sp.GetRequiredService<IHostEnvironment>()));

        // A person may be waiting on this call and a retry would be billed twice, so it has one short attempt and no retry.
        // RemoveAllResilienceHandlers is experimental, but it is the only way to replace the default handler that ServiceDefaults adds to every client.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<FeatureCategorizer>()
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(40);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            });
#pragma warning restore EXTEXP0001
        services.TryAddTransient<IFeatureCategorizer>(sp => sp.GetRequiredService<FeatureCategorizer>());
        services.AddSingleton<FeatureCategorizationService>();
        services.AddHostedService<FeatureCategorizationSweep>();
        return services;
    }

    public static IEndpointRouteBuilder MapVehicleTagsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Features are shared by every user, so only their names are returned and never which vehicle or owner uses them.
        endpoints.MapGet("/api/features", async (
                string? query,
                VehicleTagRepository tags,
                CancellationToken cancellationToken) =>
            {
                var text = query?.Trim();
                if (string.IsNullOrEmpty(text) || text.Length > AddVehicleTagRequestValidator.MaxNameLength)
                {
                    return Results.Ok(Array.Empty<string>());
                }

                return Results.Ok(await tags.SuggestAsync(text, MaxSuggestions, cancellationToken));
            })
            .WithTags("Tags")
            .WithName("SuggestFeatures")
            .WithSummary($"Names of shared features used on vehicles that contain the query text (at most {MaxSuggestions}).")
            .Produces<string[]>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/tags").WithTags("Tags").RequireActiveVehicleForWrites();

        group.MapPost("", async (
                Guid vehicleId,
                AddVehicleTagRequest request,
                ClaimsPrincipal user,
                VehicleTagRepository tags,
                FeatureCategorizationService categorization,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await tags.AddAsync(
                    ownerId, vehicleId, Guid.CreateVersion7(), request.Name!.Trim(), timeProvider.GetUtcNow(), cancellationToken);

                if (result is { Status: AddTagStatus.Added, Tag: { Category: null } added })
                {
                    // Best effort: a failure leaves the feature under Other and the background sweep tries again.
                    await categorization.CategorizePendingAsync(cancellationToken);
                    var current = (await tags.ListAsync(ownerId, vehicleId, cancellationToken)).FirstOrDefault(tag => tag.Id == added.Id);
                    result = result with { Tag = current ?? added };
                }

                return result.Status switch
                {
                    AddTagStatus.Added => Results.Created($"/api/vehicles/{vehicleId}", VehicleTagMapper.ToDto(result.Tag!)),
                    AddTagStatus.Duplicate => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This vehicle already has that feature."),
                    AddTagStatus.LimitReached => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"A vehicle can have at most {VehicleTagRepository.MaxTagsPerVehicle} features."),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .AddEndpointFilter<ValidationFilter<AddVehicleTagRequest>>()
            .WithName("AddVehicleTag")
            .WithSummary($"Add a free-text tag (max {VehicleTagRepository.MaxTagsPerVehicle} per vehicle, unique ignoring case).")
            .Produces<VehicleTagDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{tagId:guid}", async (
                Guid vehicleId,
                Guid tagId,
                ClaimsPrincipal user,
                VehicleTagRepository tags,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await tags.RemoveAsync(ownerId, vehicleId, tagId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("RemoveVehicleTag")
            .WithSummary("Remove a tag.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

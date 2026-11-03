using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.VehicleDocuments;

namespace MyGarage.Api.VehicleDocuments;

public static class VehicleDocumentsEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddVehicleDocuments(this IServiceCollection services)
    {
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        services.AddHostedService<DocumentContainerHostedService>();
        return services;
    }

    public static IEndpointRouteBuilder MapVehicleDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/documents")
            .WithTags("VehicleDocuments")
            .AddEndpointFilter<DocumentStorageFilter>();

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                VehicleDocumentRepository documents,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await documents.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await documents.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(new PagedResultDto<VehicleDocumentDto>
                {
                    Items = [.. result!.Items.Select(VehicleDocumentMapper.ToDto)],
                    Page = result.Page,
                    PageSize = result.PageSize,
                    TotalCount = result.TotalCount,
                    TotalPages = result.TotalPages,
                });
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListVehicleDocuments")
            .WithSummary("Originals the user kept for this vehicle (not vehicle pictures), newest first.")
            .Produces<PagedResultDto<VehicleDocumentDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{documentId:guid}/content", async (
                Guid vehicleId,
                Guid documentId,
                ClaimsPrincipal user,
                HttpContext http,
                VehicleDocumentRepository documents,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var content = await documents.OpenContentAsync(ownerId, vehicleId, documentId, cancellationToken);
                if (content is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers.CacheControl = "private, no-store";
                return Results.Stream(content.Content, content.ContentType);
            })
            .WithName("GetVehicleDocumentContent")
            .WithSummary("Document bytes streamed from private storage with the stored content type.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/{documentId:guid}", async (
                Guid vehicleId,
                Guid documentId,
                ClaimsPrincipal user,
                VehicleDocumentRepository documents,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await documents.DeleteAsync(ownerId, vehicleId, documentId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteVehicleDocument")
            .WithSummary("Delete a kept document and its file; the record it was kept with is unchanged.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

// Answers 503 problem details when the document store cannot be reached, so nothing else about the vehicle is affected.
public sealed class DocumentStorageFilter(ILogger<DocumentStorageFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DocumentStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Document storage is unavailable");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Document storage is temporarily unavailable.");
        }
    }
}

// Makes sure the private blob container exists; a storage outage at startup must not stop the rest of the API.
public sealed class DocumentContainerHostedService(IVehicleDocumentBlobStore store, ILogger<DocumentContainerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.EnsureContainerAsync(cancellationToken);
        }
        catch (DocumentStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "The document container could not be prepared; document features will fail until storage is reachable");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

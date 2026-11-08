using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Reminders;
using MyGarage.Api.Vehicles;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.ServiceRecords;

public static class ServiceRecordsEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddServiceRecords(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<ServiceRecordRequest>, ServiceRecordRequestValidator>();
        services.AddSingleton<IValidator<ServiceLineItemRequest>, ServiceLineItemRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapServiceRecordsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/service-records").WithTags("ServiceRecords");

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await records.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await records.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(ServiceRecordMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListServiceRecords")
            .WithSummary("Page of the vehicle's service records, most recent Date Serviced first.")
            .Produces<PagedResultDto<ServiceRecordSummaryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                ServiceRecordRequest request,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                TimeProvider timeProvider,
                ReminderEvaluator reminderEvaluator,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var detail = await records.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    request.DateServiced!.Value,
                    request.OdometerReading!.Value,
                    ServiceRecordMapper.CleanShop(request.ShopOrPerson),
                    ServiceRecordMapper.CleanTax(request.Tax),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                if (detail is not null)
                {
                    await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                }

                return detail is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/service-records/{detail.Record.Id}", ServiceRecordMapper.ToDto(detail));
            })
            .AddEndpointFilter<ValidationFilter<ServiceRecordRequest>>()
            .WithName("CreateServiceRecord")
            .WithSummary("Add a service record (metadata only) to the vehicle.")
            .Produces<ServiceRecordDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{recordId:guid}", async (
                Guid vehicleId,
                Guid recordId,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var detail = await records.GetAsync(ownerId, vehicleId, recordId, cancellationToken);
                return detail is null ? VehiclesEndpoints.NotFound() : Results.Ok(ServiceRecordMapper.ToDto(detail));
            })
            .WithName("GetServiceRecord")
            .WithSummary("One service record with its line items, parts, and total cost.")
            .Produces<ServiceRecordDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{recordId:guid}", async (
                Guid vehicleId,
                Guid recordId,
                ServiceRecordRequest request,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                TimeProvider timeProvider,
                ReminderEvaluator reminderEvaluator,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var detail = await records.UpdateAsync(
                    ownerId,
                    vehicleId,
                    recordId,
                    request.DateServiced!.Value,
                    request.OdometerReading!.Value,
                    ServiceRecordMapper.CleanShop(request.ShopOrPerson),
                    ServiceRecordMapper.CleanTax(request.Tax),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                if (detail is not null)
                {
                    await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                }

                return detail is null ? VehiclesEndpoints.NotFound() : Results.Ok(ServiceRecordMapper.ToDto(detail));
            })
            .AddEndpointFilter<ValidationFilter<ServiceRecordRequest>>()
            .WithName("UpdateServiceRecord")
            .WithSummary("Edit the record's metadata; line items are unchanged.")
            .Produces<ServiceRecordDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{recordId:guid}", async (
                Guid vehicleId,
                Guid recordId,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                ReminderEvaluator reminderEvaluator,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                if (!await records.DeleteAsync(ownerId, vehicleId, recordId, cancellationToken))
                {
                    return VehiclesEndpoints.NotFound();
                }

                await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteServiceRecord")
            .WithSummary("Delete the record and all of its line items and parts.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{recordId:guid}/line-items", async (
                Guid vehicleId,
                Guid recordId,
                ServiceLineItemRequest request,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await records.AddLineItemAsync(
                    ownerId,
                    vehicleId,
                    recordId,
                    Guid.CreateVersion7(),
                    ServiceRecordMapper.ToInput(request),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return result.Outcome switch
                {
                    AddLineItemOutcome.Created => Results.Created(
                        $"/api/vehicles/{vehicleId}/service-records/{recordId}", ServiceRecordMapper.ToDto(result.Detail!)),
                    AddLineItemOutcome.LimitReached => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"A service record can have up to {ServiceRecordRepository.MaxLineItems} line items."),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .AddEndpointFilter<ValidationFilter<ServiceLineItemRequest>>()
            .WithName("AddServiceLineItem")
            .WithSummary("Add a line item priced by a flat cost or by labor and parts.")
            .Produces<ServiceRecordDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{recordId:guid}/line-items/{lineItemId:guid}", async (
                Guid vehicleId,
                Guid recordId,
                Guid lineItemId,
                ServiceLineItemRequest request,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var detail = await records.UpdateLineItemAsync(
                    ownerId,
                    vehicleId,
                    recordId,
                    lineItemId,
                    ServiceRecordMapper.ToInput(request),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return detail is null ? VehiclesEndpoints.NotFound() : Results.Ok(ServiceRecordMapper.ToDto(detail));
            })
            .AddEndpointFilter<ValidationFilter<ServiceLineItemRequest>>()
            .WithName("UpdateServiceLineItem")
            .WithSummary("Replace the line item's description, pricing, and parts.")
            .Produces<ServiceRecordDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{recordId:guid}/line-items/{lineItemId:guid}", async (
                Guid vehicleId,
                Guid recordId,
                Guid lineItemId,
                ClaimsPrincipal user,
                ServiceRecordRepository records,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await records.DeleteLineItemAsync(ownerId, vehicleId, recordId, lineItemId, cancellationToken)
                    ? Results.NoContent()
                    : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteServiceLineItem")
            .WithSummary("Delete the line item and its parts.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

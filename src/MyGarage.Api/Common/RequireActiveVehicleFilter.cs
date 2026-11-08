using MyGarage.Database.Vehicles;

namespace MyGarage.Api.Common;

// Marks a route as refusing changes while its vehicle is archived; the route-walking test looks for it.
public sealed class ActiveVehicleRequiredMetadata
{
    public static ActiveVehicleRequiredMetadata Instance { get; } = new();
}

public static class VehicleArchivedProblem
{
    public const string Type = "urn:mygarage:problem:vehicle-archived";

    public static IResult Result() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, type: Type, title: "This vehicle is archived. Restore it to change it.");
}

// An archived vehicle is read-only: any change under it is refused before the endpoint runs.
// A vehicle that is absent or not the caller's falls through, so the endpoint answers its normal 404 and nothing is disclosed.
public sealed class RequireActiveVehicleFilter(VehicleRepository vehicles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
        {
            return await next(context);
        }

        if (CurrentOwner.Get(http.User) is { } ownerId
            && Guid.TryParse(http.GetRouteValue("vehicleId")?.ToString(), out var vehicleId)
            && await vehicles.GetArchivedStateAsync(ownerId, vehicleId, http.RequestAborted) == true)
        {
            return VehicleArchivedProblem.Result();
        }

        return await next(context);
    }
}

public static class RequireActiveVehicleExtensions
{
    // Applies to a route group or a single route under /api/vehicles/{vehicleId}.
    public static TBuilder RequireActiveVehicleForWrites<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter<TBuilder, RequireActiveVehicleFilter>();
        builder.Add(endpoint => endpoint.Metadata.Add(ActiveVehicleRequiredMetadata.Instance));
        builder.ProducesProblem(StatusCodes.Status409Conflict);
        return builder;
    }
}

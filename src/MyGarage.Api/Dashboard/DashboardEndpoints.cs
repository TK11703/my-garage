using System.Security.Claims;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Shared.Dashboard;

namespace MyGarage.Api.Dashboard;

public static class DashboardEndpoints
{
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.AddSingleton<DashboardService>();
        return services;
    }

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard").WithTags("Dashboard");

        group.MapGet("", async (ClaimsPrincipal user, DashboardService dashboard, CancellationToken cancellationToken) =>
                CurrentOwner.Get(user) is { } ownerId
                    ? Results.Ok(await dashboard.GetAsync(ownerId, cancellationToken))
                    : VehiclesEndpoints.NoOwner())
            .WithName("GetDashboard")
            .WithSummary("The caller's garage at a glance: counts and the reminders needing attention.")
            .Produces<DashboardDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}

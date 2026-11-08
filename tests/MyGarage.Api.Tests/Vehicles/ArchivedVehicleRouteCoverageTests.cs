using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Api.Common;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.Vehicles;

// Every change route under a vehicle, read from the API's own endpoint table, so a route added later is covered automatically.
public static class VehicleWriteRoutes
{
    public sealed record Route(string Method, string Pattern, bool Guarded)
    {
        public override string ToString() => $"{Method} {Pattern}";
    }

    // The only change routes under a vehicle that stay open on an archived vehicle.
    public static bool IsAllowListed(string method, string pattern) =>
        pattern.EndsWith("/archive", StringComparison.Ordinal)
        || pattern.EndsWith("/restore", StringComparison.Ordinal)
        || (method == "DELETE" && pattern == "/api/vehicles/{vehicleId:guid}");

    public static List<Route> All(ApiFactory factory) =>
        [.. factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is { } text && text.StartsWith("/api/vehicles/{vehicleId", StringComparison.Ordinal))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => method is not ("GET" or "HEAD" or "OPTIONS"))
                .Select(method => new Route(method, endpoint.RoutePattern.RawText!, endpoint.Metadata.GetMetadata<ActiveVehicleRequiredMetadata>() is not null)))
            .Distinct()
            .OrderBy(route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)];
}

public class ArchivedVehicleRouteCoverageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void EveryChangeRouteUnderAVehicle_CarriesTheGuard_ExceptArchiveRestoreAndDelete()
    {
        var routes = VehicleWriteRoutes.All(factory);

        Assert.True(routes.Count > 30, $"Expected the endpoint walk to find the vehicle change routes, found {routes.Count}.");
        var uncovered = routes.Where(route => !route.Guarded && !VehicleWriteRoutes.IsAllowListed(route.Method, route.Pattern)).Select(route => route.ToString()).ToList();
        Assert.True(uncovered.Count == 0, "Change routes without RequireActiveVehicleForWrites: " + string.Join("; ", uncovered));
    }

    [Fact]
    public void TheAllowListedRoutes_AreNotGuarded_SoArchiveRestoreAndDeleteWorkOnAnArchivedVehicle()
    {
        var routes = VehicleWriteRoutes.All(factory);

        var allowListed = routes.Where(route => VehicleWriteRoutes.IsAllowListed(route.Method, route.Pattern)).ToList();
        Assert.Equal(3, allowListed.Count);
        Assert.All(allowListed, route => Assert.False(route.Guarded, route.ToString()));
        Assert.Contains(routes, route => route is { Method: "PUT", Pattern: "/api/vehicles/{vehicleId:guid}", Guarded: true });
    }
}

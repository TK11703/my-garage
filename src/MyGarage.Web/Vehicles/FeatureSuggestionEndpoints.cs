using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

// Feeds the feature box's suggestions. Only names of shared features come back, and only for signed-in users.
public static class FeatureSuggestionEndpoints
{
    public static IEndpointRouteBuilder MapFeatureSuggestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/features/suggest", SuggestAsync)
            .WithName("SuggestFeatures");

        return endpoints;
    }

    public static async Task<IResult> SuggestAsync(string? query, VehiclesApiClient api, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.Ok(Array.Empty<string>());
        }

        try
        {
            var result = await api.SuggestFeaturesAsync(query.Trim(), cancellationToken);
            return Results.Ok(result.Value ?? []);
        }
        catch (GarageApiException)
        {
            // Suggestions are a convenience: typing a name and adding it still works without them.
            return Results.Ok(Array.Empty<string>());
        }
    }
}

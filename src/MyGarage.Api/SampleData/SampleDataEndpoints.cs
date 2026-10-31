using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.SampleData;

namespace MyGarage.Api.SampleData;

public static class SampleDataEndpoints
{
    public static IServiceCollection AddSampleData(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<SampleDataRequest>, SampleDataRequestValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapSampleDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sample-data").WithTags("SampleData");

        group.MapGet("", ([AsParameters] SampleDataRequest request) =>
                TypedResults.Ok(SampleDataProvider.GetItems(request.Count)))
            .AddEndpointFilter<ValidationFilter<SampleDataRequest>>()
            .WithName("GetSampleData")
            .WithSummary("Fixed sample data used to prove the API is reachable.")
            .Produces<IReadOnlyList<SampleItemDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}

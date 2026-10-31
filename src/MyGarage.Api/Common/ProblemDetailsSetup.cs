namespace MyGarage.Api.Common;

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddGarageProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();

        // Binding failures (for example a non-numeric query value) answer 400 problem details instead of throwing.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        return services;
    }

    // Writes RFC 9457 problem details for unhandled exceptions and for empty 4xx/5xx responses (401, 403, 404).
    public static WebApplication UseGarageProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MyGarage.Api.Common;

// Bounds what a leaked workflow credential or a runaway workflow can submit; the workflow's retry policy handles the 429.
public static class IntakeRateLimiting
{
    public const string PolicyName = "intake";
    public const int RequestsPerMinute = 120;

    public static IServiceCollection AddIntakeRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.OnRejected = WriteRejectionAsync;
        });
        return services;
    }

    // The 429 answer both rate-limit policies give: ProblemDetails with Retry-After.
    public static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : TimeSpan.FromMinutes(1);
        response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests. Retry after the delay given in Retry-After." },
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }

    // One window per calling application (the azp or appid claim), else per remote address.
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst("azp")?.Value
        ?? context.User.FindFirst("appid")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

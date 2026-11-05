using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using MyGarage.Api.Vehicles.StickerImport;

namespace MyGarage.Api.Common;

// Reading a sticker is a paid, slow call, so one user cannot start more than StickerImport:RequestsPerMinute of them a minute.
public static class StickerImportRateLimiting
{
    public const string PolicyName = "sticker-import";

    public static IServiceCollection AddStickerImportRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, context.RequestServices.GetRequiredService<IOptions<StickerImportOptions>>().Value.RequestsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.OnRejected = IntakeRateLimiting.WriteRejectionAsync;
        });
        return services;
    }

    // One window per signed-in user, taken from the validated token; the address only for a token with no user.
    private static string PartitionKey(HttpContext context) =>
        CurrentOwner.Get(context.User) is { } owner ? $"user:{owner}" : $"ip:{context.Connection.RemoteIpAddress}";
}

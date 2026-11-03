namespace MyGarage.Api.Common;

// Endpoint metadata read by UseBodySizeLimit: the largest request body the endpoint accepts.
public sealed record BodySizeLimit(long MaxBytes);

public static class BodySizeLimitExtensions
{
    // Answers 413 problem details before the body is read when the declared length is over the endpoint's limit, and caps
    // undeclared (chunked) bodies where the server allows it. Must run after routing, so it is placed after authorization.
    public static IApplicationBuilder UseBodySizeLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<BodySizeLimit>() is { } limit)
            {
                if (context.Request.ContentLength > limit.MaxBytes)
                {
                    await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The request body is too large.").ExecuteAsync(context);
                    return;
                }

                if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                {
                    feature.MaxRequestBodySize = limit.MaxBytes;
                }
            }

            await next(context);
        });
}

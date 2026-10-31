using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace MyGarage.Web.Authentication;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/authentication");

        group.MapGet("/login", (string? returnUrl) =>
                Results.Challenge(
                    new AuthenticationProperties { RedirectUri = ReturnUrlValidator.Sanitize(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        group.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Problem("The sign-out request could not be validated.", statusCode: StatusCodes.Status400BadRequest);
                }

                // Evicts pages the browser kept for the back button so they cannot be shown after sign-out.
                context.Response.Headers["Clear-Site-Data"] = "\"cache\"";

                return Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/authentication/signed-out" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .RequireAuthorization();

        return endpoints;
    }
}

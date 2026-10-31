using System.Security.Claims;
using MyGarage.Shared.Users;

namespace MyGarage.Api.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/me", (ClaimsPrincipal user) =>
                CurrentUserMapper.Map(user) is { } current
                    ? Results.Ok(current)
                    : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token does not identify a user."))
            .WithName("GetCurrentUser")
            .WithSummary("Identity of the caller, derived from the validated access token.")
            .Produces<CurrentUserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}

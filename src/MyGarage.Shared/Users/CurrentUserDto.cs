namespace MyGarage.Shared.Users;

public sealed class CurrentUserDto
{
    public required string UserId { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }
}

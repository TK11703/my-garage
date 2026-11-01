namespace MyGarage.Web.Vehicles;

public enum ApiOutcome
{
    Success,
    NotFound,
    Invalid,
    Conflict,
}

// A call the API answered; sign-in rejections and outages are thrown as GarageApiException instead.
public sealed class ApiResult<T>
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public ApiOutcome Outcome { get; init; }

    public T? Value { get; init; }

    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = NoErrors;

    public string? Title { get; init; }

    public bool IsSuccess => Outcome == ApiOutcome.Success;

    public bool IsNotFound => Outcome == ApiOutcome.NotFound;

    public string? FirstError(string field) =>
        Errors.FirstOrDefault(pair => string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
}

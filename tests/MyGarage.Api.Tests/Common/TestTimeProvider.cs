namespace MyGarage.Api.Tests.Common;

// A clock the test moves by hand (the repo avoids a fake-time package).
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

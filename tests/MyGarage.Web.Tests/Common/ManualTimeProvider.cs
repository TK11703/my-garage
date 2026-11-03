namespace MyGarage.Web.Tests.Common;

// A clock the test moves by hand; timers created from it fire only when the test advances time.
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public int ActiveTimers => timers.Count(timer => !timer.IsDisposed);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, now + dueTime, period);
        timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = now + by;
        while (true)
        {
            var next = timers.Where(timer => !timer.IsDisposed && timer.Due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
            if (next is null)
            {
                break;
            }

            now = next.Due;
            next.Fire();
        }

        now = target;
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset due, TimeSpan period) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = due;

        public bool IsDisposed { get; private set; }

        public void Fire()
        {
            Due = period > TimeSpan.Zero ? Due + period : DateTimeOffset.MaxValue;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => true;

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
